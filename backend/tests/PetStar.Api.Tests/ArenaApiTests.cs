using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PetStar.Api.Features;
using PetStar.Api.Infrastructure;
using PetStar.Rules.Arena;

namespace PetStar.Api.Tests;

/// <summary>Pet Battle Arena through the real API + PostgreSQL: decks from owned cards, PvE, matchmaking (casual/ranked),
/// friend battles, private rooms, hidden information, idempotent actions, stale versions, timers, reconnect, replay,
/// history and rewards. Human sides are played by the rule-abiding AI from their own masked view.</summary>
[Collection("api")]
public class ArenaApiTests(ApiFactory f)
{
    private static readonly JsonSerializerOptions J = new(JsonSerializerDefaults.Web);
    private ArenaRules Rules => f.Services.GetRequiredService<ArenaRulesProvider>().Rules;

    public sealed class View { public Guid Id { get; set; } public string Mode { get; set; } = ""; public string Status { get; set; } = ""; public int You { get; set; } public int Version { get; set; } public ArenaState State { get; set; } = new(); public string? RoomCode { get; set; } public string? RewardsJson { get; set; } }

    private static async Task<View> V(HttpResponseMessage r) { await Player.Ok(r); return JsonSerializer.Deserialize<View>(await r.Content.ReadAsStringAsync(), J)!; }
    private static async Task<View> Get(Player p, Guid id) => await V(await p.Http.GetAsync($"/api/arena/matches/{id}"));

    private async Task<Player> Ready(string prefix = "a")
    {
        var p = await f.NewPlayer(prefix);
        for (int i = 0; i < 6; i++)
        {
            await X.Open(p, 2);
            var r = await p.Post("/api/arena/decks/auto", null);
            if (r.IsSuccessStatusCode)
            {
                var auto = JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;
                var cards = JsonSerializer.Deserialize<List<ArenaDeckEntry>>(auto.GetProperty("cards").GetRawText(), J)!;
                var energy = JsonSerializer.Deserialize<List<string>>(auto.GetProperty("energyTypes").GetRawText(), J)!;
                await Player.Ok(await p.Post("/api/arena/decks", new SaveArenaDeckRequest { Name = "Main", IsDefault = true, Cards = cards, EnergyTypes = energy }));
                return p;
            }
        }
        throw new InvalidOperationException("could not build an arena deck");
    }

    private static Task<HttpResponseMessage> Send(Player p, View v, ArenaAction a, string? actionId = null) =>
        p.Post($"/api/arena/matches/{v.Id}/actions", new ArenaActionRequest { ActionId = actionId ?? Guid.NewGuid().ToString("N"), Version = v.Version, Action = a });

    private async Task<View> PlayToEnd(Player p, View v, int maxSteps = 1500)
    {
        for (int i = 0; i < maxSteps && v.Status == "active"; i++)
        {
            if (!ArenaEngine.MustAct(v.State, v.You)) { v = await Get(p, v.Id); if (!ArenaEngine.MustAct(v.State, v.You)) return v; continue; }
            v = await V(await Send(p, v, ArenaAi.Decide(Rules, v.State, v.You)));
        }
        return v;
    }

    [Fact]
    public async Task Collection_starter_once_and_deck_rules_use_only_owned_free_cards()
    {
        var p = await f.NewPlayer("ad");
        var mine = await X.Open(p, 3);
        var c1 = await X.GetJ(p, "/api/arena/collection");
        var c2 = await X.GetJ(p, "/api/arena/collection");
        long Trainers(JsonElement c) => c.GetProperty("trainers").EnumerateObject().Sum(x => x.Value.GetInt64());
        Assert.Equal(32, Trainers(c1));                         // 16 original trainer cards × 2, granted once
        Assert.Equal(Trainers(c1), Trainers(c2));
        var basic = mine.FirstOrDefault(c => Rules.Has(c.FormId) && Rules.Def(c.FormId).IsBasic);
        Assert.NotNull(basic);
        List<ArenaDeckEntry> Deck(long inst, string def) => new()
        {
            new() { DefId = def, CardInstanceId = inst },
            new() { DefId = "su_scholar", Count = 2 }, new() { DefId = "su_medic", Count = 2 }, new() { DefId = "su_captain", Count = 2 }, new() { DefId = "su_guide", Count = 2 },
            new() { DefId = "su_smith", Count = 2 }, new() { DefId = "at_herb_salve", Count = 2 }, new() { DefId = "at_star_lantern", Count = 2 }, new() { DefId = "at_leaf_armor", Count = 2 },
            new() { DefId = "at_lucky_coin", Count = 2 }, new() { DefId = "su_strategist", Count = 1 },
        };
        await Player.Ok(await p.Post("/api/arena/decks", new SaveArenaDeckRequest { Name = "Mini", Cards = Deck(basic!.Id, basic.FormId) }));
        var other = await f.NewPlayer("ao");
        var stolen = (await X.Open(other, 1))[0];
        Assert.Equal("deck_card_not_owned", await Player.Code(await p.Post("/api/arena/decks", new SaveArenaDeckRequest { Name = "x", Cards = Deck(stolen.Id, stolen.FormId) })));
        var bad = Deck(basic.Id, basic.FormId); bad[^1].Count = 2;
        Assert.Equal("deck_must_have_20", await Player.Code(await p.Post("/api/arena/decks", new SaveArenaDeckRequest { Name = "x", Cards = bad })));
        bad = Deck(basic.Id, basic.FormId); bad[1].Count = 3; bad[^1].Count = 0; bad.RemoveAt(bad.Count - 1);
        Assert.Contains(await Player.Code(await p.Post("/api/arena/decks", new SaveArenaDeckRequest { Name = "x", Cards = bad })), new[] { "deck_trainer_not_owned", "deck_too_many_copies" });
        Assert.Equal("deck_energy_types", await Player.Code(await p.Post("/api/arena/decks", new SaveArenaDeckRequest { Name = "x", Cards = Deck(basic.Id, basic.FormId), EnergyTypes = new() { "fire", "water", "wind", "dark" } })));
        await f.Sql("UPDATE card_instances SET busy_state = 'listing:1' WHERE id = @p0", basic.Id);
        Assert.Equal("deck_card_busy", await Player.Code(await p.Post("/api/arena/pve", new ArenaPveRequest { StageId = "c1_s1" }, Player.Key())));
        await f.Sql("UPDATE card_instances SET busy_state = NULL WHERE id = @p0", basic.Id);
    }

    [Fact]
    public async Task Pve_match_hides_secrets_dedupes_actions_refuses_stale_versions_and_replays_exactly()
    {
        var p = await Ready("ap");
        var v = await V(await p.Post("/api/arena/pve", new ArenaPveRequest { StageId = "c1_s1" }, Player.Key()));
        Assert.Equal("pve", v.Mode); Assert.Equal(0, v.You); Assert.Equal("setup", v.State.Phase);
        var opp = v.State.Players[1]; var me = v.State.Players[0];
        Assert.All(opp.Hand, c => Assert.Equal(0, c));                          // opponent hand hidden
        Assert.All(opp.Deck.Concat(me.Deck), c => Assert.Equal(0, c));           // every deck order hidden
        Assert.Equal(0UL, v.State.Rng);
        Assert.Null(opp.Active);                                                 // AI setup choice stays secret until I set up
        Assert.All(me.Hand, c => Assert.NotEqual("", v.State.Card(c).DefId));
        Assert.Contains(me.Hand, c => Rules.Def(v.State.Card(c).DefId).IsBasic); // opening hand always has a Basic
        Assert.Equal(5, me.Hand.Count);

        // anti-cheat: wrong phase, stale version, other players
        Assert.Equal("setup_required", await Player.Code(await Send(p, v, new ArenaAction { Type = "end" })));
        var stale = new ArenaActionRequest { ActionId = "x1", Version = v.Version - 1, Action = ArenaAi.SetupChoice(Rules, v.State, 0) };
        Assert.Equal("stale_state", await Player.Code(await p.Post($"/api/arena/matches/{v.Id}/actions", stale)));
        var intruder = await f.NewPlayer("ax");
        Assert.Equal(HttpStatusCode.NotFound, (await Send(intruder, v, new ArenaAction { Type = "end" })).StatusCode);

        // the same action id twice is applied once
        var setup = ArenaAi.SetupChoice(Rules, v.State, 0);
        var v1 = await V(await Send(p, v, setup, "setup-1"));
        var v2 = await V(await Send(p, v, setup, "setup-1"));
        Assert.Equal(v1.Version, v2.Version);
        Assert.Equal(1, await f.Scalar<long>("SELECT count(*) FROM arena_events WHERE match_id=@p0 AND action_id='setup-1'", v.Id));
        Assert.Equal("main", v2.State.Phase);
        Assert.NotNull(v2.State.Players[1].Active);                              // revealed once both set up

        // reconnect finds the same match and version
        var active = await X.GetJ(p, "/api/arena/active");
        Assert.Equal("match", active.GetProperty("kind").GetString());
        Assert.Equal(v2.Version, active.GetProperty("match").GetProperty("version").GetInt32());

        v = await PlayToEnd(p, v2);
        Assert.Equal("finished", v.Status);
        Assert.Equal(1, await f.Scalar<long>("SELECT count(*) FROM arena_rewards WHERE match_id=@p0", v.Id));
        Assert.True(await f.Scalar<long>("SELECT count(*) FROM arena_snapshots WHERE match_id=@p0", v.Id) >= 1);
        await Get(p, v.Id); await Get(p, v.Id);                                   // reading a finished match never pays again
        Assert.Equal(1, await f.Scalar<long>("SELECT count(*) FROM arena_rewards WHERE match_id=@p0", v.Id));
        if (v.State.Winner == 0) Assert.Contains("currencies", v.RewardsJson ?? "");

        // replay: same definitions version + seed + action log → identical final state
        var replay = await X.GetJ(p, $"/api/arena/matches/{v.Id}/replay");
        Assert.Equal(Rules.Version, replay.GetProperty("defsVersion").GetString());
        using var scope = f.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ArenaService>();
        var dto = JsonSerializer.Deserialize<ArenaService.ReplayDto>(replay.GetRawText(), J)!;
        var rebuilt = await svc.Rebuild(dto);
        var stored = ArenaService.Load(await f.NewDb().ArenaMatches.FindAsync(v.Id) ?? throw new Exception());
        Assert.Equal(stored.Log.Select(e => e.Digest()), rebuilt.Log.Select(e => e.Digest()));
        Assert.Equal(stored.Winner, rebuilt.Winner);
        Assert.Equal(HttpStatusCode.NotFound, (await intruder.Http.GetAsync($"/api/arena/matches/{v.Id}/replay")).StatusCode);

        var hist = await X.GetJ(p, "/api/arena/history");
        Assert.Contains(hist.EnumerateArray(), h => h.GetProperty("matchId").GetGuid() == v.Id);
    }

    [Fact]
    public async Task Timers_auto_play_and_three_timeouts_forfeit_on_reconnect()
    {
        var p = await Ready("at");
        var v = await V(await p.Post("/api/arena/pve", new ArenaPveRequest { StageId = "c1_s1" }, Player.Key()));
        for (int i = 0; i < 6 && v.Status == "active"; i++)
        {
            await f.Sql("UPDATE arena_matches SET deadline = now() - interval '1 second' WHERE id = @p0", v.Id);
            v = await Get(p, v.Id);
        }
        Assert.Equal("finished", v.Status);
        Assert.Equal("timeout", v.State.EndReason);
        Assert.Equal(1, v.State.Winner);
        Assert.True(await f.Scalar<long>("SELECT count(*) FROM arena_events WHERE match_id=@p0 AND kind='timeout'", v.Id) >= 3);
    }

    [Fact]
    public async Task Casual_matchmaking_pairs_two_players_and_both_play_a_real_match()
    {
        var a = await Ready("ca"); var b = await Ready("cb");
        var qa = await X.J(await a.Post("/api/arena/queue", new ArenaQueueRequest { Mode = "casual" }));
        Assert.Equal("queued", qa.GetProperty("status").GetString());
        Assert.Equal("already_in_queue", await Player.Code(await a.Post("/api/arena/pve", new ArenaPveRequest { StageId = "c1_s1" }, Player.Key())));
        var qb = await X.J(await b.Post("/api/arena/queue", new ArenaQueueRequest { Mode = "casual" }));
        Assert.Equal("matched", qb.GetProperty("status").GetString());
        var id = qb.GetProperty("matchId").GetGuid();
        Assert.Equal(id, (await X.GetJ(a, "/api/arena/active")).GetProperty("match").GetProperty("id").GetGuid());
        Assert.Equal(0, await f.Scalar<long>("SELECT count(*) FROM arena_queue"));
        await PlayBoth(a, b, id);
        var end = await Get(a, id);
        Assert.Equal("finished", end.Status); Assert.Equal("casual", end.Mode);
        Assert.Equal(2, await f.Scalar<long>("SELECT count(*) FROM arena_rewards WHERE match_id=@p0", id));
    }

    private async Task PlayBoth(Player a, Player b, Guid id)
    {
        var va = await Get(a, id); var vb = await Get(b, id);
        Assert.NotEqual(va.You, vb.You);
        Assert.All(va.State.Players[vb.You].Hand, c => Assert.Equal(0, c));   // neither sees the other's hand
        Assert.All(vb.State.Players[va.You].Hand, c => Assert.Equal(0, c));
        for (int i = 0; i < 3000; i++)
        {
            var cur = await Get(a, id);
            if (cur.Status != "active") return;
            bool moved = false;
            foreach (var (pl, v) in new[] { (a, cur), (b, await Get(b, id)) })
                if (ArenaEngine.MustAct(v.State, v.You)) { await Player.Ok(await Send(pl, v, ArenaAi.Decide(Rules, v.State, v.You))); moved = true; break; }
            Assert.True(moved, "nobody could act");
        }
    }

    [Fact]
    public async Task Ranked_matchmaking_updates_ratings_and_history()
    {
        var a = await Ready("ra"); var b = await Ready("rb");
        await Player.Ok(await a.Post("/api/arena/queue", new ArenaQueueRequest { Mode = "ranked" }));
        var qb = await X.J(await b.Post("/api/arena/queue", new ArenaQueueRequest { Mode = "ranked" }));
        Assert.Equal("matched", qb.GetProperty("status").GetString());
        var id = qb.GetProperty("matchId").GetGuid();
        await PlayBoth(a, b, id);
        var end = await Get(a, id);
        Assert.Equal("finished", end.Status);
        Assert.Equal(2, await f.Scalar<long>("SELECT count(*) FROM arena_ratings r JOIN arena_match_players p ON p.membership_id = r.membership_id WHERE p.match_id = @p0", id));
        if (end.State.Winner != null)
        {
            var ratings = await X.GetJ(a, "/api/arena/ratings");
            int mine = ratings.GetProperty("me").GetProperty("rating").GetInt32();
            Assert.Equal(end.State.Winner == end.You ? 1016 : 984, mine);
        }
        var hist = await X.GetJ(b, "/api/arena/history");
        Assert.Contains(hist.EnumerateArray(), h => h.GetProperty("matchId").GetGuid() == id && h.GetProperty("ratingAfter").ValueKind == JsonValueKind.Number);
    }

    [Fact]
    public async Task Private_room_by_code_and_friend_invites()
    {
        var host = await Ready("rh"); var guest = await Ready("rg");
        var room = await X.J(await host.Post("/api/arena/rooms", new ArenaDeckChoice()));
        var code = room.GetProperty("roomCode").GetString()!;
        Assert.Equal(6, code.Length);
        Assert.Equal("room", (await X.GetJ(host, "/api/arena/active")).GetProperty("kind").GetString());
        Assert.Equal("room_not_found", await Player.Code(await guest.Post("/api/arena/rooms/ZZZZZZ/join", new ArenaDeckChoice())));
        var v = await V(await guest.Post($"/api/arena/rooms/{code.ToLowerInvariant()}/join", new ArenaDeckChoice()));
        Assert.Equal("private", v.Mode); Assert.Equal(1, v.You);
        await host.Post($"/api/arena/matches/{v.Id}/actions", new ArenaActionRequest { Version = v.Version, Action = new ArenaAction { Type = "surrender" } });
        Assert.Equal("finished", (await Get(guest, v.Id)).Status);
        Assert.Equal(1, (await Get(guest, v.Id)).State.Winner);

        var inv = await X.J(await host.Post("/api/arena/invite", new ArenaInviteRequest { Uid = guest.Uid }));
        var mid = inv.GetProperty("matchId").GetGuid();
        var accepted = await V(await guest.Post($"/api/arena/matches/{mid}/accept", new ArenaDeckChoice()));
        Assert.Equal("friend", accepted.Mode); Assert.Equal("active", accepted.Status);
        Assert.Equal("already_in_match", await Player.Code(await host.Post("/api/arena/invite", new ArenaInviteRequest { Uid = guest.Uid })));   // one live match per player
        Assert.Equal("invite_expired", await Player.Code(await guest.Post($"/api/arena/matches/{mid}/accept", new ArenaDeckChoice())));
    }
}
