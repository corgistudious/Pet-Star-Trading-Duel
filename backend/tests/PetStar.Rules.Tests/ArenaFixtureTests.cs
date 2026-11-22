using System.Text.Json;
using PetStar.Rules.Arena;

namespace PetStar.Rules.Tests;

/// <summary>
/// Cross-engine fixtures for the Pet Battle Arena: complete matches produced by THIS (authoritative) engine, with
/// every action (AI, random-legal and timeouts). web-client/src/arena/engine.test.ts must replay each case to the
/// identical event log, and the TypeScript AI must make the same choices in the AI-only cases.
/// Regenerate with PETSTAR_WRITE_ARENA_FIXTURES=1 after an intended rules change.
/// </summary>
public class ArenaFixtureTests
{
    private static readonly JsonSerializerOptions J = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = false };

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "web-client"))) dir = dir.Parent;
        return dir!.FullName;
    }

    public static string Build()
    {
        var rules = ArenaFixture.Rules;
        var cases = new List<object>();
        for (int i = 0; i < 16; i++)
        {
            ulong seed = (ulong)(7717 * (i + 1) + 3);
            var a = ArenaFixture.Deck(i * 3 + 1); var b = ArenaFixture.Deck(i * 5 + 2);
            bool pve = i % 4 == 3, aiOnly = i < 8;
            var st = ArenaEngine.Create(rules, new ArenaSetupPlayer { Name = "Blue", Deck = a }, new ArenaSetupPlayer { Name = "Red", Deck = b }, seed, pve);
            var rnd = new SeededRandom(seed * 31);
            var actions = new List<object>();
            for (int n = 0; !st.Finished && n < 4000; n++)
            {
                if (!aiOnly && n % 97 == 50) { ArenaEngine.Timeout(rules, st); actions.Add(new { p = -1, a = (ArenaAction?)null }); continue; }
                int p = Enumerable.Range(0, 2).First(q => ArenaEngine.MustAct(st, q));
                ArenaAction act;
                if (!aiOnly && p == 1) { var legal = ArenaAi.LegalActions(rules, st, p); act = legal[rnd.Next(legal.Count)]; }
                else act = ArenaAi.Decide(rules, st, p);
                ArenaEngine.Apply(rules, st, p, act);
                actions.Add(new { p, a = (ArenaAction?)act });
            }
            cases.Add(new
            {
                seed = seed.ToString(), pve, aiOnly, decks = new[] { a, b }, actions, winner = st.Winner, endReason = st.EndReason, turn = st.Turn,
                points = st.Players.Select(p => p.Points).ToArray(), log = st.Log.Select(e => e.Digest()).ToArray(),
            });
        }
        return JsonSerializer.Serialize(new { version = rules.Version, cases }, J);
    }

    [Fact]
    public void Committed_cross_engine_fixtures_match_the_authoritative_engine()
    {
        var path = Path.Combine(Root(), "web-client", "src", "arena", "__fixtures__", "arena-fixtures.json");
        var json = Build();
        if (Environment.GetEnvironmentVariable("PETSTAR_WRITE_ARENA_FIXTURES") == "1" || !File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, json);
        }
        Assert.Equal(json, File.ReadAllText(path));
    }

    [Fact]
    public void Web_preview_ships_the_same_card_definitions_as_the_server()
    {
        var root = Root();
        Assert.Equal(File.ReadAllText(Path.Combine(root, "database", "seed", "arena_cards.json")), File.ReadAllText(Path.Combine(root, "web-client", "public", "data", "arena_cards.json")));
    }
}
