using Microsoft.EntityFrameworkCore;
using PetStar.Api.Data;
using PetStar.Api.Infrastructure;
using PetStar.Rules;

namespace PetStar.Api.Features;

public sealed class CreateAuctionRequest { public long CardId { get; set; } public string Currency { get; set; } = ""; public long StartPrice { get; set; } public long? BuyoutPrice { get; set; } public int Hours { get; set; } = 24; }
public sealed class BidRequest { public long AuctionId { get; set; } public long Amount { get; set; } }

public static class AuctionEndpoints
{
    public static void Map(WebApplication app)
    {
        var g = ServerEndpoints.GameGroup(app);

        g.MapPost("/auctions", (CreateAuctionRequest req, HttpContext ctx, PetStarDb db, GameData data, NotificationService notes, ContentGuard guard, IdempotencyService idem) =>
            idem.Run(ctx, "auctions.create", req, async () =>
            {
                var (sid, mid) = ctx.User.Player();
                await guard.EnsureAllowed(ctx.User.AccountId(), "market");
                var m = data.Market;
                if (!Currency.All.Contains(req.Currency)) throw ApiException.BadRequest("unknown_currency");
                if (req.StartPrice < m.L("listing", "minPrice") || req.StartPrice > m.L("listing", "maxPrice")) throw ApiException.BadRequest("price_out_of_range");
                if (req.BuyoutPrice.HasValue && (req.BuyoutPrice < req.StartPrice || req.BuyoutPrice > m.L("listing", "maxPrice"))) throw ApiException.BadRequest("invalid_buyout");
                if (req.Hours < m.L("auction", "minHours") || req.Hours > m.L("auction", "maxHours")) throw ApiException.BadRequest("invalid_duration");
                if (await db.Auctions.CountAsync(a => a.SellerMembershipId == mid && a.Status == AuctionStatus.Active) >= m.L("auction", "maxActivePerPlayer"))
                    throw ApiException.Conflict("too_many_auctions");
                var card = await MarketEndpoints.LockCard(db, req.CardId, mid, sid);
                if (card.IsLocked) throw ApiException.Conflict("card_locked");
                if (card.BusyState != null) throw ApiException.Conflict("card_busy");
                var now = DateTimeOffset.UtcNow;
                var a = new AuctionListing
                {
                    ServerId = sid, SellerMembershipId = mid, CardId = card.Id, Currency = req.Currency, StartPrice = req.StartPrice, BuyoutPrice = req.BuyoutPrice,
                    MinIncrementBps = (int)m.L("auction", "minIncrementBps"), FeeBps = m.AuctionFeeBps, CreatedAt = now, EndsAt = now.AddHours(req.Hours),
                };
                db.Auctions.Add(a);
                await db.SaveChangesAsync();
                card.BusyState = "auction:" + a.Id; card.UpdatedAt = now;
                await db.SaveChangesAsync();
                await MarketEndpoints.MatchWishlists(db, data, notes, sid, mid, card, "auction", a.Id, req.Currency, req.StartPrice);
                return await View(db, data, a, mid);
            }));

        g.MapPost("/auctions/bid", (BidRequest req, HttpContext ctx, PetStarDb db, GameData data, EconomyService eco, FraudService fraud, ValuationService val,
            NotificationService notes, AchievementService ach, ContentGuard guard, IdempotencyService idem) =>
            idem.Run(ctx, "auctions.bid", req, async () =>
            {
                var (sid, mid) = ctx.User.Player();
                await guard.EnsureAllowed(ctx.User.AccountId(), "market");
                var a = await db.Auctions.FromSqlRaw("SELECT * FROM auction_listings WHERE id = {0} AND server_id = {1} FOR UPDATE", req.AuctionId, sid)
                    .FirstOrDefaultAsync() ?? throw ApiException.NotFound("auction_not_found");
                var now = DateTimeOffset.UtcNow;   // server time only
                if (a.Status != AuctionStatus.Active) throw ApiException.Conflict("auction_not_active");
                if (a.EndsAt <= now) throw ApiException.Conflict("auction_ended");
                if (a.SellerMembershipId == mid) throw ApiException.Conflict("self_bid_not_allowed");
                if (await MarketEndpoints.IsBlocked(db, mid, a.SellerMembershipId)) throw ApiException.Forbidden("user_blocked");
                long min = a.HighestAmount is long h ? h + Math.Max(1, h * a.MinIncrementBps / 10_000) : a.StartPrice;
                long amount = req.Amount;
                if (a.BuyoutPrice is long bo && amount > bo) amount = bo;
                bool isBuyout = a.BuyoutPrice is long bo2 && amount == bo2;
                if (amount < min && !isBuyout) throw ApiException.Conflict("bid_too_low", min.ToString());

                var bid = new AuctionBid { AuctionId = a.Id, BidderMembershipId = mid, Amount = amount, CreatedAt = now };
                if (await fraud.LinkedAccounts(mid, a.SellerMembershipId))
                {
                    bid.Flagged = true;
                    fraud.Record(sid, "linked_account_bid", "warning", mid, a.CardId, "auction:" + a.Id, new { seller = a.SellerMembershipId, amount });
                }
                db.AuctionBids.Add(bid);
                await db.SaveChangesAsync();
                // hold the full bid: cannot bid more than the spendable balance
                await eco.Currency(mid, sid, a.Currency, -amount, "auction_hold", "bid:" + bid.Id);
                db.ReservedBalances.Add(new ReservedBalance { MembershipId = mid, ServerId = sid, Currency = a.Currency, Amount = amount, Reason = "auction_bid", RefId = "bid:" + bid.Id, CreatedAt = now });
                if (a.HighestBidId is long prevId)
                    await ReleaseBid(db, eco, notes, prevId, "outbid", a);
                a.HighestBidId = bid.Id; a.HighestAmount = amount; a.HighestBidderMembershipId = mid; a.BidCount++;
                var snipe = TimeSpan.FromSeconds(data.Market.L("auction", "antiSnipeSeconds"));
                if (a.EndsAt - now < snipe && a.Extensions < data.Market.L("auction", "maxExtensions")) { a.EndsAt = now + snipe; a.Extensions++; }
                await db.SaveChangesAsync();
                if (a.BuyoutPrice is long buyout && amount >= buyout)
                    await Settle(db, data, eco, fraud, val, notes, ach, a);
                return new { bidId = bid.Id, auction = await View(db, data, a, mid), currencies = await eco.Balances(mid) };
            }));

        g.MapPost("/auctions/{id:long}/cancel", async (long id, HttpContext ctx, PetStarDb db, GameData data) =>
        {
            var (sid, mid) = ctx.User.Player();
            await using var tx = await db.Database.BeginTransactionAsync();
            var a = await db.Auctions.FromSqlRaw("SELECT * FROM auction_listings WHERE id = {0} AND server_id = {1} FOR UPDATE", id, sid).FirstOrDefaultAsync()
                    ?? throw ApiException.NotFound("auction_not_found");
            if (a.SellerMembershipId != mid) throw ApiException.Forbidden("not_your_auction");
            if (a.Status != AuctionStatus.Active) throw ApiException.Conflict("auction_not_active");
            if (a.BidCount > 0) throw ApiException.Conflict("auction_has_bids");
            a.Status = AuctionStatus.Cancelled; a.SettledAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlRawAsync("UPDATE card_instances SET busy_state = NULL WHERE id = {0} AND busy_state = {1}", a.CardId, "auction:" + a.Id);
            await tx.CommitAsync();
            return await View(db, data, a, mid);
        });

        g.MapGet("/auctions", async (string? section, string? currency, int? page, HttpContext ctx, PetStarDb db, GameData data) =>
        {
            var (sid, mid) = ctx.User.Player();
            var q = db.Auctions.AsNoTracking().Where(a => a.ServerId == sid);
            q = section switch
            {
                "mine" => q.Where(a => a.SellerMembershipId == mid),
                "bidding" => q.Where(a => db.AuctionBids.Any(b => b.AuctionId == a.Id && b.BidderMembershipId == mid)),
                _ => q.Where(a => a.Status == AuctionStatus.Active && a.EndsAt > DateTimeOffset.UtcNow),
            };
            if (!string.IsNullOrEmpty(currency)) q = q.Where(a => a.Currency == currency);
            int pg = Math.Max(1, page ?? 1);
            var rows = await q.OrderBy(a => section == null ? a.EndsAt : DateTimeOffset.MinValue).ThenByDescending(a => a.CreatedAt).Skip((pg - 1) * 30).Take(30).ToListAsync();
            var list = new List<object>();
            foreach (var a in rows) list.Add(await View(db, data, a, mid));
            return list;
        });

        g.MapGet("/auctions/{id:long}", async (long id, HttpContext ctx, PetStarDb db, GameData data) =>
        {
            var (sid, mid) = ctx.User.Player();
            var a = await db.Auctions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.ServerId == sid) ?? throw ApiException.NotFound("auction_not_found");
            var bids = await db.AuctionBids.AsNoTracking().Where(b => b.AuctionId == id).OrderByDescending(b => b.Amount).Take(30).ToListAsync();
            return new
            {
                auction = await View(db, data, a, mid),
                serverTime = DateTimeOffset.UtcNow,
                bids = bids.Select(b => new { b.Amount, b.CreatedAt, mine = b.BidderMembershipId == mid, b.Status }),
            };
        });
    }

    private static async Task ReleaseBid(PetStarDb db, EconomyService eco, NotificationService notes, long bidId, string newStatus, AuctionListing a)
    {
        var prev = await db.AuctionBids.FirstAsync(b => b.Id == bidId);
        var hold = await db.ReservedBalances.FirstAsync(r => r.Reason == "auction_bid" && r.RefId == "bid:" + bidId);
        if (hold.Status != "held") return;
        await eco.Currency(prev.BidderMembershipId, a.ServerId, hold.Currency, hold.Amount, "auction_release", "bid:" + bidId);
        hold.Status = "released"; hold.ClosedAt = DateTimeOffset.UtcNow;
        prev.Status = newStatus;
        await db.SaveChangesAsync();
        if (newStatus == "outbid")
            await notes.Notify(prev.BidderMembershipId, "auction_outbid", new { auctionId = a.Id, refunded = hold.Amount, a.Currency }, "outbid:" + bidId);
    }

    /// <summary>Settles an auction exactly once (caller holds FOR UPDATE on the auction, inside a transaction).</summary>
    public static async Task Settle(PetStarDb db, GameData data, EconomyService eco, FraudService fraud, ValuationService val,
        NotificationService notes, AchievementService ach, AuctionListing a)
    {
        if (a.Status != AuctionStatus.Active) return;
        var card = await db.Cards.FromSqlRaw("SELECT * FROM card_instances WHERE id = {0} FOR UPDATE", a.CardId).FirstAsync();
        var now = DateTimeOffset.UtcNow;
        if (a.HighestBidId is long winId && card.OwnerMembershipId == a.SellerMembershipId && card.BusyState == "auction:" + a.Id)
        {
            var bid = await db.AuctionBids.FirstAsync(b => b.Id == winId);
            var hold = await db.ReservedBalances.FirstAsync(r => r.Reason == "auction_bid" && r.RefId == "bid:" + winId);
            var tx = await MarketEndpoints.Settle(db, data, eco, fraud, val, a.ServerId, "auction", a.Id, card, a.SellerMembershipId, bid.BidderMembershipId,
                a.Currency, bid.Amount, a.FeeBps, "auction", buyerAlreadyDebited: true);
            hold.Status = "captured"; hold.ClosedAt = now;
            bid.Status = "won";
            a.Status = AuctionStatus.Settled; a.SettledAt = now;
            db.AuctionSettlements.Add(new AuctionSettlement { AuctionId = a.Id, WinningBidId = winId, TransactionId = tx.Id, Outcome = "sold", At = now });
            await db.SaveChangesAsync();
            await notes.Notify(bid.BidderMembershipId, "auction_won", new { auctionId = a.Id, serial = card.Serial, formId = card.FormId, a.Currency, price = bid.Amount }, "auction_won:" + a.Id);
            await notes.Notify(a.SellerMembershipId, "auction_sold", new { auctionId = a.Id, serial = card.Serial, a.Currency, price = bid.Amount, net = tx.SellerNet }, "auction_sold:" + a.Id);
            await ach.Evaluate(bid.BidderMembershipId, a.ServerId, "auction_win", "codex_count", "family_complete", "element_complete", "rare_owned_distinct");
            await ach.Evaluate(a.SellerMembershipId, a.ServerId, "market_sale");
        }
        else
        {
            if (a.HighestBidId is long orphan) await ReleaseBid(db, eco, notes, orphan, "refunded", a);   // card invalid: refund
            a.Status = AuctionStatus.NoBids; a.SettledAt = now;
            db.AuctionSettlements.Add(new AuctionSettlement { AuctionId = a.Id, Outcome = a.HighestBidId == null ? "no_bids" : "invalid_card_refunded", At = now });
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlRawAsync("UPDATE card_instances SET busy_state = NULL WHERE id = {0} AND busy_state = {1}", a.CardId, "auction:" + a.Id);
            await notes.Notify(a.SellerMembershipId, "auction_ended_no_sale", new { auctionId = a.Id, serial = card.Serial }, "auction_nosale:" + a.Id);
        }
    }

    public static async Task<object> View(PetStarDb db, GameData data, AuctionListing a, long viewerMid)
    {
        var card = await db.Cards.AsNoTracking().FirstAsync(c => c.Id == a.CardId);
        var seller = await db.Memberships.AsNoTracking().FirstAsync(m => m.Id == a.SellerMembershipId);
        var dto = data.ToDto(card);
        if (a.SellerMembershipId != viewerMid) dto.Id = 0;
        long? nextMin = a.Status != AuctionStatus.Active ? null : a.HighestAmount is long h ? h + Math.Max(1, h * a.MinIncrementBps / 10_000) : a.StartPrice;
        return new
        {
            auctionId = a.Id, card = dto, sellerNickname = seller.Nickname, a.Currency, a.StartPrice, a.BuyoutPrice, a.HighestAmount, a.BidCount,
            leading = a.HighestBidderMembershipId == viewerMid, isMine = a.SellerMembershipId == viewerMid, nextMinBid = nextMin,
            feeBps = a.FeeBps, a.Status, a.EndsAt, a.CreatedAt, serverTime = DateTimeOffset.UtcNow,
        };
    }
}

/// <summary>Background maintenance: expires listings, settles auctions at server time, refreshes valuation snapshots.</summary>
public sealed class MarketMaintenanceService(IServiceProvider sp, ILogger<MarketMaintenanceService> log) : BackgroundService
{
    public static TimeSpan Interval = TimeSpan.FromSeconds(15);
    private DateTimeOffset _lastSnapshot = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try { await RunOnce(sp, stop); }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning(ex, "market maintenance failed"); }
            if (DateTimeOffset.UtcNow - _lastSnapshot > TimeSpan.FromHours(1))
            {
                try
                {
                    using var scope = sp.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<PetStarDb>();
                    var val = scope.ServiceProvider.GetRequiredService<ValuationService>();
                    foreach (var s in await db.Servers.AsNoTracking().Where(s => s.Status == ServerStatus.Online).Select(s => s.Id).ToListAsync(stop))
                        await val.SnapshotServer(s);
                    _lastSnapshot = DateTimeOffset.UtcNow;
                }
                catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning(ex, "valuation snapshot failed"); }
            }
            try { await Task.Delay(Interval, stop); } catch (OperationCanceledException) { }
        }
    }

    public static async Task RunOnce(IServiceProvider sp, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PetStarDb>();
            var notes = scope.ServiceProvider.GetRequiredService<NotificationService>();
            foreach (var id in await db.Listings.AsNoTracking().Where(l => l.Status == ListingStatus.Active && l.ExpiresAt <= now).Select(l => l.Id).Take(500).ToListAsync(ct))
            {
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                var l = await db.Listings.FromSqlRaw("SELECT * FROM market_listings WHERE id = {0} FOR UPDATE", id).FirstAsync(ct);
                if (l.Status != ListingStatus.Active) { await tx.RollbackAsync(ct); continue; }
                await MarketEndpoints.CloseListing(db, l, ListingStatus.Expired);
                await notes.Notify(l.SellerMembershipId, "listing_expired", new { listingId = l.Id }, "listing_expired:" + l.Id);
                await tx.CommitAsync(ct);
            }
        }
        List<long> due;
        using (var scope = sp.CreateScope())
            due = await scope.ServiceProvider.GetRequiredService<PetStarDb>().Auctions.AsNoTracking()
                .Where(a => a.Status == AuctionStatus.Active && a.EndsAt <= now).Select(a => a.Id).Take(200).ToListAsync(ct);
        foreach (var id in due)
        {
            using var scope = sp.CreateScope();
            var s = scope.ServiceProvider;
            var db = s.GetRequiredService<PetStarDb>();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var a = await db.Auctions.FromSqlRaw("SELECT * FROM auction_listings WHERE id = {0} FOR UPDATE", id).FirstAsync(ct);
            if (a.Status != AuctionStatus.Active || a.EndsAt > DateTimeOffset.UtcNow) { await tx.RollbackAsync(ct); continue; }
            await AuctionEndpoints.Settle(db, s.GetRequiredService<GameData>(), s.GetRequiredService<EconomyService>(), s.GetRequiredService<FraudService>(),
                s.GetRequiredService<ValuationService>(), s.GetRequiredService<NotificationService>(), s.GetRequiredService<AchievementService>(), a);
            await tx.CommitAsync(ct);
        }
    }
}
