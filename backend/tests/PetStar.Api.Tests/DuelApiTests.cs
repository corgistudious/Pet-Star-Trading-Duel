using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PetStar.Api.Features;
using PetStar.Api.Infrastructure;
using PetStar.Contracts;
using PetStar.Rules.Duel;

namespace PetStar.Api.Tests;

/// <summary>Tactical Duel through the real API + PostgreSQL: decks, PvE/PvP/Ranked, hidden information, anti-cheat,
/// timers and reconnect. Human sides are played by the same rule-abiding AI from their own (masked) view.</summary>
[Collection("api")]
public class DuelApiTests(ApiFactory f)
{
    private static readonly JsonSerializerOptions J = new(JsonSerializerDefaults.Web);
    private DuelRules Rules => f.Services.GetRequiredService<DuelRulesProvider>().Rules;

    public sealed class View { public Guid Id { get; set; } public string Mode { get; set; } = ""; public string Status { get; set; } = ""; public int You { get; set; } public int Version { get; set; } public DuelState State { get; set; } = new(); public string? RewardsJson { get; set; } }

    private static async Task<View> V(HttpResponseMessage r) { await Player.Ok(r); return JsonSerializer.Deserialize<View>(await r.Content.ReadAsStringAsync(), J)!; }

    private async Task<Player> Ready(string prefix = "d")
    {
        var p = await f.NewPlayer(prefix);
        await X.Open(p, 3);
        var auto = await X.J(await p.Post("/api/duel/decks/auto", null));
        var entries = JsonSerializer.Deserialize<List<DeckEntryJson>>(auto.GetProperty("entries").GetRawText(), J)!;
        await Player.Ok(await p.Post("/api/duel/decks", new SaveDuelDeckRequest { Name = "Main", IsDefault = true, Entries = entries }));
        return p;
    }

    private Task<HttpResponseMessage> Send(Player p, View v, DuelAction a) =>
        p.Post($"/api/duels/{v.Id}/actions", new DuelActionRequest { Version = v.Version, Action = a });

    /// <summary>Plays one side with the AI from that side's own masked view until the duel ends.</summary>
    private async Task<View> PlayToEnd(Player p, View v, int maxSteps = 900)
    {
        for (int i = 0; i < maxSteps && v.Status == "active"; i++)
        {
            if (v.State.ToAct != v.You) { v = await V(await p.Http.GetAsync($"/api/duels/{v.Id}")); if (v.State.ToAct != v.You) return v; continue; }
            v = await V(await Send(p, v, DuelAi.Decide(Rules, v.State, v.You)));
        }
        return v;
    }

    [Fact]
    public async Task Starter_collection_once_and_deck_rules_are_enforced_by_the_server()
    {
        var p = await f.NewPlayer();
        await X.Open(p, 3);
        var c1 = await X.GetJ(p, "/api/duel/collection");
        var c2 = await X.GetJ(p, "/api/duel/collection");
        int Owned(JsonElement c) => c.GetProperty("tactics").EnumerateArray().Sum(t => t.GetProperty("owned").GetInt32());
        Assert.True(Owned(c1) >= 40);
        Assert.Equal(Owned(c1), Owned(c2));                              // granted once
        Assert.Equal(33, c1.GetProperty("tactics").GetArrayLength());   // 20 Spells + 13 Traps
        var auto = JsonSerializer.Deserialize<List<DeckEntryJson>>((await X.J(await p.Post("/api/duel/decks/auto", null))).GetProperty("entries").GetRawText(), J)!;
        Assert.Equal(40, auto.Sum(e => e.Count));
        var short39 = auto.ToList(); var last = short39[^1];
        if (last.Count > 1) short39[^1] = new DeckEntryJson { Kind = last.Kind, TacticId = last.TacticId, Count = last.Count - 1 }; else short39.RemoveAt(short39.Count - 1);
        Assert.Equal("deck_must_have_40", await Player.Code(await p.Post("/api/duel/decks", new SaveDuelDeckRequest { Name = "x", Entries = short39 })));
        var greedy = auto.Where(e => e.Kind == "pet").ToList(); greedy.Add(new DeckEntryJson { Kind = "tactic", TacticId = "sp_dispel", Count = 40 - greedy.Count });
        Assert.Contains(await Player.Code(await p.Post("/api/duel/decks", new SaveDuelDeckRequest { Name = "x", Entries = greedy })), new[] { "deck_tactic_not_owned", "deck_too_many_copies" });
        var other = await f.NewPlayer("o");
        var stolen = (await X.Open(other))[0];
        var withStolen = auto.ToList(); withStolen[0] = new DeckEntryJson { Kind = "pet", CardId = stolen.Id };
        Assert.Equal("deck_card_not_owned", await Player.Code(await p.Post("/api/duel/decks", new SaveDuelDeckRequest { Name = "x", Entries = withStolen })));
        await Player.Ok(await p.Post("/api/duel/decks", new SaveDuelDeckRequest { Name = "Main", IsDefault = true, Entries = auto }));
        await f.Sql("UPDATE card_instances SET busy_state = 'listing:1' WHERE id = @p0", auto.First(e => e.Kind == "pet").CardId!.Value);
        Assert.Equal("deck_card_busy", await Player.Code(await p.Post("/api/duels/pve", new StartDuelPveRequest { StageId = "c1_s1" }, Player.Key())));
    }

    [Fact]
    public async Task Pve_duel_plays_to_the_end_hides_private_info_and_records_the_result()
    {
        var p = await Ready();
        var v = await V(await p.Post("/api/duels/pve", new StartDuelPveRequest { StageId = "c1_s1" }, Player.Key()));
        Assert.Equal("duel_pve", v.Mode); Assert.Equal(0, v.You);
        // hidden information: the opponent's hand, every deck order and the RNG never reach the client
        var opp = v.State.Players[1]; var me = v.State.Players[0];
        Assert.All(opp.Hand, c => Assert.Equal(0, c));
        Assert.All(opp.Deck.Concat(me.Deck), c => Assert.Equal(0, c));
        Assert.Equal(0UL, v.State.Rng);
        Assert.All(me.Hand, c => Assert.NotEqual("hidden", v.State.Card(c).Kind));
        Assert.True(v.State.Cards.Count(c => c.Kind == "hidden") >= 60);
        Assert.Equal(2500, me.Lp);
        // anti-cheat: stale versions and illegal moves are refused; nobody else can act on this duel
        Assert.Equal("stale_state", await Player.Code(await p.Post($"/api/duels/{v.Id}/actions", new DuelActionRequest { Version = v.Version - 1, Action = new DuelAction { Type = "end" } })));
        Assert.Equal("not_battle_phase", await Player.Code(await Send(p, v, new DuelAction { Type = "attack", Unit = 1, Target = 2 })));
        var intruder = await f.NewPlayer("x");
        Assert.Equal(HttpStatusCode.NotFound, (await Send(intruder, v, new DuelAction { Type = "end" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await p.Http.GetAsync($"/api/battles/{v.Id}")).StatusCode);   // the legacy battle API never serves a duel
        // reconnect: the active duel is found again with the same version
        var again = await V(await p.Http.GetAsync("/api/duels/active"));
        Assert.Equal(v.Id, again.Id); Assert.Equal(v.Version, again.Version);

        v = await PlayToEnd(p, v);
        Assert.Equal("finished", v.Status);
        Assert.True(v.State.Finished);
        Assert.Equal("finished", await f.Scalar<string>("SELECT status FROM battles WHERE id=@p0", v.Id));
        Assert.Equal(1, await f.Scalar<long>("SELECT count(*) FROM battle_results WHERE battle_id=@p0", v.Id));
        foreach (var pl in v.State.Players)
            Assert.Equal(v.State.Log.Count(e => e.Type == "destroyed" && e.P == v.State.Players.IndexOf(pl)) * 500, 2500 - pl.Lp);
        if (v.State.Winner == 0) Assert.Contains("currencies", v.RewardsJson ?? "");
    }

    [Fact]
    public async Task Timers_auto_pass_or_end_turns_and_three_timeouts_forfeit_and_reconnect_catches_up()
    {
        var p = await Ready("t");
        var v = await V(await p.Post("/api/duels/pve", new StartDuelPveRequest { StageId = "c1_s1" }, Player.Key()));
        int version = v.Version;
        for (int i = 0; i < 3; i++)
        {
            await f.Sql("UPDATE battles SET round_deadline = now() - interval '1 second' WHERE id = @p0", v.Id);
            v = await V(await p.Http.GetAsync($"/api/duels/{v.Id}"));        // reconnect applies the expired timer
            if (v.Status != "active") break;
        }
        Assert.True(v.Version > version);
        Assert.Contains(v.State.Log, e => e.Type == "timeout");
        Assert.Equal("finished", v.Status);
        Assert.Equal("timeout", v.State.EndReason);
        Assert.Equal(1, v.State.Winner);
    }

    [Fact]
    public async Task Pvp_duel_between_two_players_with_turn_enforcement()
    {
        var a = await Ready("pa"); var b = await Ready("pb");
        var ch = await X.J(await a.Post("/api/duels/pvp/challenge", new DuelChallengeRequest { Uid = b.Uid }));
        var id = ch.GetProperty("duelId").GetGuid();
        var vb = await V(await b.Post($"/api/duels/{id}/accept", new StartDuelRequest()));
        var va = await V(await a.Http.GetAsync($"/api/duels/{id}"));
        Assert.Equal(1, vb.You); Assert.Equal(0, va.You);
        Assert.All(vb.State.Players[0].Hand, c => Assert.Equal(0, c));   // B cannot see A's hand
        Assert.All(va.State.Players[1].Hand, c => Assert.Equal(0, c));
        var waiting = va.State.ToAct == 0 ? (b, vb) : (a, va);
        Assert.Equal("not_your_turn", await Player.Code(await Send(waiting.Item1, waiting.Item2, new DuelAction { Type = "end" })));
        for (int i = 0; i < 1500; i++)
        {
            var cur = await V(await a.Http.GetAsync($"/api/duels/{id}"));
            if (cur.Status != "active") break;
            var (pl, view) = cur.State.ToAct == 0 ? (a, cur) : (b, await V(await b.Http.GetAsync($"/api/duels/{id}")));
            await Player.Ok(await Send(pl, view, DuelAi.Decide(Rules, view.State, view.You)));
        }
        var end = await V(await a.Http.GetAsync($"/api/duels/{id}"));
        Assert.Equal("finished", end.Status);
        Assert.Equal("live_duel", end.Mode);
        Assert.Equal(1, await f.Scalar<long>("SELECT count(*) FROM battle_results WHERE battle_id=@p0", id));
    }

    [Fact]
    public async Task Ranked_duel_uses_the_same_rules_and_updates_the_rating()
    {
        var opp = await Ready("ro");
        var p = await Ready("rp");
        var v = await V(await p.Post("/api/duels/ranked", new StartDuelRequest(), Player.Key()));
        Assert.Equal("ranked", v.Mode);
        Assert.Equal(40, v.State.Players.Sum(x => x.Deck.Count + x.Hand.Count + x.Grave.Count + x.Units.Count + x.Zones.Count(z => z != null)) / 2);
        v = await PlayToEnd(p, v);
        Assert.Equal("finished", v.Status);
        Assert.True(await f.Scalar<long>("SELECT count(*) FROM ranked_records r JOIN server_memberships m ON m.id = r.membership_id WHERE m.account_id = @p0", p.Uid) >= 1);
    }
}
