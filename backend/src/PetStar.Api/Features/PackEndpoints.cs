using Microsoft.EntityFrameworkCore;
using PetStar.Api.Data;
using PetStar.Api.Infrastructure;
using PetStar.Contracts;
using PetStar.Rules;

namespace PetStar.Api.Features;

public static class PackEndpoints
{
    public static void Map(WebApplication app)
    {
        // public odds disclosure (store policy): no auth needed
        app.MapGet("/api/packs/{id}/odds", async (string id, PetStarDb db, GameData data) =>
        {
            var (p, ver) = await data.Pack(db, id);
            static Dictionary<string, double> Pct(Dictionary<string, int> t) =>
                t.Where(x => x.Value > 0).ToDictionary(x => x.Key, x => Math.Round(x.Value / 10000.0, 4));
            return new PackOddsDto
            {
                PackTypeId = id, Version = ver, Slots1To4 = Pct(p.SlotTable), Slot5 = Pct(p.FinalSlotTable),
                Quality = Pct(p.QualityTable), Finish = Pct(p.FinishTable),
                PossiblePets = PackRoller.Pool(data.Catalog, p).Select(f => f.Id).ToList(),
            };
        }).RequireRateLimiting("public");

        var g = ServerEndpoints.GameGroup(app);

        g.MapGet("/packs", async (HttpContext ctx, PetStarDb db, GameData data) =>
        {
            var (_, mid) = ctx.User.Player();
            var owned = await db.PackInventory.AsNoTracking().Where(p => p.MembershipId == mid).ToDictionaryAsync(p => p.PackTypeId, p => p.Quantity);
            var rows = await db.PackTypes.AsNoTracking().OrderBy(p => p.Id).ToListAsync();
            var list = new List<PackShopItemDto>();
            foreach (var r in rows)
            {
                var (p, _) = await data.Pack(db, r.Id);
                bool available = IsAvailable(p);
                int have = owned.GetValueOrDefault(r.Id);
                if (!available && have == 0) continue;
                list.Add(new PackShopItemDto { Id = p.Id, NameKey = p.NameKey, ArtAssetId = p.ArtAssetId, PriceCurrency = p.Price.Currency, PriceAmount = available ? p.Price.Amount : 0, Owned = have, AvailableUntil = p.AvailableUntil });
            }
            return list;
        });

        g.MapPost("/packs/purchase", (PurchasePackRequest req, HttpContext ctx, PetStarDb db, GameData data,
            EconomyService eco, IdempotencyService idem) =>
            idem.Run(ctx, "packs.purchase", req, async () =>
            {
                var (sid, mid) = ctx.User.Player();
                if (req.Quantity < 1 || req.Quantity > 10) throw ApiException.BadRequest("invalid_quantity");
                var (p, _) = await data.Pack(db, req.PackTypeId);
                if (!IsAvailable(p)) throw ApiException.Conflict("pack_not_available");
                if (p.Price.Amount <= 0) throw ApiException.Conflict("pack_not_purchasable");
                var purchaseId = Guid.NewGuid().ToString("N");
                long cost = (long)p.Price.Amount * req.Quantity;
                await eco.Currency(mid, sid, p.Price.Currency, -cost, "pack_purchase", purchaseId);
                var after = await eco.Packs(mid, sid, p.Id, req.Quantity, "pack_purchase", purchaseId);
                return new PurchasePackResponse { PackTypeId = p.Id, Owned = (int)after.BalanceAfter, Currencies = await eco.Balances(mid) };
            }));

        // Server-side RNG. Pack is consumed and the 5 cards are persisted in ONE transaction before the response
        // is sent; retries with the same Idempotency-Key replay the stored result.
        g.MapPost("/packs/open", (OpenPackRequest req, HttpContext ctx, PetStarDb db, GameData data,
            EconomyService eco, IdempotencyService idem, CardFactory factory, AchievementService ach) =>
            idem.Run(ctx, "packs.open", req, async () =>
            {
                var (sid, mid) = ctx.User.Player();
                var (p, ver) = await data.Pack(db, req.PackTypeId);
                var opening = new PackOpening
                {
                    Id = Guid.NewGuid(), MembershipId = mid, ServerId = sid, PackTypeId = p.Id, PackConfigVersion = ver,
                    IdempotencyKey = ctx.Request.Headers["Idempotency-Key"].ToString(), CreatedAt = DateTimeOffset.UtcNow,
                };
                await eco.Packs(mid, sid, p.Id, -1, "pack_open", opening.Id.ToString("N"));
                var rolled = PackRoller.Roll(data.Catalog, p, new SecureRandomSource());
                if (rolled.Count != 5) throw new InvalidOperationException("pack must yield exactly 5 cards");
                db.PackOpenings.Add(opening);
                await db.SaveChangesAsync();
                var cards = await factory.Create(sid, mid, rolled.Select(r => new NewCard(r.FormId, r.Rarity, r.Quality, r.Finish)).ToList(), "pack", opening.Id.ToString("N"));
                for (int i = 0; i < cards.Count; i++)
                    opening.Results.Add(new PackOpeningResult { OpeningId = opening.Id, Slot = rolled[i].Slot, CardId = cards[i].Id, FormId = cards[i].FormId, Rarity = cards[i].Rarity, Quality = cards[i].Quality, Finish = cards[i].Finish });
                await db.SaveChangesAsync();
                await ach.Evaluate(mid, sid, "pack_opened", "rare_obtained", "codex_count", "family_complete", "element_complete", "rare_owned_distinct");
                return new PackOpeningDto { OpeningId = opening.Id, PackTypeId = p.Id, CreatedAt = opening.CreatedAt, Cards = cards.Select((c, i) => data.ToDto(c, rolled[i].Slot)).ToList() };
            }));

        // Recovery: results the client has not acknowledged (e.g. connection lost during the animation)
        g.MapGet("/packs/openings", async (bool? unacknowledged, int? limit, HttpContext ctx, PetStarDb db, GameData data) =>
        {
            var (sid, mid) = ctx.User.Player();
            var q = db.PackOpenings.AsNoTracking().Include(o => o.Results).Where(o => o.MembershipId == mid && o.ServerId == sid);
            if (unacknowledged == true) q = q.Where(o => o.AcknowledgedAt == null);
            var list = await q.OrderByDescending(o => o.CreatedAt).Take(Math.Clamp(limit ?? 20, 1, 100)).ToListAsync();
            return await Task.WhenAll(list.Select(o => ToDto(db, data, o)));
        });

        g.MapGet("/packs/openings/{id:guid}", async (Guid id, HttpContext ctx, PetStarDb db, GameData data) =>
        {
            var (sid, mid) = ctx.User.Player();
            var o = await db.PackOpenings.AsNoTracking().Include(x => x.Results).FirstOrDefaultAsync(x => x.Id == id && x.MembershipId == mid && x.ServerId == sid)
                    ?? throw ApiException.NotFound();
            return await ToDto(db, data, o);
        });

        g.MapPost("/packs/openings/{id:guid}/ack", async (Guid id, HttpContext ctx, PetStarDb db) =>
        {
            var (sid, mid) = ctx.User.Player();
            var now = DateTimeOffset.UtcNow;
            await db.PackOpenings.Where(x => x.Id == id && x.MembershipId == mid && x.ServerId == sid && x.AcknowledgedAt == null)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.AcknowledgedAt, now));
            return Results.Ok(new { acknowledged = true });
        });
    }

    private static bool IsAvailable(PackTypeDef p)
    {
        var now = DateTimeOffset.UtcNow;
        return p.Enabled && (p.AvailableFrom == null || p.AvailableFrom <= now) && (p.AvailableUntil == null || p.AvailableUntil > now);
    }

    private static async Task<PackOpeningDto> ToDto(PetStarDb db, GameData data, PackOpening o)
    {
        var ids = o.Results.Select(r => r.CardId).ToList();
        var cards = await db.Cards.AsNoTracking().Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id);
        return new PackOpeningDto
        {
            OpeningId = o.Id, PackTypeId = o.PackTypeId, CreatedAt = o.CreatedAt, Acknowledged = o.AcknowledgedAt != null,
            // show the card as it was rolled (rarity/quality/finish/form at opening time), with current id
            Cards = o.Results.OrderBy(r => r.Slot).Select(r =>
            {
                var snapshot = new CardInstance { Id = r.CardId, FormId = r.FormId, Rarity = r.Rarity, Quality = r.Quality, Finish = r.Finish, Level = 1, SkillLevel = 1 };
                if (cards.TryGetValue(r.CardId, out var c)) { snapshot.IsFavorite = c.IsFavorite; snapshot.IsLocked = c.IsLocked; snapshot.Serial = c.Serial; }
                return data.ToDto(snapshot, r.Slot);
            }).ToList(),
        };
    }
}
