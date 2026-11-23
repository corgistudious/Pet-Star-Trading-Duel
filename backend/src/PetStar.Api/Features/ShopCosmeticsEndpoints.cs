using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PetStar.Api.Auth;
using PetStar.Api.Data;
using PetStar.Api.Infrastructure;

namespace PetStar.Api.Features;

public sealed class CosmeticPrice { public string Currency { get; set; } = ""; public long Amount { get; set; } }
public sealed class CosmeticItem
{
    public string Id { get; set; } = ""; public string Type { get; set; } = ""; public string Rarity { get; set; } = "";
    public Dictionary<string, string> Name { get; set; } = new(); public Dictionary<string, string> Desc { get; set; } = new();
    public CosmeticPrice? Price { get; set; } public JsonElement Style { get; set; } public string? Unlock { get; set; } public string? Category { get; set; }
    public Dictionary<string, long>? Grant { get; set; } public int? DailyLimit { get; set; }
}
public sealed class CosmeticsCatalog
{
    public string Version { get; set; } = ""; public List<string> Defaults { get; set; } = new(); public List<string> ShopCategories { get; set; } = new();
    public List<CosmeticItem> Items { get; set; } = new();
    public CosmeticItem? Find(string id) => Items.FirstOrDefault(i => i.Id == id);
    public static CosmeticsCatalog Load(string dir) =>
        JsonSerializer.Deserialize<CosmeticsCatalog>(File.ReadAllText(Path.Combine(dir, "cosmetics.json")), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
}
public sealed class ShopBuyRequest { public string ItemId { get; set; } = ""; public int Quantity { get; set; } = 1; }
public sealed class EquipRequest { public string Slot { get; set; } = ""; public string? ItemId { get; set; } }

/// <summary>Shop Hub + cosmetics (avatar frames, chat bubbles, effects, titles, Trainer skins). Every purchase is priced,
/// checked and charged on the server inside one transaction, keyed by the client's Idempotency-Key (no double charge).
/// In-game currencies only; there is no real-money path here.</summary>
public static class ShopCosmeticsEndpoints
{
    private static readonly Dictionary<string, string> Slots = new() { ["frame"] = "frame", ["bubble"] = "bubble", ["effect"] = "effect", ["title"] = "title" };

    public static void Map(WebApplication app)
    {
        var g = ServerEndpoints.GameGroup(app);

        g.MapGet("/shop/catalog", async (string? category, HttpContext ctx, PetStarDb db, CosmeticsCatalog cat, TeamRulesProvider rp) =>
        {
            var (_, mid) = ctx.User.Player();
            var owned = await Owned(db, mid, cat);
            var outfits = await db.TrainerOutfits.AsNoTracking().Where(o => o.MembershipId == mid).Select(o => o.OutfitId).ToListAsync();
            var trainer = await db.TrainerProfiles.AsNoTracking().FirstOrDefaultAsync(t => t.MembershipId == mid);
            var today = DateTimeOffset.UtcNow.UtcDateTime.Date;
            var boughtToday = await db.ShopPurchases.AsNoTracking().Where(p => p.MembershipId == mid && p.At >= today).GroupBy(p => p.ItemId).Select(x => new { x.Key, n = x.Sum(p => p.Quantity) }).ToDictionaryAsync(x => x.Key, x => x.n);
            var items = cat.Items.Select(i => new
            {
                i.Id, i.Type, i.Rarity, i.Name, i.Desc, i.Price, i.Style, i.Unlock, category = ShopCategory(i), i.Grant, i.DailyLimit,
                owned = i.Type != "item" && owned.Contains(i.Id), purchasable = i.Price != null, boughtToday = boughtToday.GetValueOrDefault(i.Id),
            }).Cast<object>().ToList();
            // skins: only outfits whose art is complete are listed; skins of the other character are shown but cannot be bought
            items.AddRange(rp.Trainers.Outfits.Where(o => o.AssetsComplete && o.Source != "default").Select(o => (object)new
            {
                o.Id, Type = "skin", Rarity = o.Rarity ?? "rare", o.Name, Desc = new Dictionary<string, string>(), o.Price, Style = (object?)null, Unlock = (string?)null, category = "skins",
                Grant = (object?)null, DailyLimit = (int?)null, owned = outfits.Contains(o.Id), purchasable = o.Price != null && (o.CharacterId == "*" || o.CharacterId == trainer?.CharacterId),
                boughtToday = 0, characterId = o.CharacterId, art = o.Art,
            }));
            return new { version = cat.Version, categories = cat.ShopCategories, items, packsEndpoint = "/api/shop/packs", realMoney = false };
        });

        g.MapPost("/shop/buy", async (ShopBuyRequest req, HttpContext ctx, PetStarDb db, CosmeticsCatalog cat, TeamRulesProvider rp, EconomyService eco, AuditService audit) =>
        {
            var (sid, mid) = ctx.User.Player();
            var key = ctx.Request.Headers["Idempotency-Key"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(key) || key.Length > 80) throw ApiException.BadRequest("idempotency_key_required");
            var prior = await db.ShopPurchases.AsNoTracking().FirstOrDefaultAsync(p => p.MembershipId == mid && p.IdempotencyKey == key);
            if (prior != null)
            {
                if (prior.ItemId != req.ItemId) throw ApiException.Conflict("idempotency_key_reused");
                return Results.Ok(new { purchased = true, replay = true, itemId = prior.ItemId, quantity = prior.Quantity });
            }
            int qty = Math.Clamp(req.Quantity, 1, 10);
            var item = cat.Find(req.ItemId);
            var outfit = item == null ? rp.Trainers.Outfits.FirstOrDefault(o => o.Id == req.ItemId) : null;
            if (item == null && outfit == null) throw ApiException.NotFound("item_not_found");
            CosmeticPrice price = item?.Price ?? outfit?.Price ?? throw ApiException.Conflict("item_not_for_sale");
            if (outfit != null)
            {
                if (!outfit.AssetsComplete || outfit.Source == "default") throw ApiException.Conflict("item_not_for_sale");
                var t = await db.TrainerProfiles.AsNoTracking().FirstOrDefaultAsync(x => x.MembershipId == mid) ?? throw ApiException.Conflict("trainer_not_chosen");
                if (outfit.CharacterId != "*" && outfit.CharacterId != t.CharacterId) throw ApiException.Conflict("outfit_wrong_character");
                if (await db.TrainerOutfits.AnyAsync(o => o.MembershipId == mid && o.OutfitId == outfit.Id)) throw ApiException.Conflict("already_owned");
                qty = 1;
            }
            else if (item!.Type != "item")
            {
                qty = 1;
                if (await db.Cosmetics.AnyAsync(o => o.MembershipId == mid && o.ItemId == item.Id)) throw ApiException.Conflict("already_owned");
                if (item.Unlock == "guild_member" && await SocialAccess.GuildOf(db, mid) == null) throw ApiException.Conflict("requires_guild");
            }
            else if (item.DailyLimit is int lim)
            {
                var today = DateTimeOffset.UtcNow.UtcDateTime.Date;
                int bought = await db.ShopPurchases.Where(p => p.MembershipId == mid && p.ItemId == item.Id && p.At >= today).SumAsync(p => (int?)p.Quantity) ?? 0;
                if (bought + qty > lim) throw ApiException.Conflict("daily_limit_reached");
            }
            long total = checked(price.Amount * qty);
            await using var tx = await db.Database.BeginTransactionAsync();
            var row = new ShopPurchase { MembershipId = mid, ServerId = sid, ItemId = req.ItemId, Currency = price.Currency, Price = total, Quantity = qty, IdempotencyKey = key, At = DateTimeOffset.UtcNow };
            db.ShopPurchases.Add(row);
            try { await db.SaveChangesAsync(); } catch (DbUpdateException) { throw ApiException.Conflict("duplicate_request"); }
            string refId = "shop:" + row.Id;
            await eco.Currency(mid, sid, price.Currency, -total, "shop_purchase", refId);
            if (outfit != null) db.TrainerOutfits.Add(new TrainerOutfitOwned { MembershipId = mid, OutfitId = outfit.Id, Source = "shop" });
            else if (item!.Type == "item") foreach (var (it, n) in item.Grant ?? new()) await eco.Item(mid, sid, it, n * qty, "shop_purchase", refId);
            else db.Cosmetics.Add(new CosmeticOwned { MembershipId = mid, ItemId = item.Id, Source = "shop", AcquiredAt = DateTimeOffset.UtcNow });
            try { await db.SaveChangesAsync(); } catch (DbUpdateException) { throw ApiException.Conflict("already_owned"); }
            await tx.CommitAsync();
            return Results.Ok(new { purchased = true, replay = false, itemId = req.ItemId, quantity = qty, currency = price.Currency, price = total });
        });

        g.MapGet("/shop/history", async (HttpContext ctx, PetStarDb db) =>
        {
            var (_, mid) = ctx.User.Player();
            return await db.ShopPurchases.AsNoTracking().Where(p => p.MembershipId == mid).OrderByDescending(p => p.Id).Take(50)
                .Select(p => new { p.Id, p.ItemId, p.Quantity, p.Currency, p.Price, p.At }).ToListAsync();
        });

        g.MapGet("/cosmetics", async (HttpContext ctx, PetStarDb db, CosmeticsCatalog cat) =>
        {
            var (_, mid) = ctx.User.Player();
            var owned = await Owned(db, mid, cat);
            var t = await db.TrainerProfiles.AsNoTracking().FirstOrDefaultAsync(x => x.MembershipId == mid);
            return new
            {
                equipped = new { frame = t?.FrameId ?? "frame_default", bubble = t?.BubbleId ?? "bubble_default", effect = t?.EffectId, title = t?.TitleId ?? "title_rookie", outfit = t?.OutfitId },
                owned = owned.OrderBy(x => x).ToList(),
                items = cat.Items.Where(i => i.Type != "item"),
            };
        });

        g.MapPut("/cosmetics/equip", async (EquipRequest req, HttpContext ctx, PetStarDb db, CosmeticsCatalog cat) =>
        {
            var (_, mid) = ctx.User.Player();
            if (!Slots.ContainsKey(req.Slot)) throw ApiException.BadRequest("invalid_slot");
            var t = await db.TrainerProfiles.FirstOrDefaultAsync(x => x.MembershipId == mid) ?? throw ApiException.Conflict("trainer_not_chosen");
            if (req.ItemId != null)
            {
                var item = cat.Find(req.ItemId) ?? throw ApiException.NotFound("item_not_found");
                if (item.Type != req.Slot) throw ApiException.BadRequest("wrong_slot");
                if (!(await Owned(db, mid, cat)).Contains(item.Id)) throw ApiException.Conflict("item_not_owned");
                if (item.Unlock == "guild_member" && await SocialAccess.GuildOf(db, mid) == null) throw ApiException.Conflict("requires_guild");
            }
            switch (req.Slot)   // unequip = back to the default (frame/bubble/title) or none (effect)
            {
                case "frame": t.FrameId = req.ItemId ?? "frame_default"; break;
                case "bubble": t.BubbleId = req.ItemId ?? "bubble_default"; break;
                case "effect": t.EffectId = req.ItemId; break;
                case "title": t.TitleId = req.ItemId; break;
            }
            t.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(new { frame = t.FrameId, bubble = t.BubbleId, effect = t.EffectId, title = t.TitleId });
        });
    }

    private static string ShopCategory(CosmeticItem i) => i.Type switch
    {
        "frame" => "frames", "bubble" => "bubbles", "effect" => "accessories", "title" => "profile",
        _ => i.Category ?? (i.Unlock == "event" ? "event" : "evolution"),
    };

    /// <summary>Owned cosmetics = defaults + purchased/granted rows + unlocks earned from real progress (codex size).</summary>
    public static async Task<HashSet<string>> Owned(PetStarDb db, long mid, CosmeticsCatalog cat)
    {
        var set = (await db.Cosmetics.AsNoTracking().Where(o => o.MembershipId == mid).Select(o => o.ItemId).ToListAsync()).ToHashSet();
        foreach (var d in cat.Defaults) set.Add(d);
        if (cat.Find("title_collector") != null && await db.Codex.CountAsync(c => c.MembershipId == mid) >= 50) set.Add("title_collector");
        return set;
    }
}
