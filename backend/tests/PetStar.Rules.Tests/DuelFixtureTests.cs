using System.Text.Json;
using PetStar.Rules.Duel;

namespace PetStar.Rules.Tests;

/// <summary>
/// Cross-engine fixtures: full AI-vs-AI duels produced by THIS (authoritative) engine. The TypeScript engine used by
/// the Interactive Web Preview must replay every case to the identical log (web-client/src/duel/engine.test.ts).
/// Regenerate with PETSTAR_WRITE_DUEL_FIXTURES=1 after an intended rules change; otherwise this test fails if the
/// committed fixtures no longer match the engine (so the two engines cannot drift silently).
/// </summary>
public class DuelFixtureTests
{
    private static readonly JsonSerializerOptions J = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = false };

    private static string FixturePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "web-client"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "web-client", "src", "duel", "__fixtures__", "duel-fixtures.json");
    }

    public static string Build()
    {
        var rules = DuelFixture.Rules;
        var cases = new List<object>();
        for (int i = 0; i < 8; i++)
        {
            ulong seed = (ulong)(1009 * (i + 1) + 17);
            var a = DuelFixture.Deck(i % 6, 15 + i * 5);
            var b = DuelFixture.Deck((i + 3) % 6, 20 + i * 4);
            var st = DuelEngine.Create(rules, new DuelSetupPlayer { Name = "Blue", Deck = a }, new DuelSetupPlayer { Name = "Red", Deck = b }, seed);
            DuelFixture.PlayOut(st);
            cases.Add(new
            {
                seed = seed.ToString(), decks = new[] { a, b }, winner = st.Winner, endReason = st.EndReason, turn = st.Turn,
                lp = st.Players.Select(p => p.Lp).ToArray(), log = st.Log.Select(e => e.Digest()).ToArray(),
            });
        }
        return JsonSerializer.Serialize(new { rules, cases }, J);
    }

    [Fact]
    public void Committed_cross_engine_fixtures_match_the_authoritative_engine()
    {
        var path = FixturePath();
        var json = Build();
        if (Environment.GetEnvironmentVariable("PETSTAR_WRITE_DUEL_FIXTURES") == "1" || !File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, json);
        }
        Assert.Equal(json, File.ReadAllText(path));
    }
}
