using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PetStar.Api.Data;
using PetStar.Api.Infrastructure;
using PetStar.Contracts;
using PetStar.Rules;

namespace PetStar.Api.Features;

/// <summary>
/// Card progression: stars (1–10), enhancement (+0–+20, always succeeds, never destroys), breakthrough (level cap
/// 50 → 70), awakening, skill level (1–10, existing /train route), engravings and partial reset.
/// Every mutation:
///  - runs inside the idempotency transaction (a retried request replays the stored result);
///  - carries the value the player saw (<see cref="UpgradeRequest.From"/>): stale or double-tapped requests get 409;
///  - locks the card row (FOR UPDATE) and refuses busy cards (listed, in auction, fraud review);
///  - spends materials only through the ledger (unique business reference → never charged twice);
///  - writes a card_progress_events row (shown in the card passport).
/// Stats are always recomputed by the server from stored values; nothing is taken from the client.
/// </summary>
public static class CardUpgradeEndpoints
{
    public static void Map(WebApplication app)
    {
        var g = ServerEndpoints.GameGroup(app);

        g.MapGet("/materials", async (HttpContext ctx, PetStarDb db, EconomyService eco) =>
        {
            var (sid, mid) = ctx.User.Player();
            var items = await Materials(db, mid);
            var engr = await db.Engravings.AsNoTracking().Where(e => e.MembershipId == mid && e.ServerId == sid).OrderBy(e => e.Id).ToListAsync();
            return new { materials = items, beans = (await eco.Balances(mid))[Currency.MiniGreenBean], engravings = engr.Select(CardProgressLoader.ToDto) };
        });

        g.MapGet("/cards/{id:long}/upgrades", async (long id, HttpContext ctx, PetStarDb db, GameData data, EconomyService eco) =>
        {
            var (sid, mid) = ctx.User.Player();
            var c = await db.Cards.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.OwnerMembershipId == mid && x.ServerId == sid) ?? throw ApiException.NotFound("card_not_found");
            return await Preview(db, data, eco, c, mid, sid);
        });

        Upgrade(g, "star", "cards.star", (c, _) => c.Stars, async (u) =>
        {
            var cost = Progression.StarCost(u.Card.Stars) ?? throw ApiException.Conflict("star_max_reached");
            await u.Pay(cost, $"card:{u.Card.Id}:star{u.Card.Stars + 1}");
            u.Card.Stars++;
            return cost;
        });

        Upgrade(g, "enhance", "cards.enhance", (c, _) => c.EnhanceLevel, async (u) =>
        {
            var cost = Progression.EnhanceCost(u.Card.EnhanceLevel) ?? throw ApiException.Conflict("enhance_max_reached");
            if (u.Card.Level < cost.MinLevel) throw ApiException.Conflict("enhance_level_too_low");
            // the reference includes the row version so enhancing again after a reset is a new, distinct charge
            await u.Pay(cost, $"card:{u.Card.Id}:enh{u.Card.EnhanceLevel + 1}:{u.Card.UpdatedAt.UtcTicks}");
            u.Card.EnhanceLevel++;
            return cost;
        });

        Upgrade(g, "breakthrough", "cards.breakthrough", (c, _) => c.Breakthrough, async (u) =>
        {
            var cost = Progression.BreakthroughCost(u.Card.Breakthrough) ?? throw ApiException.Conflict("breakthrough_max_reached");
            if (u.Card.Level < cost.MinLevel) throw ApiException.Conflict("breakthrough_level_too_low");
            if (u.Card.Stars < cost.MinStars) throw ApiException.Conflict("breakthrough_stars_too_low");
            await u.Pay(cost, $"card:{u.Card.Id}:bt{u.Card.Breakthrough + 1}");
            u.Card.Breakthrough++;
            return cost;
        });

        Upgrade(g, "awaken", "cards.awaken", (c, _) => c.Awakened ? 1 : 0, async (u) =>
        {
            if (u.Card.Awakened) throw ApiException.Conflict("awaken_max_reached");
            var cost = Progression.AwakenCost();
            if (u.Card.Stars < cost.MinStars) throw ApiException.Conflict("awaken_stars_too_low");
            if (u.Card.Breakthrough < cost.MinBreakthrough) throw ApiException.Conflict("awaken_breakthrough_too_low");
            if (u.Card.Level < cost.MinLevel) throw ApiException.Conflict("awaken_level_too_low");
            await u.Pay(cost, $"card:{u.Card.Id}:awaken");
            u.Card.Awakened = true;
            return cost;
        });

        // ---------------------------------------------------------------- reset (partial refund)

        g.MapGet("/cards/{id:long}/reset-preview", async (long id, string kind, HttpContext ctx, PetStarDb db) =>
        {
            var (sid, mid) = ctx.User.Player();
            var c = await db.Cards.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.OwnerMembershipId == mid && x.ServerId == sid) ?? throw ApiException.NotFound("card_not_found");
            return ResetPreview(c, kind);
        });

        g.MapPost("/cards/{id:long}/reset", (long id, ResetRequest req, HttpContext ctx, PetStarDb db, GameData data, EconomyService eco, IdempotencyService idem) =>
            idem.Run(ctx, "cards.reset", new { id, req.Kind, req.From }, async () =>
            {
                var (sid, mid) = ctx.User.Player();
                var c = await Lock(db, id, mid, sid);
                var pv = ResetPreview(c, req.Kind);
                if (pv.Blocked != null) throw ApiException.Conflict(pv.Blocked);
                if (pv.From != req.From) throw ApiException.Conflict("stale_state");
                string refId = $"card:{c.Id}:reset_{req.Kind}:{c.UpdatedAt.UtcTicks}";
                if (pv.RefundBeans > 0) await eco.Currency(mid, sid, Currency.MiniGreenBean, pv.RefundBeans, "card_reset", refId);
                foreach (var (item, n) in pv.RefundItems.Where(x => x.Value > 0)) await eco.Item(mid, sid, item, n, "card_reset", refId);
                if (req.Kind == "enhance") c.EnhanceLevel = 0; else c.SkillLevel = 1;
                // engravings in slots that are no longer unlocked would stay valid (slots depend on level/breakthrough, never reset)
                Log(db, c, "reset_" + req.Kind, pv.From, pv.To, new { refundBeans = pv.RefundBeans, refundItems = pv.RefundItems }, mid);
                c.UpdatedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync();
                return new { card = await data.ToDtoFull(db, c), refund = pv, currencies = await eco.Balances(mid), materials = await Materials(db, mid) };
            }));

        // ---------------------------------------------------------------- engravings

        g.MapPost("/engravings/craft", (CraftEngravingRequest req, HttpContext ctx, PetStarDb db, EconomyService eco, IdempotencyService idem) =>
            idem.Run(ctx, "engravings.craft", req, async () =>
            {
                var (sid, mid) = ctx.User.Player();
                if (!Progression.EngravingTypes.Contains(req.Type)) throw ApiException.BadRequest("engraving_unknown_type");
                var e = new CardEngraving { MembershipId = mid, ServerId = sid, Type = req.Type, Level = 1, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
                db.Engravings.Add(e);
                await db.SaveChangesAsync();
                await Pay(eco, mid, sid, Progression.EngravingCraftCost(), $"engraving:{e.Id}:craft", "engraving");
                return new { engraving = CardProgressLoader.ToDto(e), currencies = await eco.Balances(mid), materials = await Materials(db, mid) };
            }));

        g.MapPost("/engravings/{eid:long}/upgrade", (long eid, UpgradeRequest req, HttpContext ctx, PetStarDb db, EconomyService eco, IdempotencyService idem) =>
            idem.Run(ctx, "engravings.upgrade", new { eid, req.From }, async () =>
            {
                var (sid, mid) = ctx.User.Player();
                var e = await db.Engravings.FromSqlRaw("SELECT * FROM card_engravings WHERE id = {0} AND membership_id = {1} AND server_id = {2} FOR UPDATE", eid, mid, sid)
                    .FirstOrDefaultAsync() ?? throw ApiException.NotFound("engraving_not_found");
                if (e.Level != req.From) throw ApiException.Conflict("stale_state");
                if (e.CardId != null) await EnsureCardFree(db, e.CardId.Value, mid, sid);
                var cost = Progression.EngravingUpgradeCost(e.Level) ?? throw ApiException.Conflict("engraving_max_level");
                await Pay(eco, mid, sid, cost, $"engraving:{e.Id}:lv{e.Level + 1}", "engraving");
                e.Level++; e.UpdatedAt = DateTimeOffset.UtcNow;
                if (e.CardId != null) db.CardProgress.Add(Event(e.CardId.Value, "engraving_upgrade", new { engraving = e.Id, e.Type, to = e.Level, cost, by = mid }));
                await db.SaveChangesAsync();
                return new { engraving = CardProgressLoader.ToDto(e), currencies = await eco.Balances(mid), materials = await Materials(db, mid) };
            }));

        g.MapPost("/cards/{id:long}/engravings/equip", (long id, EquipEngravingRequest req, HttpContext ctx, PetStarDb db, GameData data, IdempotencyService idem) =>
            idem.Run(ctx, "engravings.equip", new { id, req.EngravingId, req.Slot }, async () =>
            {
                var (sid, mid) = ctx.User.Player();
                var c = await Lock(db, id, mid, sid);
                if (!Progression.SlotUnlocked(req.Slot, c.Level, c.Breakthrough)) throw ApiException.Conflict("engraving_slot_locked");
                var e = await db.Engravings.FromSqlRaw("SELECT * FROM card_engravings WHERE id = {0} AND membership_id = {1} AND server_id = {2} FOR UPDATE", req.EngravingId, mid, sid)
                    .FirstOrDefaultAsync() ?? throw ApiException.NotFound("engraving_not_found");
                if (e.CardId != null) throw ApiException.Conflict("engraving_in_use");
                var equipped = await db.Engravings.Where(x => x.CardId == c.Id).ToListAsync();
                var replaced = equipped.FirstOrDefault(x => x.Slot == req.Slot);
                var after = equipped.Where(x => x.Slot != req.Slot).Select(x => new EngravingInfo { Type = x.Type, Level = x.Level })
                    .Append(new EngravingInfo { Type = e.Type, Level = e.Level }).ToList();
                var err = Progression.ValidateEngravings(after);
                if (err != null) throw ApiException.Conflict(err);
                var now = DateTimeOffset.UtcNow;
                if (replaced != null) { replaced.CardId = null; replaced.Slot = null; replaced.UpdatedAt = now; await db.SaveChangesAsync(); }
                e.CardId = c.Id; e.Slot = req.Slot; e.UpdatedAt = now;
                db.CardProgress.Add(Event(c.Id, "engraving_equip", new { engraving = e.Id, e.Type, e.Level, slot = req.Slot, replaced = replaced?.Id, by = mid }));
                c.UpdatedAt = now;
                await db.SaveChangesAsync();
                return await data.ToDtoFull(db, c);
            }));

        g.MapPost("/cards/{id:long}/engravings/unequip", (long id, UnequipEngravingRequest req, HttpContext ctx, PetStarDb db, GameData data, IdempotencyService idem) =>
            idem.Run(ctx, "engravings.unequip", new { id, req.Slot }, async () =>
            {
                var (sid, mid) = ctx.User.Player();
                var c = await Lock(db, id, mid, sid);
                var e = await db.Engravings.FirstOrDefaultAsync(x => x.CardId == c.Id && x.Slot == req.Slot) ?? throw ApiException.NotFound("engraving_not_found");
                var now = DateTimeOffset.UtcNow;
                e.CardId = null; e.Slot = null; e.UpdatedAt = now; c.UpdatedAt = now;
                db.CardProgress.Add(Event(c.Id, "engraving_unequip", new { engraving = e.Id, slot = req.Slot, by = mid }));
                await db.SaveChangesAsync();
                return await data.ToDtoFull(db, c);
            }));
    }

    // ------------------------------------------------------------------ shared plumbing

    public sealed class UpgradeContext(PetStarDb db, EconomyService eco, CardInstance card, long mid, int sid)
    {
        public CardInstance Card { get; } = card;
        public Task Pay(UpgradeCost cost, string refId) => CardUpgradeEndpoints.Pay(eco, mid, sid, cost, refId, "card_upgrade");
        public PetStarDb Db { get; } = db;
    }

    private static void Upgrade(RouteGroupBuilder g, string kind, string endpoint, Func<CardInstance, object?, int> current, Func<UpgradeContext, Task<UpgradeCost>> apply)
    {
        g.MapPost("/cards/{id:long}/" + kind, (long id, UpgradeRequest req, HttpContext ctx, PetStarDb db, GameData data, EconomyService eco, IdempotencyService idem) =>
            idem.Run(ctx, endpoint, new { id, req.From }, async () =>
            {
                var (sid, mid) = ctx.User.Player();
                var c = await Lock(db, id, mid, sid);
                int from = current(c, null);
                if (from != req.From) throw ApiException.Conflict("stale_state");   // double tap / outdated screen
                var cost = await apply(new UpgradeContext(db, eco, c, mid, sid));
                int to = current(c, null);
                Log(db, c, kind, from, to, cost, mid);
                c.UpdatedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync();
                return new { card = await data.ToDtoFull(db, c), from, to, cost, currencies = await eco.Balances(mid), materials = await Materials(db, mid) };
            }));
    }

    private static async Task<CardInstance> Lock(PetStarDb db, long id, long mid, int sid)
    {
        var c = await db.Cards.FromSqlRaw("SELECT * FROM card_instances WHERE id = {0} AND owner_membership_id = {1} AND server_id = {2} FOR UPDATE", id, mid, sid)
            .FirstOrDefaultAsync() ?? throw ApiException.NotFound("card_not_found");
        if (c.BusyState != null) throw ApiException.Conflict("card_busy");   // listed / in auction / under review
        return c;
    }

    private static async Task EnsureCardFree(PetStarDb db, long cardId, long mid, int sid) => await Lock(db, cardId, mid, sid);

    public static async Task Pay(EconomyService eco, long mid, int sid, UpgradeCost cost, string refId, string reason)
    {
        foreach (var (item, n) in cost.Items.Where(x => x.Value > 0)) await eco.Item(mid, sid, item, -n, reason, refId);
        if (cost.Beans > 0) await eco.Currency(mid, sid, Currency.MiniGreenBean, -cost.Beans, reason, refId);
    }

    private static CardProgressEvent Event(long cardId, string type, object data) =>
        new() { CardId = cardId, Type = type, DataJson = JsonSerializer.Serialize(data, IdempotencyService.Json), At = DateTimeOffset.UtcNow };

    private static void Log(PetStarDb db, CardInstance c, string kind, int from, int to, object cost, long mid) =>
        db.CardProgress.Add(Event(c.Id, kind == "star" ? "star_up" : kind, new { from, to, cost, by = mid }));

    public static async Task<Dictionary<string, long>> Materials(PetStarDb db, long mid)
    {
        var owned = await db.Items.AsNoTracking().Where(i => i.MembershipId == mid && Progression.Materials.Contains(i.ItemId)).ToDictionaryAsync(i => i.ItemId, i => i.Quantity);
        return Progression.Materials.ToDictionary(m => m, m => owned.GetValueOrDefault(m));
    }

    public static ResetPreviewDto ResetPreview(CardInstance c, string kind)
    {
        // 50 % of everything spent on that track is refunded; stars, breakthroughs, awakening, serial and history are never reset
        UpgradeCost spent; int from;
        switch (kind)
        {
            case "enhance": spent = Progression.EnhanceSpent(c.EnhanceLevel); from = c.EnhanceLevel; break;
            case "skill": spent = Progression.SkillSpent(c.SkillLevel); from = c.SkillLevel; break;
            default: return new ResetPreviewDto { Kind = kind, Blocked = "reset_unknown_kind" };
        }
        int to = kind == "enhance" ? 0 : 1;
        return new ResetPreviewDto
        {
            Kind = kind, From = from, To = to, RefundBeans = spent.Beans / 2,
            RefundItems = spent.Items.ToDictionary(x => x.Key, x => x.Value / 2),
            Blocked = from == to ? "reset_nothing_to_reset" : c.BusyState != null ? "card_busy" : null,
        };
    }

    private static async Task<CardUpgradesDto> Preview(PetStarDb db, GameData data, EconomyService eco, CardInstance c, long mid, int sid)
    {
        var engr = (await CardProgressLoader.Engravings(db, new[] { c.Id })).GetValueOrDefault(c.Id);
        var mats = await Materials(db, mid);
        long beans = (await eco.Balances(mid))[Currency.MiniGreenBean];
        string? Afford(UpgradeCost cost)
        {
            if (beans < cost.Beans) return "not_enough_beans";
            foreach (var (item, n) in cost.Items) if (mats.GetValueOrDefault(item) < n) return "not_enough_" + item;
            return null;
        }
        CardDto After(Action<CardInstance> change)
        {
            var copy = new CardInstance
            {
                Id = c.Id, Serial = c.Serial, FormId = c.FormId, Rarity = c.Rarity, Quality = c.Quality, Finish = c.Finish, Level = c.Level, Exp = c.Exp,
                SkillLevel = c.SkillLevel, Stars = c.Stars, EnhanceLevel = c.EnhanceLevel, Breakthrough = c.Breakthrough, Awakened = c.Awakened,
                IsFavorite = c.IsFavorite, IsLocked = c.IsLocked, BusyState = c.BusyState,
            };
            change(copy);
            return data.ToDto(copy, 0, engr);
        }
        var busy = c.BusyState != null ? "card_busy" : null;
        var opts = new List<UpgradeOptionDto>();
        void Add(string kind, int cur, int max, UpgradeCost? cost, string? gate, Action<CardInstance> change) =>
            opts.Add(new UpgradeOptionDto
            {
                Kind = kind, Current = cur, Max = max, Cost = cost == null ? null : Dto(cost),
                Blocked = cost == null ? "max_reached" : busy ?? gate ?? Afford(cost),
                Preview = cost == null ? null : After(change),
            });

        var star = Progression.StarCost(c.Stars);
        Add("star", c.Stars, Progression.MaxStars, star, null, x => x.Stars++);
        var enh = Progression.EnhanceCost(c.EnhanceLevel);
        Add("enhance", c.EnhanceLevel, Progression.MaxEnhance, enh, enh != null && c.Level < enh.MinLevel ? "level_too_low" : null, x => x.EnhanceLevel++);
        var bt = Progression.BreakthroughCost(c.Breakthrough);
        Add("breakthrough", c.Breakthrough, Progression.MaxBreakthrough, bt,
            bt == null ? null : c.Level < bt.MinLevel ? "level_too_low" : c.Stars < bt.MinStars ? "stars_too_low" : null, x => x.Breakthrough++);
        var aw = c.Awakened ? null : Progression.AwakenCost();
        Add("awaken", c.Awakened ? 1 : 0, 1, aw,
            aw == null ? null : c.Stars < aw.MinStars ? "stars_too_low" : c.Breakthrough < aw.MinBreakthrough ? "breakthrough_too_low" : c.Level < aw.MinLevel ? "level_too_low" : null,
            x => x.Awakened = true);
        var sk = Progression.SkillCost(c.SkillLevel);
        Add("skill", c.SkillLevel, Progression.MaxSkillLevel, sk,
            sk == null ? null : c.Level < sk.MinLevel ? "level_too_low" : c.Breakthrough < sk.MinBreakthrough ? "breakthrough_too_low" : null, x => x.SkillLevel++);

        var free = await db.Engravings.AsNoTracking().Where(e => e.MembershipId == mid && e.ServerId == sid && e.CardId == null).OrderBy(e => e.Id).ToListAsync();
        return new CardUpgradesDto
        {
            Card = data.ToDto(c, 0, engr), Options = opts, Materials = mats, Beans = beans,
            FreeEngravings = free.Select(CardProgressLoader.ToDto).ToList(),
            UnlockedSlots = Enumerable.Range(1, Progression.MaxEngravingSlots).Where(s => Progression.SlotUnlocked(s, c.Level, c.Breakthrough)).ToList(),
        };
    }

    public static UpgradeCostDto Dto(UpgradeCost c) => new()
    {
        Beans = c.Beans, Items = new Dictionary<string, int>(c.Items), MinLevel = c.MinLevel, MinStars = c.MinStars, MinBreakthrough = c.MinBreakthrough,
    };
}
