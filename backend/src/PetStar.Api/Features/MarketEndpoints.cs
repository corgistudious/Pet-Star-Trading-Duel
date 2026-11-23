using Microsoft.EntityFrameworkCore;
using PetStar.Api.Data;
using PetStar.Api.Infrastructure;
using PetStar.Contracts;
using PetStar.Rules;

namespace PetStar.Api.Features;

public sealed class CreateListingRequest { public long CardId { get; set; } public string Currency { get; set; } = ""; public long Price { get; set; } public int? Days { get; set; } public bool ConfirmPriceWarning { get; set; } }
public sealed class BuyRequest { public long ListingId { get; set; } public long ExpectedPrice { get; set; } }
public sealed class WishlistRequest { public string? FormId { get; set; } public string? FamilyId { get; set; } public string? MinRarity { get; set; } public string? Quality { get; set; } public string? Finish { get; set; } public string? Currency { get; set; } public long? MaxPrice { get; set; } }

public sealed record ListingView(long ListingId, CardDto Card, string SellerNickname, long SellerUid, string Currency, long Price, int FeeBps, long Fee,
    long SellerNet, string Status, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, int Views, bool IsMine, long? EstimatedValue);

public static class MarketEndpoints
{
    private static readonly string[] HighRarities = { "SSR", "UR", "LR", "MR", "GR", "EX", "Secret" };

    public static long Fee(long price, int bps) => price * bps / 10_000;

    public static void Map(WebApplication app)
    {
        var g = ServerEndpoints.GameGroup(app);

        g.MapGet("/market/config", (GameData data) => new
        {
            feeBps = data.Market.FeeBps, auctionFeeBps = data.Market.AuctionFeeBps, currencies = Currency.All,
            listing = data.Market.Raw["listing"], auction = data.Market.Raw["auction"],
            notice = "In-game currencies only. No real-money sales, withdrawals or exchange.",
        });

        g.MapGet("/market/sell-quote", async (long cardId, string currency, long price, HttpContext ctx, PetStarDb db, GameData data, ValuationService val) =>
        {
            var (sid, mid) = ctx.User.Player();
            var card = await db.Cards.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cardId && c.OwnerMembershipId == mid && c.ServerId == sid) ?? throw ApiException.NotFound("card_not_found");
            if (!Currency.All.Contains(currency)) throw ApiException.BadRequest("unknown_currency");
            var est = await val.Estimate(sid, card, currency);
            long fee = Fee(price, data.Market.FeeBps);
            return new { price, currency, feeBps = data.Market.FeeBps, fee, sellerNet = price - fee, reference = est, warning = PriceWarning(data, price, est.Estimated) };
        });

        g.MapPost("/market/listings", (CreateListingRequest req, HttpContext ctx, PetStarDb db, GameData data, ValuationService val,
            NotificationService notes, ContentGuard guard, IdempotencyService idem) =>
            idem.Run(ctx, "market.list", req, async () =>
            {
                var (sid, mid) = ctx.User.Player();
                await guard.EnsureAllowed(ctx.User.AccountId(), "market");
                var m = data.Market;
                if (!Currency.All.Contains(req.Currency)) throw ApiException.BadRequest("unknown_currency");
                // the Pet market trades in Mini Green Beans only (in-game currency; never exchangeable for real money)
                if (req.Currency != Currency.MiniGreenBean) throw ApiException.BadRequest("market_currency_beans_only");
                if (req.Price < m.L("listing", "minPrice") || req.Price > m.L("listing", "maxPrice")) throw ApiException.BadRequest("price_out_of_range");
                int days = req.Days ?? (int)m.L("listing", "defaultDays");
                if (days < m.L("listing", "minDays") || days > m.L("listing", "maxDays")) throw ApiException.BadRequest("invalid_duration");
                if (await db.Listings.CountAsync(l => l.SellerMembershipId == mid && l.Status == ListingStatus.Active) >= m.L("listing", "maxActivePerPlayer"))
                    throw ApiException.Conflict("too_many_listings");
                var card = await LockCard(db, req.CardId, mid, sid);
                if (card.IsLocked) throw ApiException.Conflict("card_locked");
                if (card.BusyState != null) throw ApiException.Conflict("card_busy");
                var est = await val.Estimate(sid, card, req.Currency);
                var warning = PriceWarning(data, req.Price, est.Estimated);
                if (warning != null && !req.ConfirmPriceWarning) throw ApiException.Conflict("price_confirmation_required", warning);
                var now = DateTimeOffset.UtcNow;
                var listing = new MarketListing
                {
                    ServerId = sid, SellerMembershipId = mid, CardId = card.Id, Currency = req.Currency, Price = req.Price, FeeBps = m.FeeBps,
                    CreatedAt = now, ExpiresAt = now.AddDays(days),
                };
                db.Listings.Add(listing);
                await db.SaveChangesAsync();
                card.BusyState = "listing:" + listing.Id; card.UpdatedAt = now;
                await db.SaveChangesAsync();
                await MatchWishlists(db, data, notes, sid, mid, card, "listing", listing.Id, req.Currency, req.Price);
                return await View(db, data, val, listing, mid);
            }));

        g.MapPost("/market/listings/{id:long}/cancel", async (long id, HttpContext ctx, PetStarDb db, GameData data, ValuationService val) =>
        {
            var (sid, mid) = ctx.User.Player();
            await using var tx = await db.Database.BeginTransactionAsync();
            var l = await db.Listings.FromSqlRaw("SELECT * FROM market_listings WHERE id = {0} AND server_id = {1} FOR UPDATE", id, sid).FirstOrDefaultAsync()
                    ?? throw ApiException.NotFound("listing_not_found");
            if (l.SellerMembershipId != mid) throw ApiException.Forbidden("not_your_listing");
            if (l.Status != ListingStatus.Active) throw ApiException.Conflict("listing_not_active");
            await CloseListing(db, l, ListingStatus.Cancelled);
            await tx.CommitAsync();
            return await View(db, data, val, l, mid);
        });

        // Atomic purchase: listing + card locked, buyer debited, seller credited (minus fee), fee to system sink, card
        // transferred, history/ledger written — all in one transaction. Idempotency key prevents double purchase on retry.
        g.MapPost("/market/buy", (BuyRequest req, HttpContext ctx, PetStarDb db, GameData data, EconomyService eco, FraudService fraud,
            NotificationService notes, AchievementService ach, ContentGuard guard, ValuationService val, IdempotencyService idem) =>
            idem.Run(ctx, "market.buy", req, async () =>
            {
                var (sid, mid) = ctx.User.Player();
                await guard.EnsureAllowed(ctx.User.AccountId(), "market");
                var l = await db.Listings.FromSqlRaw("SELECT * FROM market_listings WHERE id = {0} AND server_id = {1} FOR UPDATE", req.ListingId, sid)
                    .FirstOrDefaultAsync() ?? throw ApiException.NotFound("listing_not_found");
                if (l.Status != ListingStatus.Active) throw ApiException.Conflict("listing_not_active");
                if (l.ExpiresAt <= DateTimeOffset.UtcNow) throw ApiException.Conflict("listing_expired");
                if (l.SellerMembershipId == mid) throw ApiException.Conflict("cannot_buy_own_listing");
                if (req.ExpectedPrice != 0 && req.ExpectedPrice != l.Price) throw ApiException.Conflict("price_changed");
                if (await IsBlocked(db, mid, l.SellerMembershipId)) throw ApiException.Forbidden("user_blocked");
                var card = await db.Cards.FromSqlRaw("SELECT * FROM card_instances WHERE id = {0} FOR UPDATE", l.CardId).FirstAsync();
                if (card.OwnerMembershipId != l.SellerMembershipId || card.BusyState != "listing:" + l.Id || card.ServerId != sid)
                    throw ApiException.Conflict("listing_invalid");
                var tx = await Settle(db, data, eco, fraud, val, sid, "listing", l.Id, card, l.SellerMembershipId, mid, l.Currency, l.Price, l.FeeBps, "market");
                l.Status = ListingStatus.Sold; l.BuyerMembershipId = mid; l.ClosedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync();
                await notes.Notify(l.SellerMembershipId, "market_sold", new { listingId = l.Id, serial = card.Serial, formId = card.FormId, l.Currency, l.Price, net = tx.SellerNet }, "market_sold:" + l.Id);
                await notes.Notify(mid, "market_bought", new { listingId = l.Id, serial = card.Serial, formId = card.FormId, l.Currency, l.Price }, "market_bought:" + l.Id);
                await ach.Evaluate(l.SellerMembershipId, sid, "market_sale");
                await ach.Evaluate(mid, sid, "market_purchase", "codex_count", "family_complete", "element_complete", "rare_obtained", "rare_owned_distinct");
                return new { transactionId = tx.Id, listingId = l.Id, card = data.ToDto(card), l.Currency, price = l.Price, currencies = await eco.Balances(mid) };
            }));

        g.MapGet("/market/listings", async (string? q, string? serial, string? rarity, string? quality, string? finish, string? element, int? stage,
            string? currency, long? minPrice, long? maxPrice, string? sort, string? section, int? page, int? pageSize,
            HttpContext ctx, PetStarDb db, GameData data, ValuationService val) =>
        {
            var (sid, mid) = ctx.User.Player();
            var cat = data.Catalog;
            var query = db.Listings.AsNoTracking().Where(l => l.ServerId == sid);
            query = section == "mine" ? query.Where(l => l.SellerMembershipId == mid) : query.Where(l => l.Status == ListingStatus.Active && l.ExpiresAt > DateTimeOffset.UtcNow);
            if (section != "mine")
            {
                var blocked = await BlockedIds(db, mid);
                if (blocked.Count > 0) query = query.Where(l => !blocked.Contains(l.SellerMembershipId));
            }
            if (!string.IsNullOrEmpty(currency)) query = query.Where(l => l.Currency == currency);
            if (minPrice.HasValue) query = query.Where(l => l.Price >= minPrice);
            if (maxPrice.HasValue) query = query.Where(l => l.Price <= maxPrice);
            var joined = query.Join(db.Cards, l => l.CardId, c => c.Id, (l, c) => new { l, c });
            if (!string.IsNullOrEmpty(serial)) { var s = serial.Trim().ToUpperInvariant(); joined = joined.Where(x => x.c.Serial.StartsWith(s)); }
            if (!string.IsNullOrEmpty(rarity)) { var set = rarity.Split(','); joined = joined.Where(x => set.Contains(x.c.Rarity)); }
            if (!string.IsNullOrEmpty(quality)) { var set = quality.Split(','); joined = joined.Where(x => set.Contains(x.c.Quality)); }
            if (!string.IsNullOrEmpty(finish)) { var set = finish.Split(','); joined = joined.Where(x => set.Contains(x.c.Finish)); }
            if (!string.IsNullOrEmpty(q) || !string.IsNullOrEmpty(element) || stage.HasValue)
            {
                var forms = cat.Forms.Values.Where(f => (string.IsNullOrEmpty(q) || f.Name.Contains(q.Trim(), StringComparison.OrdinalIgnoreCase))
                    && (string.IsNullOrEmpty(element) || f.Element == element) && (!stage.HasValue || f.Stage == stage)).Select(f => f.Id).ToList();
                joined = joined.Where(x => forms.Contains(x.c.FormId));
            }
            if (section == "rare") joined = joined.Where(x => HighRarities.Contains(x.c.Rarity));
            if (section == "wishlist" || section == "recommended")
            {
                var wishes = await db.Wishlists.AsNoTracking().Where(w => w.MembershipId == mid).ToListAsync();
                var wishForms = wishes.SelectMany(w => w.FormId != null ? new[] { w.FormId } : w.FamilyId != null ? cat.Families.First(f => f.Id == w.FamilyId).Forms.Select(f => f.Id).ToArray() : Array.Empty<string>()).ToHashSet();
                if (section == "recommended")
                {
                    var known = (await db.Codex.AsNoTracking().Where(c => c.MembershipId == mid).Select(c => c.FormId).ToListAsync()).ToHashSet();
                    wishForms.UnionWith(cat.Forms.Keys.Where(k => !known.Contains(k)));   // fill Codex gaps
                }
                var wl = wishForms.ToList();
                joined = joined.Where(x => wl.Contains(x.c.FormId));
            }
            var rows = await joined.Select(x => x.l).ToListAsync();
            var rank = data.Catalog.Rarities.ToDictionary(r => r.Id, r => r.Order);
            var cards = await db.Cards.AsNoTracking().Where(c => rows.Select(r => r.CardId).Contains(c.Id)).ToDictionaryAsync(c => c.Id);
            IEnumerable<MarketListing> sorted = (sort ?? (section == "new" ? "newest" : "newest")) switch
            {
                "price_asc" => rows.OrderBy(r => r.Price),
                "price_desc" => rows.OrderByDescending(r => r.Price),
                "popular" => rows.OrderByDescending(r => r.Views).ThenByDescending(r => r.CreatedAt),
                "rarity" => rows.OrderByDescending(r => rank[cards[r.CardId].Rarity]).ThenBy(r => r.Price),
                _ => rows.OrderByDescending(r => r.CreatedAt),
            };
            int ps = Math.Clamp(pageSize ?? 30, 1, 100), pg = Math.Max(1, page ?? 1);
            var pageRows = sorted.Skip((pg - 1) * ps).Take(ps).ToList();
            var items = new List<ListingView>();
            foreach (var l in pageRows) items.Add(await View(db, data, val, l, mid, cards[l.CardId], withEstimate: false));
            return new { total = rows.Count, page = pg, pageSize = ps, items };
        });

        g.MapGet("/market/listings/{id:long}", async (long id, HttpContext ctx, PetStarDb db, GameData data, ValuationService val) =>
        {
            var (sid, mid) = ctx.User.Player();
            var l = await db.Listings.FirstOrDefaultAsync(x => x.Id == id && x.ServerId == sid) ?? throw ApiException.NotFound("listing_not_found");
            if (l.SellerMembershipId != mid) await db.Listings.Where(x => x.Id == id).ExecuteUpdateAsync(u => u.SetProperty(x => x.Views, x => x.Views + 1));
            var card = await db.Cards.AsNoTracking().FirstAsync(c => c.Id == l.CardId);
            var trades = await db.MarketTransactions.AsNoTracking().Where(t => t.CardId == card.Id).OrderByDescending(t => t.At).Take(20)
                .Select(t => new { t.At, t.Currency, t.Price, t.Kind }).ToListAsync();
            return new { listing = await View(db, data, val, l, mid, card), cardTradeHistory = trades };
        });

        g.MapGet("/market/history", async (string? type, int? limit, HttpContext ctx, PetStarDb db, GameData data) =>
        {
            var (sid, mid) = ctx.User.Player();
            var q = db.MarketTransactions.AsNoTracking().Where(t => t.ServerId == sid);
            q = type == "sales" ? q.Where(t => t.SellerMembershipId == mid) : q.Where(t => t.BuyerMembershipId == mid);
            var rows = await q.OrderByDescending(t => t.At).Take(Math.Clamp(limit ?? 50, 1, 200)).ToListAsync();
            var serials = await db.Cards.AsNoTracking().Where(c => rows.Select(r => r.CardId).Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Serial);
            return rows.Select(t => new { t.Id, t.Kind, t.At, t.FormId, t.Rarity, serial = serials.GetValueOrDefault(t.CardId), t.Currency, t.Price, t.Fee, sellerNet = type == "sales" ? t.SellerNet : (long?)null });
        });

        g.MapGet("/market/wishlist", async (HttpContext ctx, PetStarDb db) =>
        {
            var (_, mid) = ctx.User.Player();
            return await db.Wishlists.AsNoTracking().Where(w => w.MembershipId == mid).OrderBy(w => w.Id).ToListAsync();
        });

        g.MapPost("/market/wishlist", async (WishlistRequest req, HttpContext ctx, PetStarDb db, GameData data) =>
        {
            var (sid, mid) = ctx.User.Player();
            if (req.FormId == null && req.FamilyId == null) throw ApiException.BadRequest("wishlist_target_required");
            if (req.FormId != null && !data.Catalog.Forms.ContainsKey(req.FormId)) throw ApiException.BadRequest("form_not_found");
            if (req.FamilyId != null && data.Catalog.Families.All(f => f.Id != req.FamilyId)) throw ApiException.BadRequest("family_not_found");
            if (req.Currency != null && !Currency.All.Contains(req.Currency)) throw ApiException.BadRequest("unknown_currency");
            if (await db.Wishlists.CountAsync(w => w.MembershipId == mid) >= data.Market.L("wishlist", "maxEntries")) throw ApiException.Conflict("wishlist_full");
            var w = new WishlistEntry { MembershipId = mid, ServerId = sid, FormId = req.FormId, FamilyId = req.FamilyId, MinRarity = req.MinRarity, Quality = req.Quality, Finish = req.Finish, Currency = req.Currency, MaxPrice = req.MaxPrice, CreatedAt = DateTimeOffset.UtcNow };
            db.Wishlists.Add(w);
            await db.SaveChangesAsync();
            return w;
        });

        g.MapDelete("/market/wishlist/{id:long}", async (long id, HttpContext ctx, PetStarDb db) =>
        {
            var (_, mid) = ctx.User.Player();
            int n = await db.Wishlists.Where(w => w.Id == id && w.MembershipId == mid).ExecuteDeleteAsync();
            return n == 0 ? throw ApiException.NotFound() : Results.Ok(new { deleted = true });
        });
    }

    public static string? PriceWarning(GameData data, long price, long reference)
    {
        if (reference <= 0) return null;
        if (price < reference * data.Market.D("listing", "confirmBelowFraction")) return "price_far_below_reference";
        if (price > reference * data.Market.D("listing", "confirmAboveMultiple")) return "price_far_above_reference";
        return null;
    }

    public static async Task<CardInstance> LockCard(PetStarDb db, long cardId, long mid, int sid) =>
        await db.Cards.FromSqlRaw("SELECT * FROM card_instances WHERE id = {0} AND owner_membership_id = {1} AND server_id = {2} FOR UPDATE", cardId, mid, sid)
            .FirstOrDefaultAsync() ?? throw ApiException.NotFound("card_not_found");

    /// <summary>Shared settlement for listings and auctions (caller holds row locks, inside a transaction).</summary>
    public static async Task<MarketTransaction> Settle(PetStarDb db, GameData data, EconomyService eco, FraudService fraud, ValuationService val,
        int sid, string kind, long refId, CardInstance card, long sellerMid, long buyerMid, string currency, long price, int feeBps, string reasonPrefix,
        bool buyerAlreadyDebited = false)
    {
        long fee = Fee(price, feeBps);
        string r = kind + ":" + refId;
        if (!buyerAlreadyDebited) await eco.Currency(buyerMid, sid, currency, -price, reasonPrefix + "_buy", r);
        await eco.Currency(sellerMid, sid, currency, price - fee, reasonPrefix + "_sale", r);
        if (fee > 0)
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO economy_system_ledger (server_id, currency, amount, reason, ref_id, at) VALUES ({0}, {1}, {2}, {3}, {4}, now())",
                sid, currency, fee, reasonPrefix + "_fee", r);
        var est = await val.Estimate(sid, card, currency);
        var assessment = await fraud.AssessTrade(sid, sellerMid, buyerMid, card.Id, price, est.Initial);
        await CardTransfer.Transfer(db, card, buyerMid, kind == "listing" ? "market_sale" : "auction_sale", r);
        var tx = new MarketTransaction
        {
            ServerId = sid, Kind = kind, RefId = refId, CardId = card.Id, FormId = card.FormId, Rarity = card.Rarity, SellerMembershipId = sellerMid,
            BuyerMembershipId = buyerMid, Currency = currency, Price = price, Fee = fee, SellerNet = price - fee, Suspicious = assessment.Suspicious,
            SuspicionReasons = assessment.Suspicious ? string.Join(",", assessment.Reasons) : null, At = DateTimeOffset.UtcNow,
        };
        db.MarketTransactions.Add(tx);
        await db.SaveChangesAsync();
        db.PriceHistory.Add(new MarketPriceHistory
        {
            ServerId = sid, TransactionId = tx.Id, FormId = card.FormId, Rarity = card.Rarity, Currency = currency, Price = price,
            NormalizedPrice = Math.Max(1, (long)Math.Round(price / val.VariantMultiplier(card.Quality, card.Finish))), Excluded = assessment.Suspicious, At = tx.At,
        });
        if (assessment.Suspicious)
            fraud.Record(sid, "suspicious_trade", "warning", buyerMid, card.Id, r, new { tx.Id, assessment.Reasons, sellerMid, buyerMid, price, currency });
        await db.SaveChangesAsync();
        return tx;
    }

    public static async Task CloseListing(PetStarDb db, MarketListing l, string status)
    {
        l.Status = status; l.ClosedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("UPDATE card_instances SET busy_state = NULL, updated_at = now() WHERE id = {0} AND busy_state = {1}", l.CardId, "listing:" + l.Id);
    }

    public static async Task<ListingView> View(PetStarDb db, GameData data, ValuationService val, MarketListing l, long viewerMid, CardInstance? card = null, bool withEstimate = true)
    {
        card ??= await db.Cards.AsNoTracking().FirstAsync(c => c.Id == l.CardId);
        var seller = await db.Memberships.AsNoTracking().FirstAsync(m => m.Id == l.SellerMembershipId);
        var dto = await data.ToDtoFull(db, card);   // buyers see stars/enhancement/breakthrough/awakening/skill/engravings
        if (l.SellerMembershipId != viewerMid) dto.Id = 0;
        long fee = Fee(l.Price, l.FeeBps);
        long? est = withEstimate ? (await val.Estimate(l.ServerId, card, l.Currency)).Estimated : null;
        return new ListingView(l.Id, dto, seller.Nickname, seller.AccountId, l.Currency, l.Price, l.FeeBps, fee, l.Price - fee, l.Status, l.CreatedAt, l.ExpiresAt, l.Views, l.SellerMembershipId == viewerMid, est);
    }

    public static async Task<List<long>> BlockedIds(PetStarDb db, long mid) =>
        await db.Blocks.AsNoTracking().Where(b => b.BlockerMembershipId == mid).Select(b => b.BlockedMembershipId)
            .Union(db.Blocks.AsNoTracking().Where(b => b.BlockedMembershipId == mid).Select(b => b.BlockerMembershipId)).ToListAsync();

    public static Task<bool> IsBlocked(PetStarDb db, long a, long b) =>
        db.Blocks.AnyAsync(x => (x.BlockerMembershipId == a && x.BlockedMembershipId == b) || (x.BlockerMembershipId == b && x.BlockedMembershipId == a));

    /// <summary>Notifies wishlist owners about a newly public listing/auction (deduped per wishlist + listing, rate limited).</summary>
    public static async Task MatchWishlists(PetStarDb db, GameData data, NotificationService notes, int sid, long sellerMid, CardInstance card,
        string kind, long refId, string currency, long price)
    {
        var form = data.Catalog.Form(card.FormId);
        int rank = data.Catalog.Rarity(card.Rarity).Order;
        var candidates = await db.Wishlists.AsNoTracking()
            .Where(w => w.ServerId == sid && w.MembershipId != sellerMid && (w.FormId == card.FormId || w.FamilyId == form.FamilyId)).ToListAsync();
        var hourAgo = DateTimeOffset.UtcNow.AddHours(-1);
        long maxPerHour = data.Market.L("wishlist", "maxAlertsPerHour");
        foreach (var w in candidates)
        {
            if (w.MinRarity != null && rank < data.Catalog.Rarity(w.MinRarity).Order) continue;
            if (w.Quality != null && w.Quality != card.Quality) continue;
            if (w.Finish != null && w.Finish != card.Finish) continue;
            if (w.Currency != null && w.Currency != currency) continue;
            if (w.MaxPrice != null && price > w.MaxPrice) continue;
            if (await IsBlocked(db, w.MembershipId, sellerMid)) continue;
            if (await db.PriceAlerts.CountAsync(a => a.MembershipId == w.MembershipId && a.At >= hourAgo) >= maxPerHour) continue;
            int n = await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO price_alerts (wishlist_id, membership_id, kind, ref_id, at) VALUES ({0}, {1}, {2}, {3}, now()) ON CONFLICT DO NOTHING", w.Id, w.MembershipId, kind, refId);
            if (n == 1)
                await notes.Notify(w.MembershipId, "wishlist_match", new { kind, refId, formId = card.FormId, rarity = card.Rarity, currency, price }, $"wishlist:{w.Id}:{kind}:{refId}");
        }
    }
}
