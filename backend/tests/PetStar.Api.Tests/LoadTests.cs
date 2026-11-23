using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using PetStar.Api.Features;
using PetStar.Contracts;
using Xunit.Abstractions;

namespace PetStar.Api.Tests;

/// <summary>
/// Concurrent load scenario (Marketplace, Auction, Feed, Battle) with economy invariant checks.
/// Runs only when PETSTAR_LOAD=1 (e.g. `PETSTAR_LOAD=1 dotnet test --filter Category=Load`).
/// </summary>
[Collection("api")]
[Trait("Category", "Load")]
public class LoadTests(ApiFactory f, ITestOutputHelper output)
{
    [Fact]
    public async Task Concurrent_market_auction_feed_battle_load_keeps_invariants()
    {
        if (Environment.GetEnvironmentVariable("PETSTAR_LOAD") != "1") return;
        int users = int.TryParse(Environment.GetEnvironmentVariable("PETSTAR_LOAD_USERS"), out var u) ? u : 40;
        var owner = await X.Owner(f);
        var players = new List<Player>();
        for (int i = 0; i < users; i++) { var p = await f.NewPlayer("load"); await X.Fund(owner, p, X.Beans, 5000); await X.Fund(owner, p, X.Stars, 500); players.Add(p); }
        var lat = new System.Collections.Concurrent.ConcurrentDictionary<string, System.Collections.Concurrent.ConcurrentBag<double>>();
        async Task<HttpResponseMessage> Timed(string op, Func<Task<HttpResponseMessage>> fn)
        {
            var sw = Stopwatch.StartNew();
            var r = await fn();
            lat.GetOrAdd(op, _ => new System.Collections.Concurrent.ConcurrentBag<double>()).Add(sw.Elapsed.TotalMilliseconds);
            return r;
        }

        // 1) everyone opens packs concurrently
        var cards = await Task.WhenAll(players.Select(async p =>
        {
            var r = await Timed("pack_open", () => p.Post("/api/packs/open", new OpenPackRequest { PackTypeId = "basic_pack" }, Player.Key()));
            return (p, (await r.Content.ReadFromJsonAsync<PackOpeningDto>(Player.Json))!.Cards);
        }));
        // 2) half list a card, the other half race to buy random listings
        var sellers = cards.Take(users / 2).ToList();
        var listings = new List<long>();
        foreach (var (p, c) in sellers)
        {
            var r = await Timed("list", () => p.Post("/api/market/listings", new CreateListingRequest { CardId = c[0].Id, Currency = X.Beans, Price = 100, ConfirmPriceWarning = true }, Player.Key()));
            listings.Add((await X.J(r)).GetProperty("listingId").GetInt64());
        }
        var buyers = cards.Skip(users / 2).Select(x => x.p).ToList();
        var rnd = new Random(7);
        var buyResults = await Task.WhenAll(buyers.SelectMany(b => Enumerable.Range(0, 3).Select(_ => Timed("buy", () => b.Post("/api/market/buy", new BuyRequest { ListingId = listings[rnd.Next(listings.Count)] }, Player.Key())))));
        // 3) auctions: each seller auctions a second card, everyone bids concurrently
        var auctions = new List<long>();
        foreach (var (p, c) in sellers.Take(5))
            auctions.Add((await X.J(await Timed("auction_create", () => p.Post("/api/auctions", new CreateAuctionRequest { CardId = c[1].Id, Currency = X.Beans, StartPrice = 10, Hours = 1 }, Player.Key())))).GetProperty("auctionId").GetInt64());
        await Task.WhenAll(buyers.SelectMany(b => auctions.Select(a => Timed("bid", () => b.Post("/api/auctions/bid", new BidRequest { AuctionId = a, Amount = 10 + rnd.Next(1, 200) }, Player.Key())))));
        // 4) feed: posts, likes, comments concurrently
        var posts = await Task.WhenAll(players.Take(10).Select(async (p, i) => (await X.J(await Timed("post", () => p.Post("/api/feed/posts", new CreatePostRequest { Type = "text", Caption = $"load post {i} {Guid.NewGuid()}" })))).GetProperty("id").GetInt64()));
        await Task.WhenAll(players.SelectMany(p => posts.Select(pid => Timed("like", () => p.Post($"/api/feed/posts/{pid}/like", null)))));
        await Task.WhenAll(players.Take(20).Select((p, i) => Timed("feed_read", () => p.Http.GetAsync("/api/feed?tab=global"))));
        // 5) settle auctions
        await f.Sql("UPDATE auction_listings SET ends_at = now() - interval '1 second' WHERE id = ANY(@p0)", auctions.ToArray());
        await MarketMaintenanceService.RunOnce(f.Services);

        // 6) battles: 10 players play full PvE battles concurrently
        var fighters = new List<Player>();
        for (int i = 0; i < 10; i++)
        {
            var p = await f.NewPlayer("fight");
            var cs = await X.Open(p, 3);
            var pick = new List<CardDto>();
            foreach (var c in cs.OrderByDescending(c => c.Power)) if (pick.Count < 5 && pick.Count(x => x.FamilyId == c.FamilyId) < 2) pick.Add(c);
            await Player.Ok(await p.Post("/api/decks", new SaveDeckRequest { Name = "Main", IsDefault = true, Slots = pick.Select((c, k) => new DeckSlotDto { Position = k, CardId = c.Id }).ToList() }));
            fighters.Add(p);
        }
        var outcomes = await Task.WhenAll(fighters.Select(async p =>
        {
            var b = (await (await Timed("battle_start", () => p.Post("/api/battles/pve", new StartPveRequest { StageId = "c1_s1" }, Player.Key()))).Content.ReadFromJsonAsync<BattleDto>(Player.Json))!;
            int guard = 0;
            while (b.Status == "active" && guard++ < 25)
                b = (await (await Timed("battle_round", () => p.Post($"/api/battles/{b.Id}/rounds", new SubmitRoundRequest { Round = b.Round }))).Content.ReadFromJsonAsync<BattleDto>(Player.Json))!;
            return b;
        }));
        Assert.All(outcomes, b => Assert.Equal("finished", b.Status));

        // ---------------- invariants ----------------
        foreach (var lid in listings)
            Assert.True(await f.Scalar<long>("SELECT count(*) FROM market_transactions WHERE kind='listing' AND ref_id=@p0", lid) <= 1);
        Assert.Equal(await f.Scalar<long>("SELECT count(*) FROM market_listings WHERE id = ANY(@p0) AND status='sold'", listings.ToArray()),
                     buyResults.Count(r => r.IsSuccessStatusCode));
        Assert.Equal(0, await f.Scalar<long>("SELECT count(*) FROM economy_wallets w WHERE w.balance <> coalesce((SELECT sum(delta) FROM economy_ledger l WHERE l.membership_id = w.membership_id AND l.currency = w.currency), 0)"));
        Assert.Equal(0, await f.Scalar<long>("SELECT count(*) FROM reserved_balances r JOIN auction_bids b ON 'bid:' || b.id = r.ref_id WHERE b.auction_id = ANY(@p0) AND r.status = 'held'", auctions.ToArray()));
        Assert.Equal(0, await f.Scalar<long>("SELECT count(*) FROM (SELECT serial FROM card_instances GROUP BY serial HAVING count(*) > 1) d"));
        foreach (var pid in posts)
            Assert.Equal(await f.Scalar<long>("SELECT count(*) FROM feed_likes WHERE post_id=@p0", pid), await f.Scalar<long>("SELECT like_count FROM feed_posts WHERE id=@p0", pid));
        foreach (var (op, list) in lat.OrderBy(k => k.Key))
        {
            var s = list.OrderBy(x => x).ToList();
            output.WriteLine($"{op,-15} n={s.Count,4}  p50={s[s.Count / 2],7:0.0} ms  p95={s[(int)(s.Count * 0.95)],7:0.0} ms  max={s[^1],7:0.0} ms");
        }
    }
}
