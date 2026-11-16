using PetStar.Rules;
using PetStar.Rules.Battle;
using Xunit.Abstractions;

namespace PetStar.Rules.Tests;

public class ProgressionTests
{
    private static readonly GameCatalog Cat = TestCatalog.Get();

    [Fact]
    public void Star_costs_follow_the_published_table_and_stop_at_ten()
    {
        long[] beans = { 100, 200, 350, 500, 800, 1200, 1800, 2600, 4000 };
        int[] shards = { 5, 10, 15, 25, 40, 60, 90, 130, 200 };
        for (int s = 1; s <= 9; s++)
        {
            var c = Progression.StarCost(s)!;
            Assert.Equal(beans[s - 1], c.Beans);
            Assert.Equal(shards[s - 1], c.Items[Progression.PetShard]);
        }
        Assert.Null(Progression.StarCost(10));
        Assert.Null(Progression.StarCost(0));
        Assert.Equal(1.36, Progression.StarMultiplier(10), 6);
        Assert.Equal(1.36, Progression.StarMultiplier(99), 6); // clamped
    }

    [Fact]
    public void Enhancement_is_bounded_tiered_and_refund_is_half_of_spent()
    {
        Assert.Null(Progression.EnhanceCost(20));
        Assert.Equal(new[] { 1, 1, 1, 1, 1, 2, 2, 2, 2, 2, 3, 3, 3, 3, 3, 4, 4, 4, 4, 4 }, Enumerable.Range(1, 20).Select(Progression.EnhanceTier));
        Assert.Equal(0.35, Progression.EnhanceBonus(20), 6);
        Assert.Equal(0.35, Progression.EnhanceBonus(500), 6);
        double prev = 0;
        for (int l = 1; l <= 20; l++) { double b = Progression.EnhanceBonus(l); Assert.True(b > prev); prev = b; }
        // costs never decrease
        for (int l = 1; l < 20; l++) Assert.True(Progression.EnhanceCost(l)!.Beans >= Progression.EnhanceCost(l - 1)!.Beans);
        Assert.Equal(40, Progression.EnhanceCost(15)!.MinLevel);
        var spent = Progression.EnhanceSpent(20);
        Assert.Equal(Enumerable.Range(0, 20).Sum(l => Progression.EnhanceCost(l)!.Beans), spent.Beans);
    }

    [Fact]
    public void Breakthrough_raises_the_level_cap_to_seventy_with_star_gates()
    {
        Assert.Equal(50, Progression.LevelCap(0));
        Assert.Equal(70, Progression.LevelCap(4));
        Assert.Equal(70, Progression.LevelCap(9));
        Assert.Equal(3, Progression.BreakthroughCost(0)!.MinStars);
        Assert.Equal(9, Progression.BreakthroughCost(3)!.MinStars);
        Assert.Equal(65, Progression.BreakthroughCost(3)!.MinLevel);
        Assert.Null(Progression.BreakthroughCost(4));
        Assert.Equal((70, 0), StatCalculator.AddExp(69, 0, 1_000_000, 70));
        Assert.Equal((50, 0), StatCalculator.AddExp(49, 0, 1_000_000)); // existing behaviour unchanged
    }

    [Fact]
    public void Skill_levels_keep_existing_training_and_extend_to_ten()
    {
        for (int l = 1; l <= 4; l++)
        {
            var t = TrainingRules.Next(l)!; var c = Progression.SkillCost(l)!;
            Assert.Equal(t.Beans, c.Beans); Assert.Equal(t.Scrolls, c.Items[Progression.TrainingScroll]); Assert.Equal(t.MinCardLevel, c.MinLevel);
        }
        Assert.Equal(1, Progression.SkillCost(5)!.MinBreakthrough);
        Assert.Equal(4, Progression.SkillCost(9)!.MinBreakthrough);
        Assert.Null(Progression.SkillCost(10));
        Assert.Equal(1.24, Progression.SkillPowerMultiplier(5, false), 6);   // same as before the expansion
        Assert.Equal(1.39, Progression.SkillPowerMultiplier(10, false), 6);
        Assert.Equal(1.44, Progression.SkillPowerMultiplier(10, true), 6);
    }

    [Fact]
    public void Engravings_have_slot_type_and_stat_caps()
    {
        var three = new List<EngravingInfo> { new() { Type = "atk", Level = 5 }, new() { Type = "atk", Level = 5 }, new() { Type = "atk", Level = 5 } };
        Assert.Equal("engraving_type_limit", Progression.ValidateEngravings(three));
        var four = Enumerable.Range(0, 4).Select(i => new EngravingInfo { Type = Progression.EngravingTypes[i], Level = 1 }).ToList();
        Assert.Equal("engraving_slots_full", Progression.ValidateEngravings(four));
        var two = new List<EngravingInfo> { new() { Type = "spd", Level = 5 }, new() { Type = "spd", Level = 5 } };
        Assert.Null(Progression.ValidateEngravings(two));
        Assert.Equal(0.06, Progression.EngravingTotal(two, "spd"), 6);       // 2 × 3 % = 6 % (cap)
        var crit = new List<EngravingInfo> { new() { Type = "crit", Level = 5 }, new() { Type = "crit", Level = 5 } };
        Assert.Equal(0.05, Progression.EngravingTotal(crit, "crit"), 6);      // capped at +5 points
        Assert.True(Progression.SlotUnlocked(1, 10, 0));
        Assert.False(Progression.SlotUnlocked(3, 70, 0));
        Assert.True(Progression.SlotUnlocked(3, 51, 1));
    }

    [Fact]
    public void Progressed_stats_are_bounded_and_crit_is_capped()
    {
        var max = new CardProgress
        {
            Level = 999, Stars = 99, Enhance = 99, Breakthrough = 99, Awakened = true, SkillLevel = 99,
            Engravings = { new() { Type = "atk", Level = 5 }, new() { Type = "atk", Level = 5 }, new() { Type = "crit", Level = 5 } },
        };
        var s = Progression.Compute(Cat, "pet_emberkit", "Secret", "SSS", max);
        var f = Cat.Form("pet_emberkit");
        double levelM = 1 + StatCalculator.PerLevel * 69, rarityM = 1.36 * 1.12;
        Assert.True(s.Atk <= Math.Ceiling(f.Stats.Atk * levelM * rarityM * Progression.MaxGrowthMultiplier));
        Assert.True(s.Crit <= 0.5);
        Assert.Equal(Math.Round(1.36 * 1.35 * 1.06 * 1.12, 6), Math.Round(Progression.MaxGrowthMultiplier, 6));
        // a default progress equals the old stat calculator exactly (existing cards keep their stats)
        var old = StatCalculator.Compute(Cat, "pet_emberkit", 23, "SR", "A");
        var now = Progression.Compute(Cat, "pet_emberkit", "SR", "A", new CardProgress { Level = 23 });
        Assert.Equal((old.Hp, old.Atk, old.Def, old.Spd, old.Crit), (now.Hp, now.Atk, now.Def, now.Spd, now.Crit));
    }

    [Fact]
    public void Skill_level_changes_real_battle_damage()
    {
        int Damage(int skillLevel, bool awakened)
        {
            var a = BattleEngine.CreateUnit(Cat, 0, 0, 0, 1, "pet_pyrelynx", "SR", "B", new CardProgress { Level = 30, SkillLevel = skillLevel, Awakened = awakened });
            var team = new List<BattleUnit> { a };
            for (int i = 1; i < 5; i++) team.Add(BattleEngine.CreateUnit(Cat, 0, i, i, 1 + i, "pet_halowl", 1, "N", "B"));
            var foes = Enumerable.Range(0, 5).Select(i => BattleEngine.CreateUnit(Cat, 1, i, i, 10 + i, "pet_puddlotl", 20, "N", "B")).ToList();
            var e = new BattleEngine(Cat);
            var st = e.Start(team, foes, 7);
            st.Energy[0] = 10;
            a.Crit = 0;
            var ev = e.ResolveRound(st, new[] { new PlannedAction { UnitIndex = 0, Type = ActionType.Skill, TargetIndex = 0 } }, Array.Empty<PlannedAction>());
            return ev.First(x => x.Type == "damage" && x.ActorSide == 0 && x.ActorIndex == 0).Amount;
        }
        int l1 = Damage(1, false), l5 = Damage(5, false), l10 = Damage(10, false), l10a = Damage(10, true);
        Assert.True(l1 < l5 && l5 < l10 && l10 < l10a, $"{l1} {l5} {l10} {l10a}");
        Assert.InRange(l10 / (double)l1, 1.30, 1.48);
    }
}

public class DivineOddsTests
{
    private static readonly GameCatalog Cat = TestCatalog.Get();

    [Theory]
    [InlineData("divine_premium_pack", 2000, "premium")]
    [InlineData("divine_limited_pack", 1500, "limited")]
    [InlineData("divine_star_pack", 1000, "star")]
    public void Divine_group_rate_per_slot_and_uniform_per_form(string packId, int groupPpm, string set)
    {
        var p = Cat.PackType(packId)!;
        Assert.Equal(groupPpm, p.SlotTable[RarityDef.Divine]);
        Assert.Equal(1_000_000, p.SlotTable.Values.Sum());
        var pool = PackRoller.DivinePool(Cat, p);
        Assert.Equal(5, pool.Count);
        Assert.All(pool, f => Assert.Equal(set, f.DivineSet));
        // divine pets never appear in a regular pool
        Assert.All(Cat.PackTypes, x => Assert.DoesNotContain(PackRoller.Pool(Cat, x), f => f.DivineSet != null));

        const int packs = 250_000; double slots = packs * 5.0;
        var rng = new SeededRandom((ulong)groupPpm * 31);
        var perForm = pool.ToDictionary(f => f.Id, _ => 0);
        int group = 0;
        for (int i = 0; i < packs; i++)
            foreach (var c in PackRoller.Roll(Cat, p, rng))
                if (c.Rarity == RarityDef.Divine) { group++; perForm[c.FormId]++; Assert.Equal(set, Cat.Form(c.FormId).DivineSet); }
                else Assert.Null(Cat.Form(c.FormId).DivineSet);
        double pg = groupPpm / 1e6, pf = pg / 5;
        Assert.InRange(group / slots, pg - 5 * Math.Sqrt(pg / slots), pg + 5 * Math.Sqrt(pg / slots));
        foreach (var n in perForm.Values) Assert.InRange(n / slots, pf - 5 * Math.Sqrt(pf / slots), pf + 5 * Math.Sqrt(pf / slots));
    }

    [Fact]
    public void Published_odds_match_the_owner_tables_exactly()
    {
        Dictionary<string, double> T(string id) => Cat.PackType(id)!.SlotTable.ToDictionary(x => x.Key, x => x.Value / 10000.0);
        Assert.Equal(new Dictionary<string, double> { ["N"] = 60, ["R"] = 28, ["SR"] = 9, ["SSR"] = 2.5, ["UR"] = 0.5 }, T("basic_pack"));
        Assert.Equal(new Dictionary<string, double> { ["N"] = 35, ["R"] = 38, ["SR"] = 19, ["SSR"] = 6, ["UR"] = 1.8, ["EX"] = 0.2 }, T("premium_pack"));
        Assert.Equal(new Dictionary<string, double> { ["N"] = 15, ["R"] = 35, ["SR"] = 32, ["SSR"] = 13, ["UR"] = 4.5, ["EX"] = 0.5 }, T("transcendent_pack"));
        Assert.Equal(new Dictionary<string, double> { ["N"] = 5, ["R"] = 25, ["SR"] = 40, ["SSR"] = 22, ["UR"] = 7, ["EX"] = 1 }, T("epic_pack"));
        Assert.Equal(new Dictionary<string, double> { ["R"] = 15, ["SR"] = 35, ["SSR"] = 34, ["UR"] = 14, ["EX"] = 2 }, T("legendary_pack"));
        Assert.Equal(new Dictionary<string, double> { ["R"] = 5, ["SR"] = 25, ["SSR"] = 42, ["UR"] = 24, ["EX"] = 4 }, T("mythic_pack"));
        Assert.Equal(9, Cat.PackTypes.Count);
        var prices = Cat.PackTypes.ToDictionary(p => p.Id, p => (p.Price.Currency, p.Price.Amount));
        Assert.Equal((Currency.MiniGreenBean, 100), prices["basic_pack"]);
        Assert.Equal((Currency.MiniGreenBean, 800), prices["transcendent_pack"]);
        Assert.Equal((Currency.RainbowStar, 120), prices["mythic_pack"]);
        Assert.Equal((Currency.RainbowStar, 500), prices["divine_star_pack"]);
        var star = Cat.PackType("divine_star_pack")!.Requirements;
        Assert.True(star.RequiresInvitation); Assert.Equal(60, star.MinLevel); Assert.Equal(7, star.MinVip);
        var lim = Cat.PackType("divine_limited_pack")!.Requirements;
        Assert.True(lim.RequiresEvent); Assert.Equal(40, lim.MinLevel); Assert.Equal(4, lim.MinVip);
        var prem = Cat.PackType("divine_premium_pack")!.Requirements;
        Assert.Equal(30, prem.MinLevel); Assert.Equal(3, prem.MinVip);
    }
}

/// <summary>Balance guard: Divine Pets must be special, not invincible, and must not make regular pets useless.</summary>
public class BalanceTests(ITestOutputHelper output)
{
    private static readonly GameCatalog Cat = TestCatalog.Get();

    private static CardProgress Max() => new()
    {
        Level = 70, Stars = 10, Enhance = 20, Breakthrough = 4, Awakened = true, SkillLevel = 10,
        Engravings = { new() { Type = "atk", Level = 5 }, new() { Type = "hp", Level = 5 }, new() { Type = "def", Level = 5 } },
    };

    // same formation rule for both teams: guardian, striker, controller in front; mage, support behind
    private static readonly string[] RoleOrder = { "guardian", "striker", "controller", "mage", "support" };
    private static List<string> Formation(IEnumerable<PetFormDef> forms) =>
        forms.OrderBy(f => Array.IndexOf(RoleOrder, f.Role)).Select(f => f.Id).ToList();

    private static List<string> BestRegular() =>
        Formation(Cat.Forms.Values.Where(f => f.DivineSet == null && (f.Stage == 3 || f.FamilyStages == 1))
            .OrderByDescending(f => f.Stats.Hp * 0.25 + f.Stats.Atk * 2 + f.Stats.Def * 1.5 + f.Stats.Spd)
            .GroupBy(f => f.Role).Select(g => g.First()));

    private static List<string> Divine(string set) => Formation(Cat.Forms.Values.Where(f => f.DivineSet == set));

    private (int a, int b, int draw) Simulate(List<string> teamA, string rarityA, CardProgress progA, List<string> teamB, string rarityB, CardProgress progB, int games)
    {
        var e = new BattleEngine(Cat); var ai = new BattleAi(Cat);
        int wa = 0, wb = 0, d = 0;
        for (int g = 0; g < games; g++)
        {
            bool swap = g % 2 == 1; // alternate sides so side order never decides
            var (ta, ra, pa, tb, rb, pb) = swap ? (teamB, rarityB, progB, teamA, rarityA, progA) : (teamA, rarityA, progA, teamB, rarityB, progB);
            List<BattleUnit> Build(int side, List<string> t, string r, CardProgress p) =>
                t.Select((f, i) => BattleEngine.CreateUnit(Cat, side, i, i, side * 10 + i + 1, f, r, "SSS", p)).ToList();
            var st = e.Start(Build(0, ta, ra, pa), Build(1, tb, rb, pb), (ulong)(g * 7919 + 13));
            int guard = 0;
            while (!st.Finished && guard++ < 50) e.ResolveRound(st, ai.Plan(st, 0, 2), ai.Plan(st, 1, 2));
            int? w = st.Winner;
            if (w == null || w == -1) d++;
            else if ((w == 0) != swap) wa++; else wb++;
        }
        return (wa, wb, d);
    }

    [Fact]
    public void Maxed_divine_team_is_strong_but_beatable_by_maxed_regular_team()
    {
        var regular = BestRegular();
        foreach (var set in new[] { "premium", "limited", "star" })
        {
            var (dv, reg, draw) = Simulate(Divine(set), RarityDef.Divine, Max(), regular, "Secret", Max(), 400);
            output.WriteLine($"{set} maxed vs regular Secret maxed: divine {dv}, regular {reg}, draws {draw}");
            Assert.True(reg >= 400 * 0.20, $"regular team must keep winning a real share ({reg}/400)");
            Assert.True(dv <= 400 * 0.80, $"divine team must not be invincible ({dv}/400)");
            Assert.True(dv >= 400 * 0.25, $"a maxed divine set should stay competitive ({dv}/400)");
        }
    }

    [Fact]
    public void Fresh_ten_star_divine_does_not_beat_a_developed_regular_team()
    {
        var fresh = new CardProgress { Level = 1, Stars = 10 };
        var (dv, reg, _) = Simulate(Divine("star"), RarityDef.Divine, fresh, BestRegular(), "UR", Max(), 200);
        output.WriteLine($"fresh 10★ star set vs developed regular UR: divine {dv}, regular {reg}");
        Assert.True(reg > dv * 4, "10 stars at creation is not max level/enhancement/skills");
    }

    [Fact]
    public void Divine_power_advantage_over_best_regular_of_the_same_role_is_limited()
    {
        double Power(string form, string rarity) => Progression.Compute(Cat, form, rarity, "SSS", Max()).Power;
        foreach (var role in Cat.Forms.Values.Where(f => f.DivineSet != null).Select(f => f.Role).Distinct())
        {
            double bestRegular = Cat.Forms.Values.Where(f => f.DivineSet == null && f.Role == role).Max(f => Power(f.Id, "Secret"));
            double bestDivine = Cat.Forms.Values.Where(f => f.DivineSet != null && f.Role == role).Max(f => Power(f.Id, RarityDef.Divine));
            output.WriteLine($"{role}: best regular Secret {bestRegular}, best divine {bestDivine} ({bestDivine / bestRegular - 1:P1})");
            Assert.True(bestDivine <= bestRegular * 1.12, $"{role}: divine {bestDivine} vs regular {bestRegular}");
            Assert.True(bestDivine >= bestRegular, $"{role}: a Divine Pet should be at least as strong as the best regular pet of its role");
        }
    }
}
