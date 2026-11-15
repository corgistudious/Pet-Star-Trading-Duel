using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using PetStar.Api.Data;
using PetStar.Api.Infrastructure;
using PetStar.Rules;

namespace PetStar.Api.Features;

public sealed class CollectionRequest { public string Name { get; set; } = ""; public List<long> CardIds { get; set; } = new(); }

public static class CommunityEndpoints
{
    private static readonly string[] RareSet = { "SSR", "UR", "LR", "MR", "GR", "EX", "Secret" };

    public static void Map(WebApplication app)
    {
        var g = ServerEndpoints.GameGroup(app);

        // ---------------- Notification Center ----------------
        g.MapGet("/notifications", async (bool? unread, long? before, HttpContext ctx, PetStarDb db) =>
        {
            var (_, mid) = ctx.User.Player();
            var q = db.Notifications.AsNoTracking().Where(n => n.MembershipId == mid);
            if (unread == true) q = q.Where(n => n.ReadAt == null);
            if (before.HasValue) q = q.Where(n => n.Id < before);
            var items = await q.OrderByDescending(n => n.Id).Take(50).ToListAsync();
            return new
            {
                unreadCount = await db.Notifications.CountAsync(n => n.MembershipId == mid && n.ReadAt == null),
                items = items.Select(n => new { n.Id, n.Type, data = JsonNode.Parse(n.DataJson), n.CreatedAt, read = n.ReadAt != null }),
            };
        });

        g.MapPost("/notifications/read", async (List<long>? ids, HttpContext ctx, PetStarDb db) =>
        {
            var (_, mid) = ctx.User.Player();
            var now = DateTimeOffset.UtcNow;
            var q = db.Notifications.Where(n => n.MembershipId == mid && n.ReadAt == null);
            if (ids != null && ids.Count > 0) q = q.Where(n => ids.Contains(n.Id));
            int n = await q.ExecuteUpdateAsync(u => u.SetProperty(x => x.ReadAt, now));
            return Results.Ok(new { marked = n });
        });

        // ---------------- Mailbox (attachments claimable exactly once) ----------------
        g.MapGet("/mailbox", async (HttpContext ctx, PetStarDb db) =>
        {
            var (_, mid) = ctx.User.Player();
            var now = DateTimeOffset.UtcNow;
            var rows = await db.Mailbox.AsNoTracking().Where(m => m.MembershipId == mid && (m.ExpiresAt == null || m.ExpiresAt > now)).OrderByDescending(m => m.Id).Take(100).ToListAsync();
            return rows.Select(m => new { m.Id, m.TitleKey, m.Body, attachments = JsonNode.Parse(m.AttachmentsJson), m.CreatedAt, m.ExpiresAt, claimed = m.ClaimedAt != null });
        });

        g.MapPost("/mailbox/{id:long}/claim", async (long id, HttpContext ctx, PetStarDb db, EconomyService eco) =>
        {
            var (sid, mid) = ctx.User.Player();
            await using var tx = await db.Database.BeginTransactionAsync();
            var m = await db.Mailbox.FromSqlRaw("SELECT * FROM mailbox_messages WHERE id = {0} AND membership_id = {1} FOR UPDATE", id, mid).FirstOrDefaultAsync()
                    ?? throw ApiException.NotFound("mail_not_found");
            if (m.ClaimedAt != null) throw ApiException.Conflict("mail_already_claimed");
            if (m.ExpiresAt != null && m.ExpiresAt <= DateTimeOffset.UtcNow) throw ApiException.Conflict("mail_expired");
            var att = JsonNode.Parse(m.AttachmentsJson)!.AsObject();
            string refId = "mail:" + m.Id;
            foreach (var (k, v) in att["currencies"]?.AsObject() ?? new JsonObject()) await eco.Currency(mid, sid, k, v!.GetValue<long>(), "mail_claim", refId);
            foreach (var (k, v) in att["items"]?.AsObject() ?? new JsonObject()) await eco.Item(mid, sid, k, v!.GetValue<long>(), "mail_claim", refId);
            foreach (var (k, v) in att["packs"]?.AsObject() ?? new JsonObject()) await eco.Packs(mid, sid, k, v!.GetValue<int>(), "mail_claim", refId);
            m.ClaimedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return Results.Ok(new { claimed = true, currencies = await eco.Balances(mid) });
        });

        // ---------------- Achievements ----------------
        g.MapGet("/achievements", async (HttpContext ctx, PetStarDb db, GameData data) =>
        {
            var (_, mid) = ctx.User.Player();
            var have = await db.Achievements.AsNoTracking().Where(a => a.MembershipId == mid).ToDictionaryAsync(a => a.AchievementId, a => a.UnlockedAt);
            return data.Achievements.Items.Select(a => new { a.Id, a.Trigger, a.Threshold, a.Element, a.Currencies, a.Packs, unlocked = have.ContainsKey(a.Id), unlockedAt = have.TryGetValue(a.Id, out var t) ? t : (DateTimeOffset?)null });
        });

        // ---------------- Collections / albums ----------------
        g.MapGet("/collections", async (HttpContext ctx, PetStarDb db, GameData data) =>
        {
            var (sid, mid) = ctx.User.Player();
            var codex = (await db.Codex.AsNoTracking().Where(c => c.MembershipId == mid).Select(c => c.FormId).ToListAsync()).ToHashSet();
            var owned = await db.Cards.AsNoTracking().Where(c => c.OwnerMembershipId == mid && c.ServerId == sid).Select(c => new { c.FormId, c.Rarity }).ToListAsync();
            var cat = data.Catalog;
            object Album(string id, IEnumerable<string> forms)
            {
                var list = forms.ToList();
                int done = list.Count(codex.Contains);
                return new { id, total = list.Count, discovered = done, complete = done == list.Count && list.Count > 0 };
            }
            var system = new List<object>
            {
                Album("starter", cat.Forms.Values.Where(f => f.Stage == 1 && f.FamilyStages > 1).Select(f => f.Id)),
                Album("evolution", cat.Forms.Values.Where(f => f.FamilyStages == 3).Select(f => f.Id)),
                Album("standalone", cat.Forms.Values.Where(f => f.FamilyStages == 1).Select(f => f.Id)),
            };
            foreach (var el in cat.Elements.Elements) system.Add(Album("element_" + el.Id, cat.Forms.Values.Where(f => f.Element == el.Id).Select(f => f.Id)));
            int rareForms = owned.Where(o => RareSet.Contains(o.Rarity)).Select(o => o.FormId).Distinct().Count();
            system.Add(new { id = "rare", total = 25, discovered = Math.Min(25, rareForms), complete = rareForms >= 25 });
            system.Add(new { id = "event", total = 0, discovered = 0, complete = false });
            var custom = await db.Collections.AsNoTracking().Include(c => c.Items).Where(c => c.MembershipId == mid).OrderBy(c => c.Id).ToListAsync();
            return new
            {
                codexCompletion = Math.Round(100.0 * codex.Count / cat.Forms.Count, 2),
                system,
                custom = custom.Select(c => new { c.Id, c.Name, c.CreatedAt, cardIds = c.Items.OrderBy(i => i.Position).Select(i => i.CardId) }),
            };
        });

        g.MapPost("/collections", async (CollectionRequest req, HttpContext ctx, PetStarDb db, GameData data) =>
        {
            var (sid, mid) = ctx.User.Player();
            var name = ContentGuard.Clean(req.Name, 32, true);
            if (await db.Collections.CountAsync(c => c.MembershipId == mid) >= data.Market.L("social", "maxCollections")) throw ApiException.Conflict("too_many_albums");
            var col = new Collection { MembershipId = mid, ServerId = sid, Name = name, CreatedAt = DateTimeOffset.UtcNow };
            await SetItems(db, data, col, req.CardIds, mid, sid);
            db.Collections.Add(col);
            try { await db.SaveChangesAsync(); } catch (DbUpdateException) { throw ApiException.Conflict("album_name_taken"); }
            return new { col.Id, col.Name, cardIds = col.Items.Select(i => i.CardId) };
        });

        g.MapPut("/collections/{id:long}", async (long id, CollectionRequest req, HttpContext ctx, PetStarDb db, GameData data) =>
        {
            var (sid, mid) = ctx.User.Player();
            var col = await db.Collections.Include(c => c.Items).FirstOrDefaultAsync(c => c.Id == id && c.MembershipId == mid) ?? throw ApiException.NotFound();
            col.Name = ContentGuard.Clean(req.Name, 32, true);
            db.CollectionItems.RemoveRange(col.Items);
            col.Items.Clear();
            await SetItems(db, data, col, req.CardIds, mid, sid);
            await db.SaveChangesAsync();
            return new { col.Id, col.Name, cardIds = col.Items.Select(i => i.CardId) };
        });

        g.MapDelete("/collections/{id:long}", async (long id, HttpContext ctx, PetStarDb db) =>
        {
            var (_, mid) = ctx.User.Player();
            int n = await db.Collections.Where(c => c.Id == id && c.MembershipId == mid).ExecuteDeleteAsync();
            return n == 0 ? throw ApiException.NotFound() : Results.Ok(new { deleted = true });
        });
    }

    /// <summary>Albums can only contain cards the player really owns (no generated cards).</summary>
    private static async Task SetItems(PetStarDb db, GameData data, Collection col, List<long> ids, long mid, int sid)
    {
        if (ids.Count > data.Market.L("social", "maxCollectionItems") || ids.Distinct().Count() != ids.Count) throw ApiException.BadRequest("album_items_invalid");
        if (await db.Cards.CountAsync(c => ids.Contains(c.Id) && c.OwnerMembershipId == mid && c.ServerId == sid) != ids.Count) throw ApiException.BadRequest("card_not_owned");
        for (int i = 0; i < ids.Count; i++) col.Items.Add(new CollectionItem { CardId = ids[i], Position = i });
    }
}
