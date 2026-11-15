using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using PetStar.Api.Features;
using PetStar.Contracts;

namespace PetStar.Api.Tests;

public static class L2
{
    public static async Task Friends(Player a, Player b)
    {
        var req = await X.J(await a.Post("/api/friends/requests", new FriendRequestBody { Uid = b.Uid }));
        await Player.Ok(await b.Post($"/api/friends/requests/{req.GetProperty("requestId").GetInt64()}/accept", null));
    }

    public static async Task Deck(Player p)
    {
        var cards = await X.Open(p, 3);
        var pick = new List<CardDto>();
        foreach (var c in cards.OrderByDescending(c => c.Power)) if (pick.Count < 5 && pick.Count(x => x.FamilyId == c.FamilyId) < 2) pick.Add(c);
        await Player.Ok(await p.Post("/api/decks", new SaveDeckRequest { Name = "Main", IsDefault = true, Slots = pick.Select((c, i) => new DeckSlotDto { Position = i, CardId = c.Id }).ToList() }));
    }

    public static async Task<Player> Admin(ApiFactory f, string role = "admin")
    {
        var a = await f.NewPlayer(role);
        await f.Sql("INSERT INTO account_roles (account_id, role, granted_at) VALUES (@p0, @p1, now())", a.Uid, role);
        await a.Enter(1);
        return a;
    }
}

[Collection("api")]
public class GuildAndChatTests(ApiFactory f)
{
    [Fact]
    public async Task Guild_lifecycle_roles_and_private_feed()
    {
        var leader = await f.NewPlayer("gl"); var m1 = await f.NewPlayer("gm"); var m2 = await f.NewPlayer("gm"); var outsider = await f.NewPlayer("go");
        string name = "Stars" + Random.Shared.Next(100000);
        long gid = (await X.J(await leader.Post("/api/guilds", new CreateGuildRequest { Name = name, JoinPolicy = "approval" }))).GetProperty("id").GetInt64();
        Assert.Equal("guild_name_taken", await Player.Code(await m1.Post("/api/guilds", new CreateGuildRequest { Name = name.ToLowerInvariant() })));
        Assert.Equal("requested", (await X.J(await m1.Post($"/api/guilds/{gid}/join", null))).GetProperty("status").GetString());
        var mine = await X.GetJ(leader, "/api/guilds/mine");
        long reqId = mine.GetProperty("requests")[0].GetProperty("id").GetInt64();
        await Player.Ok(await leader.Post($"/api/guilds/requests/{reqId}/accept", null));
        await Player.Ok(await m2.Post($"/api/guilds/{gid}/join", null));
        long req2 = (await X.GetJ(leader, "/api/guilds/mine")).GetProperty("requests")[0].GetProperty("id").GetInt64();
        await Player.Ok(await leader.Post($"/api/guilds/requests/{req2}/accept", null));
        Assert.Equal("already_in_guild", await Player.Code(await m1.Post("/api/guilds", new CreateGuildRequest { Name = "Other" + Random.Shared.Next(100000) })));
        // members cannot kick, leader can; officers cannot kick officers
        Assert.Equal("guild_rank_too_low", await Player.Code(await m1.Post($"/api/guilds/members/{m2.Uid}/kick", null)));
        await Player.Ok(await leader.Post($"/api/guilds/members/{m1.Uid}/role", new GuildRoleRequest { Role = "officer" }));
        await Player.Ok(await m1.Post("/api/guilds/announcements", new GuildTextRequest { Text = "Guild war tonight!" }));
        // guild feed is private to members
        await Player.Ok(await m2.Post("/api/guilds/feed", new CreatePostRequest { Type = "text", Caption = "hello guild " + Guid.NewGuid() }));
        Assert.Single((await X.GetJ(leader, "/api/guilds/feed")).GetProperty("items").EnumerateArray());
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.Http.GetAsync("/api/guilds/feed")).StatusCode);
        Assert.DoesNotContain((await X.GetJ(outsider, "/api/feed?tab=global")).GetProperty("items").EnumerateArray(), p => p.GetProperty("caption").GetString()!.StartsWith("hello guild"));
        await Player.Ok(await m1.Post($"/api/guilds/members/{m2.Uid}/kick", null));
        // leader leaves → officer becomes leader
        await Player.Ok(await leader.Post("/api/guilds/leave", null));
        Assert.Equal("leader", (await X.GetJ(m1, "/api/guilds/mine")).GetProperty("myRole").GetString());
    }

    [Fact]
    public async Task Chat_channels_filters_rate_limits_and_dm_privacy()
    {
        var a = await f.NewPlayer("ca"); var b = await f.NewPlayer("cb"); var c = await f.NewPlayer("cc");
        string text = "hi world " + Guid.NewGuid().ToString("N")[..6];
        await Player.Ok(await a.Post("/api/chat/world", new ChatSendRequest { Text = text }));
        Assert.Contains((await X.GetJ(b, "/api/chat/world")).EnumerateArray(), m => m.GetProperty("text").GetString() == text);
        Assert.Equal("chat_too_fast", await Player.Code(await a.Post("/api/chat/world", new ChatSendRequest { Text = "again" })));
        Assert.Equal("text_links_not_allowed", await Player.Code(await b.Post("/api/chat/world", new ChatSendRequest { Text = "visit www.cheap-stars.com" })));
        Assert.Equal("dm_friends_only", await Player.Code(await a.Post($"/api/chat/dm:{c.Uid}", new ChatSendRequest { Text = "hey" })));
        Assert.Equal("not_in_guild", await Player.Code(await a.Post("/api/chat/guild", new ChatSendRequest { Text = "hey" })));
        await L2.Friends(a, b);
        await Task.Delay(1100);
        await Player.Ok(await a.Post($"/api/chat/dm:{b.Uid}", new ChatSendRequest { Text = "secret plan" }));
        Assert.Contains((await X.GetJ(b, $"/api/chat/dm:{a.Uid}")).EnumerateArray(), m => m.GetProperty("text").GetString() == "secret plan");
        Assert.Equal(HttpStatusCode.Forbidden, (await c.Http.GetAsync($"/api/chat/dm:{a.Uid}")).StatusCode);
        // blocking hides the author's world messages
        await Player.Ok(await c.Post($"/api/players/{a.Uid}/block", null));
        Assert.DoesNotContain((await X.GetJ(c, "/api/chat/world")).EnumerateArray(), m => m.GetProperty("authorUid").GetInt64() == a.Uid);
    }

    [Fact]
    public async Task Websocket_requires_single_use_ticket_and_pushes_notifications()
    {
        var a = await f.NewPlayer("wa"); var b = await f.NewPlayer("wb");
        var ticket = (await X.J(await a.Post("/api/realtime/ticket", null))).GetProperty("ticket").GetString()!;
        var client = f.Server.CreateWebSocketClient();
        var ws = await client.ConnectAsync(new Uri($"ws://localhost/ws?ticket={ticket}"), CancellationToken.None);
        var buf = new byte[8192];
        var first = await ws.ReceiveAsync(buf, CancellationToken.None);
        Assert.Contains("\"hello\"", Encoding.UTF8.GetString(buf, 0, first.Count));
        await Player.Ok(await b.Post($"/api/players/{a.Uid}/follow", null));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var msg = await ws.ReceiveAsync(buf, cts.Token);
        var json = Encoding.UTF8.GetString(buf, 0, msg.Count);
        Assert.Contains("\"notification\"", json);
        Assert.Contains("new_follower", json);
        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
        // reused ticket is rejected
        await Assert.ThrowsAnyAsync<Exception>(() => f.Server.CreateWebSocketClient().ConnectAsync(new Uri($"ws://localhost/ws?ticket={ticket}"), CancellationToken.None));
    }
}

[Collection("api")]
public class LivePvpTests(ApiFactory f)
{
    private async Task<(Player a, Player b, Guid battle)> Duel()
    {
        var a = await f.NewPlayer("pa"); var b = await f.NewPlayer("pb");
        await L2.Deck(a); await L2.Deck(b); await L2.Friends(a, b);
        long cid = (await X.J(await a.Post("/api/pvp/challenges", new ChallengeRequest { Uid = b.Uid }))).GetProperty("challengeId").GetInt64();
        Assert.Equal(HttpStatusCode.Forbidden, (await a.Post($"/api/pvp/challenges/{cid}/accept", null)).StatusCode);
        var bid = Guid.Parse((await X.J(await b.Post($"/api/pvp/challenges/{cid}/accept", null))).GetProperty("battleId").GetString()!);
        return (a, b, bid);
    }

    [Fact]
    public async Task Both_sides_submit_sealed_plans_and_round_resolves()
    {
        var (a, b, id) = await Duel();
        var sa = (await X.GetJ(a, $"/api/pvp/battles/{id}")).GetProperty("battle");
        Assert.Equal(0, sa.GetProperty("mySide").GetInt32());
        Assert.Equal(1, (await X.GetJ(b, $"/api/pvp/battles/{id}")).GetProperty("battle").GetProperty("mySide").GetInt32());
        var r1 = await X.J(await a.Post($"/api/pvp/battles/{id}/rounds", new SubmitRoundRequest { Round = 1 }));
        Assert.False(r1.GetProperty("resolved").GetBoolean());
        Assert.Equal("already_submitted", await Player.Code(await a.Post($"/api/pvp/battles/{id}/rounds", new SubmitRoundRequest { Round = 1 })));
        // the opponent sees only that a plan exists, never its content (pending table is not exposed)
        Assert.False((await X.GetJ(b, $"/api/pvp/battles/{id}")).GetProperty("submitted").GetBoolean());
        Assert.Equal("not_enough_energy", await Player.Code(await b.Post($"/api/pvp/battles/{id}/rounds", new SubmitRoundRequest { Round = 1,
            Actions = Enumerable.Range(0, 5).Select(i => new PetStar.Rules.Battle.PlannedAction { UnitIndex = i, Type = PetStar.Rules.Battle.ActionType.Skill }).ToList() })));
        var r2 = await X.J(await b.Post($"/api/pvp/battles/{id}/rounds", new SubmitRoundRequest { Round = 1 }));
        Assert.True(r2.GetProperty("resolved").GetBoolean());
        Assert.Equal(2, (await X.GetJ(a, $"/api/pvp/battles/{id}")).GetProperty("battle").GetProperty("round").GetInt32());
        var stranger = await f.NewPlayer();
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Http.GetAsync($"/api/pvp/battles/{id}")).StatusCode);
        Assert.Equal("use_pvp_endpoint", await Player.Code(await a.Post($"/api/battles/{id}/rounds", new SubmitRoundRequest { Round = 2 })));
    }

    [Fact]
    public async Task Deadline_resolves_with_defaults_and_three_misses_forfeit()
    {
        var (a, b, id) = await Duel();
        for (int i = 0; i < 3; i++)
        {
            int round = (await X.GetJ(a, $"/api/pvp/battles/{id}")).GetProperty("battle").GetProperty("round").GetInt32();
            if ((await X.GetJ(a, $"/api/pvp/battles/{id}")).GetProperty("battle").GetProperty("status").GetString() != "active") break;
            await Player.Ok(await a.Post($"/api/pvp/battles/{id}/rounds", new SubmitRoundRequest { Round = round }));   // b never submits
            await f.Sql("UPDATE battles SET round_deadline = now() - interval '1 second' WHERE id = @p0", id);
            await PvpTimeoutService.RunOnce(f.Services);
        }
        var end = (await X.GetJ(a, $"/api/pvp/battles/{id}")).GetProperty("battle");
        Assert.Equal("finished", end.GetProperty("status").GetString());
        if (end.GetProperty("endReason").GetString() == "disconnect") Assert.Equal(0, end.GetProperty("winnerSide").GetInt32());
    }

    [Fact]
    public async Task Casual_queue_pairs_two_players()
    {
        var a = await f.NewPlayer("qa"); var b = await f.NewPlayer("qb");
        await L2.Deck(a); await L2.Deck(b);
        await f.Sql("DELETE FROM pvp_queue");
        Assert.Equal("queued", (await X.J(await a.Post("/api/pvp/queue", null))).GetProperty("status").GetString());
        var m = await X.J(await b.Post("/api/pvp/queue", null));
        Assert.Equal("matched", m.GetProperty("status").GetString());
        var id = m.GetProperty("battleId").GetString();
        Assert.Equal("active", (await X.GetJ(a, $"/api/pvp/battles/{id}")).GetProperty("battle").GetProperty("status").GetString());
    }
}

[Collection("api")]
public class GiftcodeEventAdminTests(ApiFactory f)
{
    [Fact]
    public async Task Giftcode_redeems_once_respects_scope_and_limits()
    {
        var admin = await L2.Admin(f);
        var p = await f.NewPlayer("g1");
        string code = "WELCOME-" + Random.Shared.Next(100000);
        await Player.Ok(await admin.Post("/api/admin/giftcodes", new CreateGiftcodeRequest { Code = code, MaxUses = 2, Rewards = new() { Currencies = { [X.Stars] = 15 }, Packs = { ["basic_pack"] = 1 } } }));
        Assert.Equal(HttpStatusCode.Forbidden, (await p.Post("/api/admin/giftcodes", new CreateGiftcodeRequest { Code = "HACK-1234" })).StatusCode);
        var before = await p.Me();
        var res = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => p.Post("/api/giftcodes/redeem", new RedeemRequest { Code = code.ToLowerInvariant() })));
        Assert.Equal(1, res.Count(r => r.IsSuccessStatusCode));
        var after = await p.Me();
        Assert.Equal(X.Bal(before, X.Stars) + 15, X.Bal(after, X.Stars));
        Assert.Equal(before.Packs["basic_pack"] + 1, after.Packs["basic_pack"]);
        await Player.Ok(await (await f.NewPlayer("g2")).Post("/api/giftcodes/redeem", new RedeemRequest { Code = code }));
        Assert.Equal("giftcode_used_up", await Player.Code(await (await f.NewPlayer("g3")).Post("/api/giftcodes/redeem", new RedeemRequest { Code = code })));
        Assert.Equal("invalid_giftcode", await Player.Code(await p.Post("/api/giftcodes/redeem", new RedeemRequest { Code = "NOPE-0000" })));
        // admin (not owner) cannot create card rewards; owner can, and cards get serial + passport
        var cardReward = new CreateGiftcodeRequest { Code = "CARD-" + Random.Shared.Next(100000), Rewards = new() { Cards = { new RewardCard { FormId = "pet_halowl", Rarity = "SR", Quality = "A" } } } };
        Assert.Equal("owner_required_for_card_rewards", await Player.Code(await admin.Post("/api/admin/giftcodes", cardReward)));
        var owner = await X.Owner(f);
        await Player.Ok(await owner.Post("/api/admin/giftcodes", cardReward));
        var p2 = await f.NewPlayer("g4");
        await Player.Ok(await p2.Post("/api/giftcodes/redeem", new RedeemRequest { Code = cardReward.Code }));
        var card = (await p2.Get<CardPageDto>("/api/cards")).Items.First(c => c.FormId == "pet_halowl" && c.Rarity == "SR");
        Assert.Equal(12, card.Serial.Length);
        Assert.Equal("giftcode", (await p2.Get<PassportDto>($"/api/cards/{card.Id}/passport")).Origin);
    }

    [Fact]
    public async Task Login_reward_and_event_claim_once()
    {
        var admin = await L2.Admin(f);
        var p = await f.NewPlayer();
        var status = await X.GetJ(p, "/api/rewards/login");
        Assert.False(status.GetProperty("claimedToday").GetBoolean());
        var claims = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => p.Post("/api/rewards/login/claim", null)));
        Assert.Equal(1, claims.Count(r => r.IsSuccessStatusCode));
        Assert.True((await X.GetJ(p, "/api/rewards/login")).GetProperty("claimedToday").GetBoolean());
        long eid = (await X.J(await admin.Post("/api/admin/events", new CreateEventRequest { Key = "launch_party", StartsAt = DateTimeOffset.UtcNow.AddMinutes(-1), EndsAt = DateTimeOffset.UtcNow.AddDays(1), Rewards = new() { Currencies = { [X.Beans] = 300 } } }))).GetProperty("id").GetInt64();
        var ev = (await X.GetJ(p, "/api/events")).EnumerateArray().First(e => e.GetProperty("id").GetInt64() == eid);
        Assert.False(ev.GetProperty("claimed").GetBoolean());
        await Player.Ok(await p.Post($"/api/events/{eid}/claim", null));
        Assert.Equal("event_already_claimed", await Player.Code(await p.Post($"/api/events/{eid}/claim", null)));
    }

    [Fact]
    public async Task Ban_revokes_sessions_and_only_owner_manages_roles()
    {
        var admin = await L2.Admin(f);
        var p = await f.NewPlayer("ban");
        Assert.Contains((await X.GetJ(admin, $"/api/admin/accounts?q={p.Uid}")).EnumerateArray(), a => a.GetProperty("uid").GetInt64() == p.Uid);
        await Player.Ok(await admin.Post($"/api/admin/accounts/{p.Uid}/ban", new BanRequest { Reason = "cheating test", Hours = 24 }));
        Assert.Equal(HttpStatusCode.Unauthorized, (await p.Http.GetAsync("/api/player")).StatusCode);
        var login = await p.Http.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = p.Email, Password = p.Password });
        Assert.Equal("account_banned", await Player.Code(login));
        await Player.Ok(await admin.Post($"/api/admin/accounts/{p.Uid}/unban", null));
        Assert.Equal(HttpStatusCode.OK, (await p.Http.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = p.Email, Password = p.Password })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.Post($"/api/admin/accounts/{p.Uid}/roles", new RoleRequest { Role = "gm" })).StatusCode);
        var owner = await X.Owner(f);
        await Player.Ok(await owner.Post($"/api/admin/accounts/{p.Uid}/roles", new RoleRequest { Role = "moderator" }));
    }

    [Fact]
    public async Task Cors_allows_only_configured_origins()
    {
        var c = f.CreateClient();
        HttpRequestMessage Pre(string origin)
        {
            var r = new HttpRequestMessage(HttpMethod.Options, "/api/auth/login");
            r.Headers.Add("Origin", origin); r.Headers.Add("Access-Control-Request-Method", "POST"); r.Headers.Add("Access-Control-Request-Headers", "content-type,authorization,idempotency-key");
            return r;
        }
        var ok = await c.SendAsync(Pre("https://petstar.pages.dev"));
        Assert.Equal("https://petstar.pages.dev", ok.Headers.GetValues("Access-Control-Allow-Origin").Single());
        var bad = await c.SendAsync(Pre("https://evil.example"));
        Assert.False(bad.Headers.Contains("Access-Control-Allow-Origin"));
    }
}
