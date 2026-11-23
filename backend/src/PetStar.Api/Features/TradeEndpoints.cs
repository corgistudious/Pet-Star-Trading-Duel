using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PetStar.Api.Data;
using PetStar.Api.Infrastructure;

namespace PetStar.Api.Features;

public sealed class TradeOfferRequest { public long Uid { get; set; } public List<long> OfferCardIds { get; set; } = new(); public List<long> RequestCardIds { get; set; } = new(); public string? Note { get; set; } }

/// <summary>Card-for-card trades ("Trao Đổi") inside the Pet market. No currency changes hands and nothing can be cashed out.
/// Ownership, locks, listings and active matches are re-checked under row locks when the receiver accepts, and both sides
/// move in one transaction (all or nothing).</summary>
public static class TradeEndpoints
{
    public const int MaxCardsPerSide = 5;
    private static List<long> Ids(string json) => JsonSerializer.Deserialize<List<long>>(json) ?? new();

    public static void Map(WebApplication app)
    {
        var g = ServerEndpoints.GameGroup(app);

        g.MapPost("/market/trades", async (TradeOfferRequest req, HttpContext ctx, PetStarDb db, ContentGuard guard, NotificationService notes) =>
        {
            var (sid, mid) = ctx.User.Player();
            await guard.EnsureAllowed(ctx.User.AccountId(), "market");
            var offer = req.OfferCardIds.Distinct().ToList(); var want = req.RequestCardIds.Distinct().ToList();
            if (offer.Count is < 1 or > MaxCardsPerSide || want.Count is < 1 or > MaxCardsPerSide) throw ApiException.BadRequest("trade_cards_1_to_5");
            var other = await SocialEndpoints.Member(db, req.Uid, sid);
            if (other.Id == mid) throw ApiException.BadRequest("cannot_trade_self");
            if (await MarketEndpoints.IsBlocked(db, mid, other.Id)) throw ApiException.Forbidden("user_blocked");
            var note = ContentGuard.Clean(req.Note, 120, false);
            var now = DateTimeOffset.UtcNow;
            if (await db.TradeOffers.CountAsync(t => t.FromMembershipId == mid && t.CreatedAt > now.AddHours(-1)) >= 10) throw new ApiException(429, "trading_too_fast");
            if (await db.TradeOffers.CountAsync(t => t.FromMembershipId == mid && t.Status == "pending") >= 10) throw ApiException.Conflict("too_many_open_trades");
            await CheckSide(db, offer, mid, sid);
            await CheckSide(db, want, other.Id, sid);
            var t = new TradeOffer { ServerId = sid, FromMembershipId = mid, ToMembershipId = other.Id, OfferCardIds = JsonSerializer.Serialize(offer), RequestCardIds = JsonSerializer.Serialize(want), Note = note, CreatedAt = now, ExpiresAt = now.AddHours(48) };
            db.TradeOffers.Add(t);
            await db.SaveChangesAsync();
            await notes.Notify(other.Id, "trade_offer", new { tradeId = t.Id, uid = ctx.User.AccountId() }, "trade:" + t.Id);
            return Results.Ok(new { tradeId = t.Id, status = t.Status, t.ExpiresAt });
        });

        g.MapGet("/market/trades", async (string? box, HttpContext ctx, PetStarDb db, GameData data) =>
        {
            var (_, mid) = ctx.User.Player();
            var now = DateTimeOffset.UtcNow;
            await db.TradeOffers.Where(t => t.Status == "pending" && t.ExpiresAt < now && (t.FromMembershipId == mid || t.ToMembershipId == mid))
                .ExecuteUpdateAsync(u => u.SetProperty(t => t.Status, "expired").SetProperty(t => t.ClosedAt, now));
            var q = db.TradeOffers.AsNoTracking().Where(t => t.FromMembershipId == mid || t.ToMembershipId == mid);
            q = (box ?? "incoming") switch
            {
                "incoming" => q.Where(t => t.ToMembershipId == mid && t.Status == "pending"),
                "outgoing" => q.Where(t => t.FromMembershipId == mid && t.Status == "pending"),
                "history" => q.Where(t => t.Status != "pending"),
                _ => throw ApiException.BadRequest("invalid_box"),
            };
            var rows = await q.OrderByDescending(t => t.Id).Take(50).ToListAsync();
            var allIds = rows.SelectMany(r => Ids(r.OfferCardIds).Concat(Ids(r.RequestCardIds))).Distinct().ToList();
            var cards = await db.Cards.AsNoTracking().Where(c => allIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id);
            var badges = await SocialAccess.Badges(db, rows.SelectMany(r => new[] { r.FromMembershipId, r.ToMembershipId }));
            object Side(string json) => Ids(json).Where(cards.ContainsKey).Select(id => data.ToDto(cards[id])).ToList();
            return rows.Select(r => new { r.Id, r.Status, r.Note, r.CreatedAt, r.ExpiresAt, r.ClosedAt, from = badges[r.FromMembershipId], to = badges[r.ToMembershipId], mine = r.FromMembershipId == mid, offer = Side(r.OfferCardIds), request = Side(r.RequestCardIds) });
        });

        g.MapPost("/market/trades/{id:long}/accept", async (long id, HttpContext ctx, PetStarDb db, NotificationService notes) =>
        {
            var (sid, mid) = ctx.User.Player();
            await using var tx = await db.Database.BeginTransactionAsync();
            var t = await db.TradeOffers.FromSqlRaw("SELECT * FROM trade_offers WHERE id = {0} FOR UPDATE", id).FirstOrDefaultAsync() ?? throw ApiException.NotFound("trade_not_found");
            if (t.ToMembershipId != mid) throw ApiException.NotFound("trade_not_found");
            if (t.Status != "pending") throw ApiException.Conflict("trade_closed");
            if (t.ExpiresAt < DateTimeOffset.UtcNow) { t.Status = "expired"; t.ClosedAt = DateTimeOffset.UtcNow; await db.SaveChangesAsync(); await tx.CommitAsync(); throw ApiException.Conflict("trade_expired"); }
            var offer = Ids(t.OfferCardIds); var want = Ids(t.RequestCardIds);
            var all = offer.Concat(want).OrderBy(x => x).ToList();
            // lock every card row in a stable order (no deadlocks), then re-validate both sides
            var locked = await db.Cards.FromSqlRaw("SELECT * FROM card_instances WHERE id = ANY({0}) ORDER BY id FOR UPDATE", all.ToArray()).ToListAsync();
            try
            {
                Validate(locked, offer, t.FromMembershipId, sid);
                Validate(locked, want, t.ToMembershipId, sid);
                if (await InActiveMatch(db, all)) throw ApiException.Conflict("trade_card_in_match");
            }
            catch (ApiException e)
            {
                t.Status = "failed"; t.ClosedAt = DateTimeOffset.UtcNow; await db.SaveChangesAsync(); await tx.CommitAsync();
                throw ApiException.Conflict(e.Code == "trade_card_in_match" ? e.Code : "trade_cards_changed");
            }
            string r = "trade:" + t.Id;
            foreach (var c in locked.Where(c => offer.Contains(c.Id))) await CardTransfer.Transfer(db, c, t.ToMembershipId, "trade", r);
            foreach (var c in locked.Where(c => want.Contains(c.Id))) await CardTransfer.Transfer(db, c, t.FromMembershipId, "trade", r);
            // saved teams that referenced traded cards are no longer valid for their previous owners
            await db.TeamLineups.Where(l => all.Contains(l.Card1Id) || all.Contains(l.Card2Id) || all.Contains(l.Card3Id)).ExecuteDeleteAsync();
            await db.TrainerProfiles.Where(p => p.CompanionCardId != null && all.Contains(p.CompanionCardId.Value)).ExecuteUpdateAsync(u => u.SetProperty(p => p.CompanionCardId, (long?)null));
            t.Status = "accepted"; t.ClosedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            // other pending offers that involve the moved cards can no longer succeed
            var stale = await db.TradeOffers.Where(o => o.Status == "pending" && o.Id != t.Id && (o.FromMembershipId == t.FromMembershipId || o.ToMembershipId == t.FromMembershipId || o.FromMembershipId == t.ToMembershipId || o.ToMembershipId == t.ToMembershipId)).ToListAsync();
            foreach (var o in stale.Where(o => Ids(o.OfferCardIds).Concat(Ids(o.RequestCardIds)).Any(all.Contains))) { o.Status = "failed"; o.ClosedAt = DateTimeOffset.UtcNow; }
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            await notes.Notify(t.FromMembershipId, "trade_accepted", new { tradeId = t.Id, uid = ctx.User.AccountId() }, "trade_ok:" + t.Id);
            return Results.Ok(new { status = "accepted" });
        });

        g.MapPost("/market/trades/{id:long}/decline", async (long id, HttpContext ctx, PetStarDb db) =>
        {
            var (_, mid) = ctx.User.Player();
            int n = await db.TradeOffers.Where(t => t.Id == id && t.ToMembershipId == mid && t.Status == "pending").ExecuteUpdateAsync(u => u.SetProperty(t => t.Status, "declined").SetProperty(t => t.ClosedAt, DateTimeOffset.UtcNow));
            return n == 0 ? throw ApiException.NotFound("trade_not_found") : Results.Ok(new { status = "declined" });
        });

        g.MapPost("/market/trades/{id:long}/cancel", async (long id, HttpContext ctx, PetStarDb db) =>
        {
            var (_, mid) = ctx.User.Player();
            int n = await db.TradeOffers.Where(t => t.Id == id && t.FromMembershipId == mid && t.Status == "pending").ExecuteUpdateAsync(u => u.SetProperty(t => t.Status, "cancelled").SetProperty(t => t.ClosedAt, DateTimeOffset.UtcNow));
            return n == 0 ? throw ApiException.NotFound("trade_not_found") : Results.Ok(new { status = "cancelled" });
        });
    }

    private static async Task CheckSide(PetStarDb db, List<long> ids, long owner, int sid)
    {
        var cards = await db.Cards.AsNoTracking().Where(c => ids.Contains(c.Id)).ToListAsync();
        Validate(cards, ids, owner, sid);
        if (await InActiveMatch(db, ids)) throw ApiException.Conflict("trade_card_in_match");
    }

    private static void Validate(List<CardInstance> cards, List<long> ids, long owner, int sid)
    {
        foreach (var id in ids)
        {
            var c = cards.FirstOrDefault(x => x.Id == id);
            if (c == null || c.OwnerMembershipId != owner || c.ServerId != sid) throw ApiException.Conflict("trade_card_not_owned");
            if (c.IsLocked) throw ApiException.Conflict("card_locked");
            if (c.BusyState != null) throw ApiException.Conflict("card_busy");   // listed / in auction / under review
        }
    }

    private static async Task<bool> InActiveMatch(PetStarDb db, List<long> ids)
    {
        var active = await db.ArenaMatchPlayers.AsNoTracking().Where(p => p.MembershipId != null)
            .Join(db.ArenaMatches.Where(m => m.Status == "active" || m.Status == "pending"), p => p.MatchId, m => m.Id, (p, m) => p.DeckJson).ToListAsync();
        return active.Any(j => ids.Any(id => j.Contains($"\"cardInstanceId\":{id},") || j.Contains($"\"cardInstanceId\":{id}}}")));
    }
}
