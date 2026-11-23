using System.Text.Json;
using PetStar.Rules.Team;

namespace PetStar.Rules.Tests;

/// <summary>Cross-engine fixtures for Battle 3v3: complete battles from THIS (authoritative) engine with every plan,
/// random orders and timeouts. web-client/src/team/engine.test.ts must replay them to identical logs, and its AI must
/// choose the same plans in the AI-only cases. Regenerate with PETSTAR_WRITE_TEAM_FIXTURES=1.</summary>
public class TeamFixtureTests
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
        var rules = TeamFixture.Rules;
        var cases = new List<object>();
        for (int i = 0; i < 14; i++)
        {
            ulong seed = (ulong)(9091 * (i + 1) + 5);
            var a = TeamFixture.Team(i * 11 + 2, 10 + i * 4, "Blue"); var b = TeamFixture.Team(i * 17 + 9, 12 + i * 3, "Red");
            bool aiOnly = i < 7;
            var st = TeamEngine.Create(rules, a, b, seed);
            var rnd = new SeededRandom(seed * 13);
            var steps = new List<object>();
            for (int n = 0; !st.Finished && n < 400; n++)
            {
                if (!aiOnly && n % 9 == 4) { TeamEngine.Timeout(rules, st); steps.Add(new { p = -1, a = (TeamAction?)null }); continue; }
                int p = TeamEngine.MustAct(st, 0) ? 0 : 1;
                TeamAction act;
                if (!aiOnly && p == 1)
                {
                    var orders = new List<TeamOrder>();
                    foreach (var u in TeamEngine.Team(st, p).Where(u => u.Alive))
                    {
                        var slots = new[] { "basic", "skill", "ultimate", "guard" }.Where(s => TeamEngine.OrderProblem(rules, st, p, new TeamOrder { Unit = u.Uid, Slot = s }) == null).ToList();
                        orders.Add(new TeamOrder { Unit = u.Uid, Slot = slots[rnd.Next(slots.Count)] });
                    }
                    act = new TeamAction { Orders = orders };
                }
                else act = TeamAi.Decide(rules, st, p);
                TeamEngine.Apply(rules, st, p, act);
                steps.Add(new { p, a = (TeamAction?)act });
            }
            cases.Add(new { seed = seed.ToString(), aiOnly, teams = new[] { a, b }, steps, winner = st.Winner, endReason = st.EndReason, round = st.Round, log = st.Log.Select(e => e.Digest()).ToArray() });
        }
        return JsonSerializer.Serialize(new { version = rules.Version, cases }, J);
    }

    [Fact]
    public void Committed_cross_engine_fixtures_match_the_authoritative_engine()
    {
        var path = Path.Combine(Root(), "web-client", "src", "team", "__fixtures__", "team-fixtures.json");
        var json = Build();
        if (Environment.GetEnvironmentVariable("PETSTAR_WRITE_TEAM_FIXTURES") == "1" || !File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, json);
        }
        Assert.Equal(json, File.ReadAllText(path));
    }

    [Fact]
    public void Web_preview_ships_the_same_battle_data_as_the_server()
    {
        var root = Root();
        Assert.Equal(File.ReadAllText(Path.Combine(root, "database", "seed", "team_battle.json")), File.ReadAllText(Path.Combine(root, "web-client", "public", "data", "team_battle.json")));
    }
}
