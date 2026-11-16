using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using PetStar.Api.Features;
using PetStar.Api.Infrastructure;
using PetStar.Contracts;

namespace PetStar.Api.Tests;

public static class X
{
    public static async Task<Player> Owner(ApiFactory f)
    {
        var o = await f.NewPlayer("owner");
        await f.Sql("INSERT INTO account_roles (account_id, role, granted_at) VALUES (@p0, 'owner', now())", o.Uid);
        await o.Enter(1);
        return o;
    }

    public static async Task Fund(Player owner, Player target, string currency, long amount, int server = 1) =>
        await Player.Ok(await owner.Post("/api/admin/economy/adjust", new AdjustmentRequest { Uid = target.Uid, ServerId = server, Currency = currency, Delta = amount, Reason = "integration test funding" }));

    public static async Task<List<CardDto>> Open(Player p, int packs = 1)
    {
        var all = new List<CardDto>();
        for (int i = 0; i < packs; i++) all.AddRange((await p.PostOk<PackOpeningDto>("/api/packs/open", new OpenPackRequest { PackTypeId = "basic_pack" }, Player.Key())).Cards);
        return all;
    }

    public static async Task<JsonElement> J(HttpResponseMessage r)
    {
        await Player.Ok(r);
        return JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;
    }

    public static Task<JsonElement> GetJ(Player p, string url) => p.Http.GetAsync(url).ContinueWith(t => J(t.Result)).Unwrap();
    public static long Bal(PlayerDto p, string c) => p.Currencies[c];
    public const string Beans = "MINI_GREEN_BEAN";
    public const string Stars = "RAINBOW_STAR";
}

public class SerialUnitTests
{
    [Fact]
    public void One_million_serials_are_unique_and_well_formed()
    {
        var set = new HashSet<string>(1_000_000);
        for (int i = 0; i < 1_000_000; i++)
        {
            var s = SerialGenerator.Next();
            Assert.True(s.Length == 12 && s.All(c => (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')));
            Assert.True(set.Add(s), "duplicate serial generated");
        }
    }
}

[Collection("api")]
public class SerialAndPassportTests(ApiFactory f)
{
    [Fact]
    public async Task Cards_get_unique_permanent_serials_and_a_passport()
    {
        var p = await f.NewPlayer();
        var cards = await X.Open(p, 2);
        Assert.All(cards, c => Assert.True(SerialGenerator.IsValid(c.Serial)));
        Assert.Equal(10, cards.Select(c => c.Serial).Distinct().Count());
        Assert.Equal(10, await f.Scalar<long>("SELECT count(*) FROM card_serial_registry WHERE card_id = ANY(@p0)", cards.Select(c => c.Id).ToArray()));
        var pass = await p.Get<PassportDto>($"/api/cards/{cards[0].Id}/passport");
        Assert.True(pass.IsOwner);
        Assert.Equal(cards[0].Serial, pass.Serial);
        Assert.Equal("pack", pass.Origin);
        Assert.Equal("PS-1", pass.IssuedServer);
        Assert.Equal(2, pass.Values.Count);
        Assert.All(pass.Values, v => Assert.True(v.Initial > 0));
        // DB enforces uniqueness and format
        var dup = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => f.Sql("UPDATE card_instances SET serial = @p0 WHERE id = @p1", cards[0].Serial, cards[1].Id));
        Assert.Equal("23505", dup.SqlState);
        var bad = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => f.Sql("UPDATE card_instances SET serial = 'abc' WHERE id = @p0", cards[1].Id));
        Assert.Equal("23514", bad.SqlState);
    }

    [Fact]
    public async Task Serial_survives_evolution_and_public_passport_hides_private_fields()
    {
        var p = await f.NewPlayer();
        var cards = await X.Open(p, 2);
        CardDto? card = null;
        foreach (var c in cards) if ((await p.Get<EvolutionPreviewDto>($"/api/cards/{c.Id}/evolution-preview")).Next != null) { card = c; break; }
        Assert.NotNull(card);
        var mid = await f.Scalar<long>("SELECT id FROM server_memberships WHERE account_id=@p0", p.Uid);
        await f.Sql("UPDATE card_instances SET level = 15 WHERE id = @p0", card!.Id);
        await f.Sql("INSERT INTO inventory_items (membership_id, item_id, quantity) VALUES (@p0, @p1, 5) ON CONFLICT (membership_id, item_id) DO UPDATE SET quantity = 5", mid, "essence_" + card.Element);
        await Player.Ok(await p.Post($"/api/cards/{card.Id}/evolve", null, Player.Key()));
        var after = await p.Get<CardDto>($"/api/cards/{card.Id}");
        Assert.NotEqual(card.FormId, after.FormId);
        Assert.Equal(card.Serial, after.Serial);

        var other = await f.NewPlayer();
        var pub = await other.Get<PassportDto>($"/api/passport/{card.Serial}");
        Assert.False(pub.IsOwner);
        Assert.Null(pub.CardId);
        Assert.Null(pub.OriginRef);
        Assert.Equal(0, pub.Card.Id);
        Assert.Single(pub.Evolutions);
        Assert.NotEmpty(pub.OwnerNickname);
        // serial alone grants nothing: there is no endpoint that transfers by serial, and another server cannot see it
        Assert.Equal(HttpStatusCode.BadRequest, (await other.Http.GetAsync("/api/passport/not-a-serial")).StatusCode);
    }
}

[Collection("api")]
public class MarketplaceTests(ApiFactory f)
{
    private async Task<(Player owner, Player seller, Player buyer, CardDto card)> Setup()
    {
        var owner = await X.Owner(f);
        var seller = await f.NewPlayer("seller");
        var buyer = await f.NewPlayer("buyer");
        var card = (await X.Open(seller))[0];
        await X.Fund(owner, buyer, X.Stars, 1000);
        return (owner, seller, buyer, card);
    }

    private static Task<JsonElement> List(Player p, long cardId, string currency, long price) =>
        X.J(p.Post("/api/market/listings", new CreateListingRequest { CardId = cardId, Currency = currency, Price = price, ConfirmPriceWarning = true }, Player.Key()).Result);

    [Fact]
    public async Task Listing_locks_card_and_purchase_settles_atomically_with_fee()
    {
        var (_, seller, buyer, card) = await Setup();
        var listing = await List(seller, card.Id, X.Stars, 200);
        long lid = listing.GetProperty("listingId").GetInt64();
        Assert.Equal(6, listing.GetProperty("fee").GetInt64());          // 3%
        Assert.Equal(194, listing.GetProperty("sellerNet").GetInt64());
        // locked: cannot list twice, cannot evolve/train while listed
        Assert.Equal("card_busy", await Player.Code(await seller.Post("/api/market/listings", new CreateListingRequest { CardId = card.Id, Currency = X.Beans, Price = 50, ConfirmPriceWarning = true }, Player.Key())));
        Assert.Equal(HttpStatusCode.Conflict, (await seller.Post($"/api/cards/{card.Id}/evolve", null, Player.Key())).StatusCode);
        Assert.Equal("cannot_buy_own_listing", await Player.Code(await seller.Post("/api/market/buy", new BuyRequest { ListingId = lid }, Player.Key())));

        var sellerBefore = X.Bal(await seller.Me(), X.Stars);
        var buy = await X.J(await buyer.Post("/api/market/buy", new BuyRequest { ListingId = lid, ExpectedPrice = 200 }, Player.Key()));
        Assert.Equal(card.Serial, buy.GetProperty("card").GetProperty("serial").GetString());
        Assert.Equal(800, X.Bal(await buyer.Me(), X.Stars));
        Assert.Equal(sellerBefore + 194, X.Bal(await seller.Me(), X.Stars));
        Assert.Equal(6, await f.Scalar<long>("SELECT amount FROM economy_system_ledger WHERE ref_id = @p0", "listing:" + lid));
        // ownership moved, serial unchanged, seller lost access, single owner
        var bought = await buyer.Get<CardPageDto>("/api/cards");
        Assert.Contains(bought.Items, c => c.Serial == card.Serial && !c.IsBusy);
        Assert.Equal(HttpStatusCode.NotFound, (await seller.Http.GetAsync($"/api/cards/{card.Id}")).StatusCode);
        Assert.Equal(1, await f.Scalar<long>("SELECT count(*) FROM card_instances WHERE serial = @p0", card.Serial));
        // notifications for both sides, transaction history
        Assert.Contains("market_sold", (await X.GetJ(seller, "/api/notifications")).GetProperty("items").EnumerateArray().Select(n => n.GetProperty("type").GetString()));
        Assert.Single((await X.GetJ(buyer, "/api/market/history?type=purchases")).EnumerateArray());
        Assert.Single((await X.GetJ(seller, "/api/market/history?type=sales")).EnumerateArray());
        // already sold
        Assert.Equal("listing_not_active", await Player.Code(await buyer.Post("/api/market/buy", new BuyRequest { ListingId = lid }, Player.Key())));
    }

    [Fact]
    public async Task Concurrent_buyers_only_one_wins_and_nobody_loses_money()
    {
        var owner = await X.Owner(f);
        var seller = await f.NewPlayer("seller");
        var card = (await X.Open(seller))[0];
        var buyers = new List<Player>();
        for (int i = 0; i < 8; i++) { var b = await f.NewPlayer("b"); await X.Fund(owner, b, X.Beans, 1000); buyers.Add(b); }
        long lid = (await List(seller, card.Id, X.Beans, 300)).GetProperty("listingId").GetInt64();
        var res = await Task.WhenAll(buyers.Select(b => b.Post("/api/market/buy", new BuyRequest { ListingId = lid }, Player.Key())));
        Assert.Equal(1, res.Count(r => r.IsSuccessStatusCode));
        var balances = await Task.WhenAll(buyers.Select(b => b.Me()));
        Assert.Equal(7, balances.Count(b => X.Bal(b, X.Beans) == 1500));   // losers untouched
        Assert.Equal(1, await f.Scalar<long>("SELECT count(*) FROM economy_ledger WHERE reason = 'market_buy' AND ref_id = @p0 AND delta = -300", "listing:" + lid));
        Assert.Equal(1, await f.Scalar<long>("SELECT count(*) FROM market_transactions WHERE ref_id = @p0 AND kind = 'listing'", lid));
    }

    [Fact]
    public async Task Same_buyer_retry_is_idempotent_and_insufficient_funds_changes_nothing()
    {
        var (owner, seller, buyer, card) = await Setup();
        long lid = (await List(seller, card.Id, X.Stars, 300)).GetProperty("listingId").GetInt64();
        var poor = await f.NewPlayer("poor");
        Assert.Equal("insufficient_rainbow_star", await Player.Code(await poor.Post("/api/market/buy", new BuyRequest { ListingId = lid }, Player.Key())));
        Assert.Equal("active", (await X.GetJ(poor, $"/api/market/listings/{lid}")).GetProperty("listing").GetProperty("status").GetString());
        var key = Player.Key();
        await Player.Ok(await buyer.Post("/api/market/buy", new BuyRequest { ListingId = lid }, key));
        var replay = await buyer.Post("/api/market/buy", new BuyRequest { ListingId = lid }, key);
        await Player.Ok(replay);
        Assert.Equal(700, X.Bal(await buyer.Me(), X.Stars));   // charged once
    }

    [Fact]
    public async Task Cannot_list_foreign_or_locked_cards_and_cancel_releases()
    {
        var (_, seller, buyer, card) = await Setup();
        Assert.Equal(HttpStatusCode.NotFound, (await buyer.Post("/api/market/listings", new CreateListingRequest { CardId = card.Id, Currency = X.Beans, Price = 10, ConfirmPriceWarning = true }, Player.Key())).StatusCode);
        await Player.Ok(await seller.Post($"/api/cards/{card.Id}/lock", new FlagRequest { Value = true }));
        Assert.Equal("card_locked", await Player.Code(await seller.Post("/api/market/listings", new CreateListingRequest { CardId = card.Id, Currency = X.Beans, Price = 10, ConfirmPriceWarning = true }, Player.Key())));
        await Player.Ok(await seller.Post($"/api/cards/{card.Id}/lock", new FlagRequest { Value = false }));
        long lid = (await List(seller, card.Id, X.Beans, 40)).GetProperty("listingId").GetInt64();
        await Player.Ok(await seller.Post($"/api/market/listings/{lid}/cancel", null));
        Assert.False((await seller.Get<CardDto>($"/api/cards/{card.Id}")).IsBusy);
        Assert.Equal("price_out_of_range", await Player.Code(await seller.Post("/api/market/listings", new CreateListingRequest { CardId = card.Id, Currency = X.Beans, Price = 0 }, Player.Key())));
        Assert.Equal("unknown_currency", await Player.Code(await seller.Post("/api/market/listings", new CreateListingRequest { CardId = card.Id, Currency = "USD", Price = 10 }, Player.Key())));
    }

    [Fact]
    public async Task Expired_listing_is_released_and_seller_notified()
    {
        var (_, seller, _, card) = await Setup();
        long lid = (await List(seller, card.Id, X.Beans, 40)).GetProperty("listingId").GetInt64();
        await f.Sql("UPDATE market_listings SET expires_at = now() - interval '1 minute' WHERE id = @p0", lid);
        await MarketMaintenanceService.RunOnce(f.Services);
        Assert.False((await seller.Get<CardDto>($"/api/cards/{card.Id}")).IsBusy);
        Assert.Equal("expired", await f.Scalar<string>("SELECT status FROM market_listings WHERE id = @p0", lid));
        Assert.Contains("listing_expired", (await X.GetJ(seller, "/api/notifications")).GetProperty("items").EnumerateArray().Select(n => n.GetProperty("type").GetString()));
    }

    [Fact]
    public async Task Listings_are_not_price_evidence_and_wash_trades_are_excluded()
    {
        var (owner, seller, buyer, card) = await Setup();
        await X.Fund(owner, seller, X.Stars, 1000);
        string Path(string currency) => $"/api/valuation/history?formId={card.FormId}&rarity={card.Rarity}&currency={currency}";
        long lid = (await List(seller, card.Id, X.Stars, 999)).GetProperty("listingId").GetInt64();
        Assert.Empty((await X.GetJ(buyer, Path(X.Stars))).GetProperty("trades").EnumerateArray());   // listing price never shows as a trade
        await Player.Ok(await buyer.Post("/api/market/buy", new BuyRequest { ListingId = lid }, Player.Key()));
        // buyer sells the same card straight back to the original seller: round trip => suspicious, excluded
        var back = (await buyer.Get<CardPageDto>("/api/cards")).Items.First(c => c.Serial == card.Serial);
        long lid2 = (await List(buyer, back.Id, X.Stars, 10)).GetProperty("listingId").GetInt64();
        await Player.Ok(await seller.Post("/api/market/buy", new BuyRequest { ListingId = lid2 }, Player.Key()));
        Assert.True(await f.Scalar<bool>("SELECT suspicious FROM market_transactions WHERE kind='listing' AND ref_id=@p0", lid2));
        Assert.True(await f.Scalar<bool>("SELECT excluded FROM market_price_history h JOIN market_transactions t ON t.id = h.transaction_id WHERE t.ref_id=@p0 AND t.kind='listing'", lid2));
        var trades = (await X.GetJ(buyer, Path(X.Stars))).GetProperty("trades").EnumerateArray().Select(t => t.GetProperty("price").GetInt64()).ToList();
        Assert.DoesNotContain(10L, trades);
        Assert.True(await f.Scalar<long>("SELECT count(*) FROM economy_audit_events WHERE type='suspicious_trade'") >= 1);
    }

    [Fact]
    public async Task Wishlist_match_notifies_once()
    {
        var (_, seller, buyer, card) = await Setup();
        await Player.Ok(await buyer.Post("/api/market/wishlist", new WishlistRequest { FormId = card.FormId, Currency = X.Beans, MaxPrice = 500 }));
        await List(seller, card.Id, X.Beans, 100);
        var n = (await X.GetJ(buyer, "/api/notifications")).GetProperty("items").EnumerateArray().Count(x => x.GetProperty("type").GetString() == "wishlist_match");
        Assert.Equal(1, n);
        var wl = await X.GetJ(buyer, "/api/market/listings?section=wishlist");
        Assert.True(wl.GetProperty("total").GetInt32() >= 1);
    }

    [Fact]
    public async Task Listings_are_invisible_and_unbuyable_from_another_server()
    {
        var (owner, seller, buyer, card) = await Setup();
        int sid = 5000 + Random.Shared.Next(1000);
        await Player.Ok(await owner.Post("/api/admin/servers", new AdminServerRequest { Id = sid, Code = $"PS-{sid}", Status = "online", RegistrationOpen = true }));
        long lid = (await List(seller, card.Id, X.Beans, 30)).GetProperty("listingId").GetInt64();
        await buyer.Enter(sid);
        Assert.Equal(HttpStatusCode.NotFound, (await buyer.Http.GetAsync($"/api/market/listings/{lid}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await buyer.Post("/api/market/buy", new BuyRequest { ListingId = lid }, Player.Key())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await buyer.Http.GetAsync($"/api/passport/{card.Serial}")).StatusCode);
    }
}

[Collection("api")]
public class AuctionTests(ApiFactory f)
{
    private Task<long> LedgerSum(Player p, string reason) =>
        f.Scalar<long>("SELECT coalesce(sum(l.delta),0) FROM economy_ledger l JOIN server_memberships m ON m.id = l.membership_id WHERE m.account_id = @p0 AND l.reason = @p1", p.Uid, reason);

    [Fact]
    public async Task Bids_hold_funds_outbid_refunds_and_settlement_happens_once()
    {
        var owner = await X.Owner(f);
        var seller = await f.NewPlayer("s"); var b1 = await f.NewPlayer("b1"); var b2 = await f.NewPlayer("b2");
        await X.Fund(owner, b1, X.Beans, 1000); await X.Fund(owner, b2, X.Beans, 1000);
        var card = (await X.Open(seller))[0];
        var a = await X.J(await seller.Post("/api/auctions", new CreateAuctionRequest { CardId = card.Id, Currency = X.Beans, StartPrice = 100, Hours = 1 }, Player.Key()));
        long aid = a.GetProperty("auctionId").GetInt64();
        Assert.Equal("self_bid_not_allowed", await Player.Code(await seller.Post("/api/auctions/bid", new BidRequest { AuctionId = aid, Amount = 200 }, Player.Key())));
        Assert.Equal("bid_too_low", await Player.Code(await b1.Post("/api/auctions/bid", new BidRequest { AuctionId = aid, Amount = 50 }, Player.Key())));
        Assert.Equal("insufficient_mini_green_bean", await Player.Code(await b1.Post("/api/auctions/bid", new BidRequest { AuctionId = aid, Amount = 99999 }, Player.Key())));
        await Player.Ok(await b1.Post("/api/auctions/bid", new BidRequest { AuctionId = aid, Amount = 300 }, Player.Key()));
        Assert.Equal(1500 - 300, X.Bal(await b1.Me(), X.Beans));          // held
        Assert.Equal("bid_too_low", await Player.Code(await b2.Post("/api/auctions/bid", new BidRequest { AuctionId = aid, Amount = 305 }, Player.Key()))); // min +5%
        await Player.Ok(await b2.Post("/api/auctions/bid", new BidRequest { AuctionId = aid, Amount = 400 }, Player.Key()));
        Assert.Equal(1500, X.Bal(await b1.Me(), X.Beans));                // refunded
        Assert.Equal(1100, X.Bal(await b2.Me(), X.Beans));
        Assert.Equal("card_busy", await Player.Code(await seller.Post("/api/market/listings", new CreateListingRequest { CardId = card.Id, Currency = X.Beans, Price = 10, ConfirmPriceWarning = true }, Player.Key())));

        await f.Sql("UPDATE auction_listings SET ends_at = now() - interval '1 second' WHERE id = @p0", aid);
        await MarketMaintenanceService.RunOnce(f.Services);
        await MarketMaintenanceService.RunOnce(f.Services);   // second pass must not settle again
        Assert.Equal(1, await f.Scalar<long>("SELECT count(*) FROM auction_settlements WHERE auction_id = @p0", aid));
        Assert.Equal(1, await f.Scalar<long>("SELECT count(*) FROM market_transactions WHERE kind='auction' AND ref_id = @p0", aid));
        Assert.Contains((await b2.Get<CardPageDto>("/api/cards")).Items, c => c.Serial == card.Serial);
        // captured, not double charged: b2 paid exactly one hold of 400; seller received exactly 400 - 3% fee
        Assert.Equal(-400, await LedgerSum(b2, "auction_hold") + await LedgerSum(b2, "auction_release"));
        Assert.Equal(388, await LedgerSum(seller, "auction_sale"));
        Assert.Equal(12, await f.Scalar<long>("SELECT amount FROM economy_system_ledger WHERE ref_id = @p0", "auction:" + aid));
        Assert.Equal(0, await f.Scalar<long>("SELECT count(*) FROM reserved_balances r JOIN auction_bids b ON 'bid:' || b.id = r.ref_id WHERE b.auction_id = @p0 AND r.status = 'held'", aid));
    }

    [Fact]
    public async Task No_bid_auction_returns_card_and_concurrent_bids_stay_consistent()
    {
        var owner = await X.Owner(f);
        var seller = await f.NewPlayer("s");
        var cards = await X.Open(seller);
        long a1 = (await X.J(await seller.Post("/api/auctions", new CreateAuctionRequest { CardId = cards[0].Id, Currency = X.Stars, StartPrice = 5, Hours = 1 }, Player.Key()))).GetProperty("auctionId").GetInt64();
        await f.Sql("UPDATE auction_listings SET ends_at = now() - interval '1 second' WHERE id = @p0", a1);
        await MarketMaintenanceService.RunOnce(f.Services);
        Assert.False((await seller.Get<CardDto>($"/api/cards/{cards[0].Id}")).IsBusy);

        long a2 = (await X.J(await seller.Post("/api/auctions", new CreateAuctionRequest { CardId = cards[1].Id, Currency = X.Stars, StartPrice = 10, Hours = 1 }, Player.Key()))).GetProperty("auctionId").GetInt64();
        var bidders = new List<Player>();
        for (int i = 0; i < 8; i++) { var b = await f.NewPlayer("cb"); await X.Fund(owner, b, X.Stars, 10_000); bidders.Add(b); }
        await Task.WhenAll(bidders.Select((b, i) => b.Post("/api/auctions/bid", new BidRequest { AuctionId = a2, Amount = 100 + i * 50 }, Player.Key())));
        // exactly one held reservation (the leader); everyone else whole again
        Assert.Equal(1, await f.Scalar<long>("SELECT count(*) FROM reserved_balances r JOIN auction_bids b ON 'bid:' || b.id = r.ref_id WHERE b.auction_id = @p0 AND r.status = 'held'", a2));
        var lead = await f.Scalar<long>("SELECT highest_amount FROM auction_listings WHERE id = @p0", a2);
        var bal = await Task.WhenAll(bidders.Select(b => b.Me()));
        Assert.Equal(1, bal.Count(b => X.Bal(b, X.Stars) == 10_000 - lead));
        Assert.Equal(7, bal.Count(b => X.Bal(b, X.Stars) == 10_000));
        Assert.Equal(0, await f.Scalar<long>("SELECT count(*) FROM economy_wallets w WHERE w.balance <> coalesce((SELECT sum(delta) FROM economy_ledger l WHERE l.membership_id = w.membership_id AND l.currency = w.currency), 0)"));
    }
}

[Collection("api")]
public class FeedAndSocialTests(ApiFactory f)
{
    [Fact]
    public async Task Card_post_shows_current_owner_after_sale()
    {
        var owner = await X.Owner(f);
        var a = await f.NewPlayer("a"); var b = await f.NewPlayer("b");
        await X.Fund(owner, b, X.Beans, 1000);
        var card = (await X.Open(a))[0];
        var post = await X.J(await a.Post("/api/feed/posts", new CreatePostRequest { Type = "card", Caption = "My new pet!", CardIds = new() { card.Id } }));
        long pid = post.GetProperty("id").GetInt64();
        Assert.Equal(card.Serial, post.GetProperty("cards")[0].GetProperty("serial").GetString());
        long lid = (await X.J(await a.Post("/api/market/listings", new CreateListingRequest { CardId = card.Id, Currency = X.Beans, Price = 50, ConfirmPriceWarning = true }, Player.Key()))).GetProperty("listingId").GetInt64();
        await Player.Ok(await b.Post("/api/market/buy", new BuyRequest { ListingId = lid }, Player.Key()));
        var view = (await X.GetJ(b, $"/api/feed/posts/{pid}")).GetProperty("post");
        var c0 = view.GetProperty("cards")[0];
        Assert.True(c0.GetProperty("ownerChanged").GetBoolean());
        Assert.Equal(b.Uid, c0.GetProperty("currentOwnerUid").GetInt64());
        Assert.Equal(a.Uid, view.GetProperty("authorUid").GetInt64());
    }

    [Fact]
    public async Task Likes_comments_block_report_and_moderation()
    {
        var a = await f.NewPlayer("a"); var b = await f.NewPlayer("b"); var mod = await f.NewPlayer("mod");
        await f.Sql("INSERT INTO account_roles (account_id, role, granted_at) VALUES (@p0, 'moderator', now())", mod.Uid);
        await mod.Enter(1);
        long pid = (await X.J(await a.Post("/api/feed/posts", new CreatePostRequest { Type = "text", Caption = "Hello Pet Star " + Guid.NewGuid() }))).GetProperty("id").GetInt64();
        await Player.Ok(await b.Post($"/api/feed/posts/{pid}/like", null));
        await Player.Ok(await b.Post($"/api/feed/posts/{pid}/like", null));      // idempotent
        await Player.Ok(await b.Post($"/api/feed/posts/{pid}/comments", new CommentRequest { Text = "So cute!" }));
        var v = (await X.GetJ(a, $"/api/feed/posts/{pid}")).GetProperty("post");
        Assert.Equal(1, v.GetProperty("likes").GetInt32());
        Assert.Equal(1, v.GetProperty("comments").GetInt32());
        Assert.Equal(1, (await X.GetJ(a, "/api/notifications")).GetProperty("items").EnumerateArray().Count(n => n.GetProperty("type").GetString() == "post_liked"));
        Assert.Equal("text_links_not_allowed", await Player.Code(await b.Post($"/api/feed/posts/{pid}/comments", new CommentRequest { Text = "buy cheap stars at www.scam.com" })));
        // report + moderator hides
        await Player.Ok(await b.Post("/api/reports", new ReportRequest { TargetType = "post", TargetId = pid, Reason = "spam" }));
        Assert.Equal("already_reported", await Player.Code(await b.Post("/api/reports", new ReportRequest { TargetType = "post", TargetId = pid, Reason = "spam" })));
        Assert.Equal(HttpStatusCode.Forbidden, (await b.Http.GetAsync("/api/admin/reports")).StatusCode);
        var reports = await X.GetJ(mod, "/api/admin/reports");
        var rid = reports.EnumerateArray().First(r => r.GetProperty("targetId").GetInt64() == pid).GetProperty("id").GetInt64();
        await Player.Ok(await mod.Post($"/api/admin/reports/{rid}/resolve", new ResolveReportRequest { Action = "hide", Reason = "spam" }));
        Assert.Equal(HttpStatusCode.NotFound, (await b.Http.GetAsync($"/api/feed/posts/{pid}")).StatusCode);
        // moderator cannot create currency
        Assert.Equal(HttpStatusCode.Forbidden, (await mod.Post("/api/admin/economy/adjust", new AdjustmentRequest { Uid = mod.Uid, ServerId = 1, Currency = X.Stars, Delta = 1000, Reason = "trying to mint" })).StatusCode);
        // block hides content both ways
        long pid2 = (await X.J(await a.Post("/api/feed/posts", new CreatePostRequest { Type = "text", Caption = "second " + Guid.NewGuid() }))).GetProperty("id").GetInt64();
        await Player.Ok(await b.Post($"/api/players/{a.Uid}/block", null));
        var feed = (await X.GetJ(b, "/api/feed?tab=global")).GetProperty("items").EnumerateArray().Select(p => p.GetProperty("id").GetInt64());
        Assert.DoesNotContain(pid2, feed);
        Assert.Equal(HttpStatusCode.NotFound, (await a.Http.GetAsync($"/api/players/{b.Uid}")).StatusCode);
    }

    [Fact]
    public async Task Follow_friends_and_profiles_expose_no_private_data()
    {
        var a = await f.NewPlayer("a"); var b = await f.NewPlayer("b");
        await Player.Ok(await a.Post($"/api/players/{b.Uid}/follow", null));
        var prof = await X.GetJ(a, $"/api/players/{b.Uid}");
        Assert.True(prof.GetProperty("isFollowing").GetBoolean());
        var raw = prof.GetRawText();
        Assert.DoesNotContain("@test.local", raw);
        Assert.DoesNotContain("email", raw, StringComparison.OrdinalIgnoreCase);
        var req = await X.J(await a.Post("/api/friends/requests", new FriendRequestBody { Uid = b.Uid }));
        await Player.Ok(await b.Post($"/api/friends/requests/{req.GetProperty("requestId").GetInt64()}/accept", null));
        Assert.Contains((await X.GetJ(a, "/api/friends")).EnumerateArray(), x => x.GetProperty("status").GetString() == "friends");
        // a player cannot read someone else's cards, passport internals, ledger or notifications
        var bCard = (await X.Open(b))[0];
        Assert.Equal(HttpStatusCode.NotFound, (await a.Http.GetAsync($"/api/cards/{bCard.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await a.Http.GetAsync($"/api/cards/{bCard.Id}/passport")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await a.Http.GetAsync($"/api/admin/ledger?uid={b.Uid}&serverId=1")).StatusCode);
    }
}

[Collection("api")]
public class ProgressionAndRewardsTests(ApiFactory f)
{
    [Fact]
    public async Task First_pack_achievement_rewards_once_and_mail_claims_once()
    {
        var owner = await X.Owner(f);
        var p = await f.NewPlayer();
        await X.Open(p);
        await X.Open(p);
        var ach = await X.GetJ(p, "/api/achievements");
        Assert.True(ach.EnumerateArray().First(a => a.GetProperty("id").GetString() == "first_pack").GetProperty("unlocked").GetBoolean());
        Assert.Equal(1, await f.Scalar<long>("SELECT count(*) FROM economy_ledger l JOIN server_memberships m ON m.id = l.membership_id WHERE m.account_id = @p0 AND l.ref_id = 'achievement:first_pack'", p.Uid));
        await Player.Ok(await owner.Post("/api/admin/mail", new MailRequest { Uid = p.Uid, ServerId = 1, Body = "Event gift", Currencies = new() { [X.Stars] = 7 } }));
        long mailId = (await X.GetJ(p, "/api/mailbox")).EnumerateArray().First().GetProperty("id").GetInt64();
        var before = X.Bal(await p.Me(), X.Stars);
        var claims = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => p.Post($"/api/mailbox/{mailId}/claim", null)));
        Assert.Equal(1, claims.Count(r => r.IsSuccessStatusCode));
        Assert.Equal(before + 7, X.Bal(await p.Me(), X.Stars));
    }

    [Fact]
    public async Task Skill_training_is_capped_and_consumes_materials_once()
    {
        var owner = await X.Owner(f);
        var p = await f.NewPlayer();
        var card = (await X.Open(p))[0];
        await X.Fund(owner, p, X.Beans, 10_000);
        var mid = await f.Scalar<long>("SELECT id FROM server_memberships WHERE account_id=@p0", p.Uid);
        Assert.Equal("training_level_too_low", await Player.Code(await p.Post($"/api/cards/{card.Id}/train", null, Player.Key())));
        await f.Sql("UPDATE card_instances SET level = 50 WHERE id = @p0", card.Id);
        await f.Sql("INSERT INTO inventory_items (membership_id, item_id, quantity) VALUES (@p0, 'training_scroll', 100)", mid);
        for (int i = 0; i < 4; i++) await Player.Ok(await p.Post($"/api/cards/{card.Id}/train", null, Player.Key()));
        // card system expansion: 5 → 6 now also needs a breakthrough (skill levels go up to 10)
        Assert.Equal("training_breakthrough_too_low", await Player.Code(await p.Post($"/api/cards/{card.Id}/train", null, Player.Key())));
        Assert.Equal(5, (await p.Get<CardDto>($"/api/cards/{card.Id}")).SkillLevel);
        Assert.Equal(100 - 15, (await p.Me()).Items["training_scroll"]);
    }

    [Fact]
    public async Task Ranked_match_updates_rating_and_leaderboard()
    {
        var a = await f.NewPlayer("ra"); var b = await f.NewPlayer("rb");
        foreach (var pl in new[] { a, b })
        {
            var cards = await X.Open(pl, 3);
            var pick = new List<CardDto>();
            foreach (var c in cards.OrderByDescending(c => c.Power)) if (pick.Count < 5 && pick.Count(x => x.FamilyId == c.FamilyId) < 2) pick.Add(c);
            await Player.Ok(await pl.Post("/api/decks", new SaveDeckRequest { Name = "Main", IsDefault = true, Slots = pick.Select((c, i) => new DeckSlotDto { Position = i, CardId = c.Id }).ToList() }));
        }
        var battle = await a.PostOk<BattleDto>("/api/ranked/match", new { }, Player.Key());
        Assert.Equal("ranked", battle.Mode);
        var cur = battle; int guard = 0;
        while (cur.Status == "active" && guard++ < 25) cur = await a.PostOk<BattleDto>($"/api/battles/{battle.Id}/rounds", new SubmitRoundRequest { Round = cur.Round });
        Assert.Equal("finished", cur.Status);
        var me = await X.GetJ(a, "/api/ranked");
        Assert.NotEqual(1000, me.GetProperty("rating").GetInt32() + (cur.WinnerSide == -1 ? 1 : 0));
        Assert.Contains((await X.GetJ(a, "/api/ranked/leaderboard")).GetProperty("entries").EnumerateArray(), e => e.GetProperty("uid").GetInt64() == a.Uid);
    }
}

[Collection("api")]
public class EconomyGuardTests(ApiFactory f)
{
    [Fact]
    public void No_real_money_withdrawal_or_exchange_endpoints_exist()
    {
        var routes = f.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().Select(e => e.RoutePattern.RawText ?? "").ToList();
        Assert.NotEmpty(routes);
        foreach (var bad in new[] { "withdraw", "cashout", "cash-out", "payout", "exchange", "convert", "usd", "vnd", "fiat", "redeem-money" })
            Assert.DoesNotContain(routes, r => r.Contains(bad, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Owner_adjustment_is_a_compensating_ledger_entry_and_ledger_reconciles()
    {
        var owner = await X.Owner(f);
        var p = await f.NewPlayer();
        await X.Fund(owner, p, X.Stars, 50);
        await X.Fund(owner, p, X.Stars, -20);
        Assert.Equal(30, X.Bal(await p.Me(), X.Stars));
        var mismatches = await f.Scalar<long>("SELECT count(*) FROM economy_wallets w WHERE w.balance <> coalesce((SELECT sum(delta) FROM economy_ledger l WHERE l.membership_id = w.membership_id AND l.currency = w.currency), 0)");
        Assert.Equal(0, mismatches);
        Assert.Equal(HttpStatusCode.Forbidden, (await p.Post("/api/admin/economy/adjust", new AdjustmentRequest { Uid = p.Uid, ServerId = 1, Currency = X.Stars, Delta = 999, Reason = "self mint attempt" })).StatusCode);
    }

    [Fact]
    public async Task Ownership_audit_is_clean()
    {
        var owner = await X.Owner(f);
        var audit = await X.GetJ(owner, "/api/admin/cards/ownership-audit?serverId=1");
        Assert.True(audit.GetProperty("ok").GetBoolean(), audit.GetRawText());
    }
}
