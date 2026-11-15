using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PetStar.Api.Data;
using PetStar.Api.Infrastructure;
using PetStar.Contracts;
using PetStar.Rules;

namespace PetStar.Api.Features;

public sealed record PassportValue(string Currency, long Initial, long Estimated, int Samples, string Confidence);
public sealed record PassportEvent(string Type, DateTimeOffset At, string? FromFormId = null, string? ToFormId = null, string? Detail = null,
    string? Currency = null, long? Price = null, string? Status = null);

public sealed class PassportDto
{
    public string Serial { get; set; } = "";
    public long? CardId { get; set; }                 // only for the owner (needed for actions)
    public bool IsOwner { get; set; }
    public CardDto Card { get; set; } = new();
    public string IssuedServer { get; set; } = "";
    public DateTimeOffset IssuedAt { get; set; }
    public string Origin { get; set; } = "";
    public string? OriginRef { get; set; }            // owner only
    public string FormAtIssue { get; set; } = "";
    public string OwnerNickname { get; set; } = "";
    public long OwnerUid { get; set; }
    public int TransferCount { get; set; }
    public List<PassportEvent> Evolutions { get; set; } = new();
    public List<PassportEvent> Upgrades { get; set; } = new();
    public List<PassportEvent> Listings { get; set; } = new();
    public List<PassportEvent> Trades { get; set; } = new();
    public List<PassportValue> Values { get; set; } = new();
    public string LockStatus { get; set; } = "free";  // free | player_locked | listed | in_auction | under_review
}

public static class PassportEndpoints
{
    public static void Map(WebApplication app)
    {
        var g = ServerEndpoints.GameGroup(app);

        g.MapGet("/cards/{id:long}/passport", async (long id, HttpContext ctx, PetStarDb db, GameData data, ValuationService val) =>
        {
            var (sid, mid) = ctx.User.Player();
            var card = await db.Cards.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id && c.OwnerMembershipId == mid && c.ServerId == sid) ?? throw ApiException.NotFound("card_not_found");
            return await Build(db, data, val, card, true);
        });

        // Public passport (same server only). Serial is a collectible identifier — it grants no ownership rights.
        g.MapGet("/passport/{serial}", async (string serial, HttpContext ctx, PetStarDb db, GameData data, ValuationService val) =>
        {
            var (sid, mid) = ctx.User.Player();
            serial = serial.Trim().ToUpperInvariant();
            if (!SerialGenerator.IsValid(serial)) throw ApiException.BadRequest("invalid_serial");
            var card = await db.Cards.AsNoTracking().FirstOrDefaultAsync(c => c.Serial == serial && c.ServerId == sid) ?? throw ApiException.NotFound("card_not_found");
            return await Build(db, data, val, card, card.OwnerMembershipId == mid);
        });

        g.MapGet("/valuation/history", async (string formId, string rarity, string? currency, int? days, HttpContext ctx, PetStarDb db, GameData data) =>
        {
            var (sid, _) = ctx.User.Player();
            if (!data.Catalog.Forms.ContainsKey(formId)) throw ApiException.NotFound("form_not_found");
            var since = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-Math.Clamp(days ?? 30, 1, 365)));
            var snaps = await db.ValuationSnapshots.AsNoTracking()
                .Where(s => s.ServerId == sid && s.FormId == formId && s.Rarity == rarity && (currency == null || s.Currency == currency) && s.Day >= since)
                .OrderBy(s => s.Day).Select(s => new { s.Day, s.Currency, s.InitialReference, s.EstimatedValue, s.SampleSize, s.Confidence, s.Supply }).ToListAsync();
            var since2 = DateTimeOffset.UtcNow.AddDays(-Math.Clamp(days ?? 30, 1, 365));
            // completed, non-excluded trades only — listing prices are never shown as market evidence
            var trades = await db.PriceHistory.AsNoTracking()
                .Where(p => p.ServerId == sid && p.FormId == formId && p.Rarity == rarity && !p.Excluded && (currency == null || p.Currency == currency) && p.At >= since2)
                .OrderBy(p => p.At).Select(p => new { p.At, p.Currency, p.Price }).Take(500).ToListAsync();
            return new { formId, rarity, snapshots = snaps, trades };
        });

        g.MapGet("/cards/{id:long}/training", async (long id, HttpContext ctx, PetStarDb db, GameData data) =>
        {
            var (sid, mid) = ctx.User.Player();
            var c = await db.Cards.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.OwnerMembershipId == mid && x.ServerId == sid) ?? throw ApiException.NotFound("card_not_found");
            var step = TrainingRules.Next(c.SkillLevel);
            long scrolls = await db.Items.Where(i => i.MembershipId == mid && i.ItemId == TrainingRules.ScrollItemId).Select(i => i.Quantity).FirstOrDefaultAsync();
            long beans = await db.Wallets.Where(w => w.MembershipId == mid && w.Currency == Currency.MiniGreenBean).Select(w => w.Balance).FirstOrDefaultAsync();
            return new { skillLevel = c.SkillLevel, maxSkillLevel = TrainingRules.MaxSkillLevel, next = step, scrollsOwned = scrolls, beansOwned = beans, cardLevel = c.Level };
        });

        // Skill upgrade: server-validated, capped at level 5, materials consumed once (ledger ref card:{id}:skill{n}).
        g.MapPost("/cards/{id:long}/train", (long id, HttpContext ctx, PetStarDb db, GameData data, EconomyService eco, IdempotencyService idem) =>
            idem.Run(ctx, "cards.train", new { id }, async () =>
            {
                var (sid, mid) = ctx.User.Player();
                var c = await db.Cards.FromSqlRaw("SELECT * FROM card_instances WHERE id = {0} AND owner_membership_id = {1} AND server_id = {2} FOR UPDATE", id, mid, sid)
                    .FirstOrDefaultAsync() ?? throw ApiException.NotFound("card_not_found");
                if (c.BusyState != null) throw ApiException.Conflict("card_busy");
                var step = TrainingRules.Next(c.SkillLevel) ?? throw ApiException.Conflict("skill_max_level");
                if (c.Level < step.MinCardLevel) throw ApiException.Conflict("training_level_too_low");
                string refId = $"card:{c.Id}:skill{c.SkillLevel + 1}";
                await eco.Item(mid, sid, TrainingRules.ScrollItemId, -step.Scrolls, "training", refId);
                await eco.Currency(mid, sid, Currency.MiniGreenBean, -step.Beans, "training", refId);
                c.SkillLevel++; c.UpdatedAt = DateTimeOffset.UtcNow;
                db.CardProgress.Add(new CardProgressEvent { CardId = c.Id, Type = "skill_upgrade", DataJson = JsonSerializer.Serialize(new { to = c.SkillLevel, step.Scrolls, step.Beans }), At = DateTimeOffset.UtcNow });
                await db.SaveChangesAsync();
                return new { card = data.ToDto(c), currencies = await eco.Balances(mid) };
            }));
    }

    public static async Task<PassportDto> Build(PetStarDb db, GameData data, ValuationService val, CardInstance card, bool isOwner)
    {
        var pass = await db.Passports.AsNoTracking().FirstOrDefaultAsync(p => p.CardId == card.Id);
        var owner = await db.Memberships.AsNoTracking().FirstAsync(m => m.Id == card.OwnerMembershipId);
        var server = await db.Servers.AsNoTracking().FirstAsync(s => s.Id == (pass != null ? pass.IssuedServerId : card.ServerId));
        var dto = new PassportDto
        {
            Serial = card.Serial, CardId = isOwner ? card.Id : null, IsOwner = isOwner, Card = data.ToDto(card),
            IssuedServer = server.Code, IssuedAt = pass?.IssuedAt ?? card.CreatedAt, Origin = pass?.Origin ?? card.Origin,
            OriginRef = isOwner ? pass?.OriginRef : null, FormAtIssue = pass?.FormIdAtIssue ?? card.FormId,
            OwnerNickname = owner.Nickname, OwnerUid = owner.AccountId,
            TransferCount = await db.CardOwnership.CountAsync(h => h.CardId == card.Id && h.FromMembershipId != null),
            LockStatus = card.BusyState switch
            {
                null => card.IsLocked ? "player_locked" : "free",
                var b when b.StartsWith("listing:") => "listed",
                var b when b.StartsWith("auction:") => "in_auction",
                _ => "under_review",
            },
        };
        if (!isOwner) dto.Card.Id = 0; // internal id is not exposed publicly
        dto.Evolutions = (await db.CardEvolutions.AsNoTracking().Where(e => e.CardId == card.Id).OrderBy(e => e.At).ToListAsync())
            .Select(e => new PassportEvent("evolution", e.At, e.FromFormId, e.ToFormId)).ToList();
        dto.Upgrades = (await db.CardProgress.AsNoTracking().Where(e => e.CardId == card.Id).OrderBy(e => e.At).ToListAsync())
            .Select(e => new PassportEvent(e.Type, e.At, Detail: e.DataJson)).ToList();
        dto.Listings = (await db.Listings.AsNoTracking().Where(l => l.CardId == card.Id).OrderByDescending(l => l.CreatedAt).Take(50).ToListAsync())
            .Select(l => new PassportEvent("listing", l.CreatedAt, Currency: l.Currency, Price: l.Price, Status: l.Status)).ToList();
        dto.Listings.AddRange((await db.Auctions.AsNoTracking().Where(a => a.CardId == card.Id).OrderByDescending(a => a.CreatedAt).Take(50).ToListAsync())
            .Select(a => new PassportEvent("auction", a.CreatedAt, Currency: a.Currency, Price: a.HighestAmount ?? a.StartPrice, Status: a.Status)));
        // completed trades (counterparties are not disclosed)
        dto.Trades = (await db.MarketTransactions.AsNoTracking().Where(t => t.CardId == card.Id).OrderBy(t => t.At).ToListAsync())
            .Select(t => new PassportEvent("trade_" + t.Kind, t.At, Currency: t.Currency, Price: t.Price)).ToList();
        foreach (var cur in Currency.All)
        {
            // the issuance value applies while the card keeps its issue form; after evolution it is recomputed for the new form
            long? initial = pass == null || pass.FormIdAtIssue != card.FormId ? null : cur == Currency.MiniGreenBean ? pass.InitialValueBeans : pass.InitialValueStars;
            if (initial <= 0) initial = null;   // backfilled passports carry no issuance value
            var e = await val.Estimate(card.ServerId, card, cur, initial);
            dto.Values.Add(new PassportValue(cur, e.Initial, e.Estimated, e.Samples, e.Confidence));
        }
        return dto;
    }
}
