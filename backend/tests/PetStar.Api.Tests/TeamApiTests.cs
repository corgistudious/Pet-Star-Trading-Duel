using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PetStar.Api.Features;
using PetStar.Api.Infrastructure;
using PetStar.Rules.Team;

namespace PetStar.Api.Tests;

/// <summary>Battle 3v3 and Trainer profile through the real API + PostgreSQL: teams from owned cards, PvE / Boss /
/// Practice, two-player PvP (matchmaking, ranked, private room, friend invite) with hidden simultaneous plans,
/// idempotent actions, stale versions, timers, reconnect, replay, history, rewards.</summary>
[Collection("api")]
public class TeamApiTests(ApiFactory f)
{
    private static readonly JsonSerializerOptions J = new(JsonSerializerDefaults.Web);
    private TeamRules Rules => f.Services.GetRequiredService<TeamRulesProvider>().Rules;

    public sealed class View { public Guid Id { get; set; } public string Mode { get; set; } = ""; public string Status { get; set; } = ""; public int You { get; set; } public int Version { get; set; } public TeamState State { get; set; } = new(); public string? RewardsJson { get; set; } }
    private static async Task<View> V(HttpResponseMessage r) { await Player.Ok(r); return JsonSerializer.Deserialize<View>(await r.Content.ReadAsStringAsync(), J)!; }
    private static async Task<View> Get(Player p, Guid id) => await V(await p.Http.GetAsync($"/api/team/matches/{id}"));
    private static Task<HttpResponseMessage> Send(Player p, View v, TeamAction a, string? actionId = null) =>
        p.Post($"/api/team/matches/{v.Id}/actions", new TeamActionRequest { ActionId = actionId ?? Guid.NewGuid().ToString("N"), Version = v.Version, Action = a });

    private async Task<(Player p, List<long> cards)> Ready(string prefix)
    {
        var p = await f.NewPlayer(prefix);
        var cards = (await X.Open(p, 1)).Select(c => c.Id).Take(3).ToList();
        await Player.Ok(await p.Post("/api/team/lineups", new SaveTeamRequest { Name = "Main", IsDefault = true, CardIds = cards }));
        return (p, cards);
    }

    [Fact]
    public async Task Trainer_is_chosen_once_saved_and_companion_and_outfit_follow_ownership()
    {
        var p = await f.NewPlayer("tr");
        var cards = await X.Open(p, 1);
        Assert.False((await X.GetJ(p, "/api/trainer")).GetProperty("chosen").GetBoolean());
        Assert.Equal("trainer_not_chosen", await Player.Code(await p.Http.PutAsync("/api/trainer/companion", JsonContent(new { cardId = cards[0].Id }))));
        Assert.Equal("unknown_character", await Player.Code(await p.Post("/api/trainer", new ChooseTrainerRequest { CharacterId = "trainer_dragon" })));
        await Player.Ok(await p.Post("/api/trainer", new ChooseTrainerRequest { CharacterId = "trainer_girl" }));
        Assert.Equal("trainer_already_chosen", await Player.Code(await p.Post("/api/trainer", new ChooseTrainerRequest { CharacterId = "trainer_boy" })));
        await Player.Ok(await p.Http.PutAsync("/api/trainer/companion", JsonContent(new { cardId = cards[0].Id })));
        var other = await f.NewPlayer("tro"); var theirs = (await X.Open(other, 1))[0];
        Assert.Equal("card_not_owned", await Player.Code(await p.Http.PutAsync("/api/trainer/companion", JsonContent(new { cardId = theirs.Id }))));
        Assert.Equal("outfit_wrong_character", await Player.Code(await p.Http.PutAsync("/api/trainer/outfit", JsonContent(new { outfitId = "outfit_boy_default" }))));
        await Player.Ok(await p.Http.PutAsync("/api/trainer/outfit", JsonContent(new { outfitId = "outfit_girl_default" })));
        // log out / log in again: everything is still there
        await p.Enter(1);
        var t = await X.GetJ(p, "/api/trainer");
        Assert.Equal("trainer_girl", t.GetProperty("characterId").GetString());
        Assert.Equal("outfit_girl_default", t.GetProperty("outfitId").GetString());
        Assert.Equal(cards[0].Id, t.GetProperty("companion").GetProperty("id").GetInt64());
        Assert.Contains("outfit_girl_default", t.GetProperty("ownedOutfits").EnumerateArray().Select(x => x.GetString()));
    }

    private static StringContent JsonContent(object o) => new(JsonSerializer.Serialize(o, J), System.Text.Encoding.UTF8, "application/json");

    [Fact]
    public async Task Teams_need_three_different_owned_free_cards()
    {
        var p = await f.NewPlayer("tl");
        var cards = (await X.Open(p, 1)).Select(c => c.Id).ToList();
        Assert.Equal("team_needs_3_different_pets", await Player.Code(await p.Post("/api/team/lineups", new SaveTeamRequest { CardIds = cards.Take(2).ToList() })));
        Assert.Equal("team_needs_3_different_pets", await Player.Code(await p.Post("/api/team/lineups", new SaveTeamRequest { CardIds = new() { cards[0], cards[0], cards[1] } })));
        var other = await f.NewPlayer("tlo"); var theirs = (await X.Open(other, 1))[0].Id;
        Assert.Equal("team_card_not_owned", await Player.Code(await p.Post("/api/team/lineups", new SaveTeamRequest { CardIds = new() { cards[0], cards[1], theirs } })));
        var ok = await X.J(await p.Post("/api/team/lineups", new SaveTeamRequest { Name = "A", CardIds = cards.Take(3).ToList() }));
        Assert.Equal(3, ok.GetProperty("preview").GetArrayLength());
        Assert.True(ok.GetProperty("preview")[0].GetProperty("mag").GetInt32() > 0);
        await f.Sql("UPDATE card_instances SET busy_state = 'listing:1' WHERE id = @p0", cards[0]);
        Assert.Equal("team_card_busy", await Player.Code(await p.Post("/api/team/pve", new TeamPveRequest { StageId = "c1_s1" }, Player.Key())));
    }

    private async Task<View> PlayToEnd(Player p, View v, int max = 200)
    {
        for (int i = 0; i < max && v.Status == "active"; i++)
        {
            if (!TeamEngine.MustAct(v.State, v.You)) { v = await Get(p, v.Id); if (!TeamEngine.MustAct(v.State, v.You)) return v; continue; }
            v = await V(await Send(p, v, TeamAi.Decide(Rules, v.State, v.You)));
        }
        return v;
    }

    [Fact]
    public async Task Pve_battle_plays_to_the_end_dedupes_actions_refuses_stale_versions_and_replays_exactly()
    {
        var (p, _) = await Ready("tp");
        var v = await V(await p.Post("/api/team/pve", new TeamPveRequest { StageId = "c1_s1" }, Player.Key()));
        Assert.Equal("pve", v.Mode); Assert.Equal(0, v.You); Assert.Equal(6, v.State.Units.Count);
        Assert.True(v.State.Players[1].Locked);                                     // the AI locked its plan...
        Assert.Empty(v.State.Players[1].Plan ?? new());                              // ...but the plan is hidden
        Assert.Equal(0UL, v.State.Rng);
        Assert.Equal("stale_state", await Player.Code(await p.Post($"/api/team/matches/{v.Id}/actions", new TeamActionRequest { ActionId = "s", Version = v.Version - 1, Action = TeamAi.Decide(Rules, v.State, 0) })));
        var intruder = await f.NewPlayer("tx");
        Assert.Equal(HttpStatusCode.NotFound, (await Send(intruder, v, TeamAi.Decide(Rules, v.State, 0))).StatusCode);
        var plan = TeamAi.Decide(Rules, v.State, 0);
        var v1 = await V(await Send(p, v, plan, "round-1"));
        var v2 = await V(await Send(p, v, plan, "round-1"));                         // a retry is not applied twice
        Assert.Equal(v1.Version, v2.Version);
        Assert.Equal(1, await f.Scalar<long>("SELECT count(*) FROM arena_events WHERE match_id=@p0 AND action_id='round-1'", v.Id));
        Assert.Equal(2, v2.State.Round);
        var active = await X.GetJ(p, "/api/team/active");
        Assert.Equal("match", active.GetProperty("kind").GetString());
        v = await PlayToEnd(p, v2);
        Assert.Equal("finished", v.Status);
        Assert.Equal(1, await f.Scalar<long>("SELECT count(*) FROM arena_rewards WHERE match_id=@p0", v.Id));
        await Get(p, v.Id);
        Assert.Equal(1, await f.Scalar<long>("SELECT count(*) FROM arena_rewards WHERE match_id=@p0", v.Id));
        if (v.State.Winner == 0) Assert.Contains("currencies", v.RewardsJson ?? "");
        var replay = await X.GetJ(p, $"/api/team/matches/{v.Id}/replay");
        using var scope = f.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<TeamService>();
        var rebuilt = await svc.Rebuild(JsonSerializer.Deserialize<TeamService.ReplayDto>(replay.GetRawText(), J)!);
        var stored = TeamService.Load(await f.NewDb().ArenaMatches.FindAsync(v.Id) ?? throw new Exception());
        Assert.Equal(stored.Log.Select(e => e.Digest()), rebuilt.Log.Select(e => e.Digest()));
        Assert.Contains((await X.GetJ(p, "/api/team/history")).EnumerateArray(), h => h.GetProperty("matchId").GetGuid() == v.Id);
    }

    [Fact]
    public async Task Boss_and_practice_modes_and_plan_timer_forfeit()
    {
        var (p, _) = await Ready("tb");
        var boss = await V(await p.Post("/api/team/pve", new TeamPveRequest { StageId = "c1_s1", Kind = "boss" }, Player.Key()));
        Assert.Equal("boss", boss.Mode);
        Assert.True(boss.State.Units.First(u => u.Side == 1 && u.Slot == 0).MaxHp > boss.State.Units.First(u => u.Side == 1 && u.Slot == 1).MaxHp);
        var pr = await V(await p.Post("/api/team/pve", new TeamPveRequest { StageId = "c1_s1", Kind = "practice" }, Player.Key()));   // abandons the boss match
        Assert.Equal("practice", pr.Mode);
        for (int i = 0; i < 5 && pr.Status == "active"; i++)
        {
            await f.Sql("UPDATE arena_matches SET deadline = now() - interval '1 second' WHERE id = @p0", pr.Id);
            pr = await Get(p, pr.Id);
        }
        Assert.Equal("finished", pr.Status);
        Assert.Equal("timeout", pr.State.EndReason);
        Assert.Equal("{}", await f.Scalar<string>("SELECT (rewards_json->'rewards')::text FROM arena_rewards WHERE match_id=@p0", pr.Id));   // practice pays nothing
    }

    private async Task PlayBoth(Player a, Player b, Guid id)
    {
        var va = await Get(a, id); var vb = await Get(b, id);
        Assert.NotEqual(va.You, vb.You);
        for (int i = 0; i < 400; i++)
        {
            if ((await Get(a, id)).Status != "active") return;
            bool moved = false;
            foreach (var pl in new[] { a, b })
            {
                var v = await Get(pl, id);                                           // fresh view: the other player may just have moved
                if (TeamEngine.MustAct(v.State, v.You))
                {
                    // the opponent's locked plan is never visible
                    var opp = v.State.Players[1 - v.You];
                    if (opp.Locked && !v.State.Finished) Assert.Empty(opp.Plan ?? new());
                    await Player.Ok(await Send(pl, v, TeamAi.Decide(Rules, v.State, v.You))); moved = true;
                }
            }
            Assert.True(moved, "nobody could act");
        }
    }

    [Fact]
    public async Task Two_players_casual_matchmaking_then_ranked_updates_ratings()
    {
        var (a, _) = await Ready("ca"); var (b, _) = await Ready("cb");
        Assert.Equal("queued", (await X.J(await a.Post("/api/team/queue", new TeamQueueRequest { Mode = "casual" }))).GetProperty("status").GetString());
        Assert.Equal("already_in_queue", await Player.Code(await a.Post("/api/team/pve", new TeamPveRequest { StageId = "c1_s1" }, Player.Key())));
        var qb = await X.J(await b.Post("/api/team/queue", new TeamQueueRequest { Mode = "casual" }));
        Assert.Equal("matched", qb.GetProperty("status").GetString());
        var id = qb.GetProperty("matchId").GetGuid();
        await PlayBoth(a, b, id);
        Assert.Equal("finished", (await Get(a, id)).Status);
        Assert.Equal(2, await f.Scalar<long>("SELECT count(*) FROM arena_rewards WHERE match_id=@p0", id));

        await Player.Ok(await a.Post("/api/team/queue", new TeamQueueRequest { Mode = "ranked" }));
        var rb = await X.J(await b.Post("/api/team/queue", new TeamQueueRequest { Mode = "ranked" }));
        var rid = rb.GetProperty("matchId").GetGuid();
        await PlayBoth(a, b, rid);
        var end = await Get(a, rid);
        Assert.Equal("ranked", end.Mode);
        if (end.State.Winner != null)
            Assert.Equal(end.State.Winner == end.You ? 1016 : 984, (await X.GetJ(a, "/api/team/ratings")).GetProperty("me").GetProperty("rating").GetInt32());
    }

    [Fact]
    public async Task Private_room_and_friend_invite_and_surrender()
    {
        var (host, _) = await Ready("rh"); var (guest, _) = await Ready("rg");
        var room = await X.J(await host.Post("/api/team/rooms", new TeamChoice()));
        var code = room.GetProperty("roomCode").GetString()!;
        Assert.Equal("room_not_found", await Player.Code(await guest.Post("/api/team/rooms/ZZZZZZ/join", new TeamChoice())));
        var v = await V(await guest.Post($"/api/team/rooms/{code.ToLowerInvariant()}/join", new TeamChoice()));
        Assert.Equal("private", v.Mode); Assert.Equal(1, v.You);
        await host.Post($"/api/team/matches/{v.Id}/actions", new TeamActionRequest { Version = v.Version, Action = new TeamAction { Type = "surrender" } });
        Assert.Equal(1, (await Get(guest, v.Id)).State.Winner);
        var inv = await X.J(await host.Post("/api/team/invite", new TeamInviteRequest { Uid = guest.Uid }));
        var mid = inv.GetProperty("matchId").GetGuid();
        var acc = await V(await guest.Post($"/api/team/matches/{mid}/accept", new TeamChoice()));
        Assert.Equal("friend", acc.Mode);
        Assert.Equal("already_in_match", await Player.Code(await host.Post("/api/team/invite", new TeamInviteRequest { Uid = guest.Uid })));
        await PlayBoth(host, guest, mid);
        Assert.Equal("finished", (await Get(host, mid)).Status);
    }
}
