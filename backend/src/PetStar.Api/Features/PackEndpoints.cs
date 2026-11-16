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
        // public odds disclosure (store policy): no auth needed. Per slot AND per whole 5-card pack.
        app.MapGet("/api/packs/{id}/odds", async (string id, PetStarDb db, GameData data) =>
        {
            var (p, ver) = await data.Pack(db, id);
            return Odds(data.Catalog, p, ver);
        }).RequireRateLimiting("public");

        // all 9 packs' odds at once (shop "odds" page, store review)
        app.MapGet("/api/packs/odds", async (PetStarDb db, GameData data) =>
        {
            var rows = await db.PackTypes.AsNoTracking().Where(r => r.Enabled).ToListAsync();
            var list = new List<PackOddsDto>();
            foreach (var r in rows) { var (p, ver) = await data.Pack(db, r.Id); list.Add(Odds(data.Catalog, p, ver)); }
            return list.OrderBy(o => data.Catalog.PackType(o.PackTypeId)?.Order ?? 99).ToList();
        }).RequireRateLimiting("public");

        var g = ServerEndpoints.GameGroup(app);

        g.MapGet("/packs", async (HttpContext ctx, PetStarDb db, GameData data, PackGate gate) =>
        {
            var (sid, mid) = ctx.User.Player();
            var owned = await db.PackInventory.AsNoTracking().Where(p => p.MembershipId == mid).ToDictionaryAsync(p => p.PackTypeId, p => p.Quantity);
            var rows = await db.PackTypes.AsNoTracking().OrderBy(p => p.Id).ToListAsync();
            var now = DateTimeOffset.UtcNow;
            var list = new List<PackShopItemDto>();
            foreach (var r in rows)
            {
                var (p, ver) = await data.Pack(db, r.Id);
                bool available = PackGate.OnSale(p, now);
                int have = owned.GetValueOrDefault(r.Id);
                if (!available && have == 0) continue;
                var check = available ? await gate.Check(p, mid, sid, 1) : new PackGate.Result("pack_not_available", null, null, 0);
                var ev = check.Event ?? (p.Requirements.RequiresEvent ? await gate.ActiveEvent(p.Id, sid, now) : null);
                var inv = p.Requirements.RequiresInvitation ? check.Invitation ?? await gate.ValidInvitation(mid, sid, now) : null;
                list.Add(new PackShopItemDto
                {
                    Id = p.Id, NameKey = p.NameKey, ArtAssetId = p.ArtAssetId, PriceCurrency = p.Price.Currency, PriceAmount = available ? p.Price.Amount : 0,
                    Owned = have, AvailableUntil = ev?.EndsAt ?? p.AvailableUntil, Order = p.Order, Theme = p.Theme, Names = p.Names, Descriptions = p.Descriptions,
                    MinLevel = p.Requirements.MinLevel, MinVip = p.Requirements.MinVip, RequiresEvent = p.Requirements.RequiresEvent,
                    RequiresInvitation = p.Requirements.RequiresInvitation, CanBuy = check.Blocked == null, BlockedReason = check.Blocked, Version = ver,
                    Event = ev == null ? null : new LimitedEventDto
                    {
                        Id = ev.Id, NameVi = ev.NameVi, NameEn = ev.NameEn, StartsAt = ev.StartsAt, EndsAt = ev.EndsAt, TotalLimit = ev.TotalLimit,
                        PerPlayerLimit = ev.PerPlayerLimit, Sold = ev.Sold,
                        BoughtByMe = await db.PackPurchases.Where(x => x.EventId == ev.Id && x.MembershipId == mid).SumAsync(x => (int?)x.Quantity) ?? 0,
                    },
                    InvitationPacksLeft = inv == null ? null : inv.MaxPacks - inv.UsedPacks, InvitationExpiresAt = inv?.ExpiresAt,
                });
            }
            return list.OrderBy(x => x.Order).ToList();
        });

        g.MapGet("/vip", async (HttpContext ctx, VipService vip) => await vip.Get(ctx.User.Player().membershipId));

        g.MapGet("/packs/purchases", async (string? packTypeId, int? limit, HttpContext ctx, PetStarDb db) =>
        {
            var (sid, mid) = ctx.User.Player();
            var q = db.PackPurchases.AsNoTracking().Where(x => x.MembershipId == mid && x.ServerId == sid);
            if (!string.IsNullOrEmpty(packTypeId)) q = q.Where(x => x.PackTypeId == packTypeId);
            return await q.OrderByDescending(x => x.CreatedAt).Take(Math.Clamp(limit ?? 50, 1, 200))
                .Select(x => new { x.Id, x.PackTypeId, x.PackVersion, x.Quantity, x.Currency, x.Amount, x.EventId, x.InvitationId, x.CreatedAt }).ToListAsync();
        });

        // All conditions are evaluated by the server inside the purchase transaction (level, VIP, event window and
        // limits, Star invitation). Event counters and invitation uses are consumed with guarded atomic updates.
        g.MapPost("/packs/purchase", (PurchasePackRequest req, HttpContext ctx, PetStarDb db, GameData data,
            EconomyService eco, IdempotencyService idem, PackGate gate, VipService vip) =>
            idem.Run(ctx, "packs.purchase", req, async () =>
            {
                var (sid, mid) = ctx.User.Player();
                if (req.Quantity < 1 || req.Quantity > 10) throw ApiException.BadRequest("invalid_quantity");
                var (p, ver) = await data.Pack(db, req.PackTypeId);
                var r = p.Requirements;
                // serialise this player's gated purchases so per-player limits cannot be raced
                if (r.RequiresEvent || r.RequiresInvitation)
                    await db.Database.ExecuteSqlRawAsync("SELECT 1 FROM server_memberships WHERE id = {0} FOR UPDATE", mid);
                var check = await gate.Check(p, mid, sid, req.Quantity);
                if (check.Blocked != null) throw ApiException.Conflict(check.Blocked);
                var purchaseId = Guid.NewGuid();
                if (check.Event != null)
                {
                    int n = await db.Database.ExecuteSqlRawAsync(
                        "UPDATE limited_events SET sold = sold + {0}, updated_at = now() WHERE id = {1} AND NOT cancelled AND starts_at <= now() AND ends_at > now() AND (total_limit IS NULL OR sold + {0} <= total_limit)",
                        req.Quantity, check.Event.Id);
                    if (n == 0) throw ApiException.Conflict("pack_event_sold_out");
                }
                if (check.Invitation != null)
                {
                    int n = await db.Database.ExecuteSqlRawAsync(
                        "UPDATE star_invitations SET used_packs = used_packs + {0} WHERE id = {1} AND status = 'active' AND expires_at > now() AND used_packs + {0} <= max_packs",
                        req.Quantity, check.Invitation.Id);
                    if (n == 0) throw ApiException.Conflict("pack_invitation_exhausted");
                }
                long cost = (long)p.Price.Amount * req.Quantity;
                await eco.Currency(mid, sid, p.Price.Currency, -cost, "pack_purchase", purchaseId.ToString("N"));
                var after = await eco.Packs(mid, sid, p.Id, req.Quantity, "pack_purchase", purchaseId.ToString("N"));
                db.PackPurchases.Add(new PackPurchase
                {
                    Id = purchaseId, MembershipId = mid, ServerId = sid, PackTypeId = p.Id, PackVersion = ver, Quantity = req.Quantity,
                    Currency = p.Price.Currency, Amount = cost, EventId = check.Event?.Id, InvitationId = check.Invitation?.Id, CreatedAt = DateTimeOffset.UtcNow,
                });
                await db.SaveChangesAsync();
                if (p.Price.Currency == Currency.RainbowStar) await vip.AddPoints(mid, sid, cost);
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
                // Divine Pet Cards are created at 10 stars; every other card starts at 1 star
                var cards = await factory.Create(sid, mid, rolled.Select(r => new NewCard(r.FormId, r.Rarity, r.Quality, r.Finish,
                    r.Rarity == RarityDef.Divine ? Progression.DivineStars : 1)).ToList(), "pack", opening.Id.ToString("N"));
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

    /// <summary>Public odds: per slot, and the chance of at least one card of each tier (and of that tier or better)
    /// in a whole pack. All 5 slots use one table, so pack odds are exact: 1 − (1 − p)^5.</summary>
    public static PackOddsDto Odds(GameCatalog cat, PackTypeDef p, int ver)
    {
        static double R(double x) => Math.Round(x, 6);
        static Dictionary<string, double> Pct(Dictionary<string, int> t) =>
            t.Where(x => x.Value > 0).ToDictionary(x => x.Key, x => Math.Round(x.Value / 10000.0, 4));
        var order = cat.Rarities.OrderBy(r => r.Order).Select(r => r.Id).ToList();
        int n = p.CardsPerPack;
        var rows = new List<PackOddsRow>();
        foreach (var r in order.Where(r => p.SlotTable.GetValueOrDefault(r) > 0))
        {
            double ps = p.SlotTable[r] / 1e6;
            double better = order.Where(x => order.IndexOf(x) >= order.IndexOf(r)).Sum(x => p.SlotTable.GetValueOrDefault(x)) / 1e6;
            rows.Add(new PackOddsRow { Rarity = r, PerSlot = R(ps * 100), AtLeastOneInPack = R((1 - Math.Pow(1 - ps, n)) * 100), AtLeastOneThisOrBetter = R((1 - Math.Pow(1 - better, n)) * 100) });
        }
        var divine = PackRoller.DivinePool(cat, p);
        double dg = p.SlotTable.GetValueOrDefault(RarityDef.Divine) / 1e6;
        return new PackOddsDto
        {
            PackTypeId = p.Id, Version = ver, Slots1To4 = Pct(p.SlotTable), Slot5 = Pct(p.FinalSlotTable.Count > 0 ? p.FinalSlotTable : p.SlotTable),
            SameTableAllSlots = p.FinalSlotTable.Count == 0, Quality = Pct(p.QualityTable), Finish = Pct(p.FinishTable),
            PossiblePets = PackRoller.Pool(cat, p).Select(f => f.Id).ToList(), Pack = rows,
            DivinePets = divine.Select(f => f.Id).ToList(), DivineGroupPerSlot = R(dg * 100),
            DivinePerPetPerSlot = divine.Count == 0 ? 0 : R(dg / divine.Count * 100), DivineAtLeastOneInPack = R((1 - Math.Pow(1 - dg, n)) * 100),
        };
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
                var snapshot = new CardInstance { Id = r.CardId, FormId = r.FormId, Rarity = r.Rarity, Quality = r.Quality, Finish = r.Finish, Level = 1, SkillLevel = 1,
                    Stars = r.Rarity == RarityDef.Divine ? Progression.DivineStars : 1 };
                if (cards.TryGetValue(r.CardId, out var c)) { snapshot.IsFavorite = c.IsFavorite; snapshot.IsLocked = c.IsLocked; snapshot.Serial = c.Serial; }
                return data.ToDto(snapshot, r.Slot);
            }).ToList(),
        };
    }
}
