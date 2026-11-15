using PetStar.Api.Infrastructure;
using PetStar.Rules;
using PetStar.Rules.Battle;

namespace PetStar.Rules.Tests;

public static class TestCatalog
{
    private static readonly Lazy<GameCatalog> Lazy = new(() => CatalogLoader.LoadCatalog(Path.Combine(AppContext.BaseDirectory, "seed")));
    public static GameCatalog Get() => Lazy.Value;
}

public class CatalogTests
{
    [Fact]
    public void Seed_catalog_is_valid()
    {
        var cat = TestCatalog.Get();
        Assert.Empty(cat.Validate());
        Assert.Equal(10, cat.Forms.Count);
        Assert.Equal(10, cat.Rarities.Count);
        Assert.Equal(7, cat.Qualities.Count);
        Assert.Equal(5, cat.Finishes.Count);
        Assert.Equal(8, cat.Elements.Elements.Count);
    }

    [Fact]
    public void Rarity_colors_match_spec()
    {
        var expected = new Dictionary<string, string>
        {
            ["N"] = "#9CA3AF", ["R"] = "#43BD78", ["SR"] = "#4099F4", ["SSR"] = "#AA70E8", ["UR"] = "#F3C04F",
            ["LR"] = "#F36D79", ["MR"] = "#DE6DC9", ["GR"] = "#48D0C4", ["EX"] = "#E6B95E", ["Secret"] = "#9673EC",
        };
        foreach (var r in TestCatalog.Get().Rarities) Assert.Equal(expected[r.Id], r.Color);
    }

    [Fact]
    public void Evolution_lines_are_ordered_and_terminate()
    {
        var cat = TestCatalog.Get();
        var ember = cat.Form("pet_emberkit");
        Assert.Equal("pet_cinderlynx", cat.NextForm(ember)!.Id);
        Assert.Equal("pet_pyrelynx", cat.NextForm(cat.Form("pet_cinderlynx"))!.Id);
        Assert.Null(cat.NextForm(cat.Form("pet_pyrelynx")));
        Assert.Null(cat.NextForm(cat.Form("pet_halowl")));
    }
}

public class ElementTests
{
    [Theory]
    [InlineData("water", "fire", 1.5)]
    [InlineData("fire", "water", 0.75)]
    [InlineData("fire", "nature", 1.5)]
    [InlineData("nature", "earth", 1.5)]
    [InlineData("earth", "lightning", 1.5)]
    [InlineData("lightning", "wind", 1.5)]
    [InlineData("wind", "water", 1.5)]
    [InlineData("light", "dark", 1.5)]
    [InlineData("dark", "light", 1.5)]
    [InlineData("fire", "lightning", 1.0)]
    [InlineData("fire", "fire", 1.0)]
    public void Element_chart(string a, string d, double m) => Assert.Equal(m, TestCatalog.Get().Elements.Multiplier(a, d));

    [Fact]
    public void Every_base_element_has_one_strength_and_one_weakness()
    {
        var chart = TestCatalog.Get().Elements;
        foreach (var e in new[] { "fire", "water", "nature", "earth", "lightning", "wind" })
        {
            var others = chart.Elements.Select(x => x.Id).Where(x => x != e).ToList();
            Assert.Equal(1, others.Count(o => chart.Multiplier(e, o) > 1));
            Assert.Equal(1, others.Count(o => chart.Multiplier(e, o) < 1));
        }
    }
}

public class RandomTests
{
    [Fact]
    public void SeededRandom_is_deterministic_and_unbiased_enough()
    {
        var a = new SeededRandom(42); var b = new SeededRandom(42);
        for (int i = 0; i < 1000; i++) Assert.Equal(a.Next(1000), b.Next(1000));
        var r = new SeededRandom(7);
        var counts = new int[10];
        for (int i = 0; i < 100_000; i++) counts[r.Next(10)]++;
        Assert.All(counts, c => Assert.InRange(c, 9_500, 10_500));
    }

    [Fact]
    public void Secure_random_stays_in_range()
    {
        var r = new SecureRandomSource();
        for (int i = 0; i < 10_000; i++) Assert.InRange(r.Next(7), 0, 6);
    }
}

public class PackTests
{
    private static readonly GameCatalog Cat = TestCatalog.Get();

    [Fact]
    public void Every_pack_yields_exactly_five_cards_from_pool()
    {
        var rng = new SeededRandom(1);
        foreach (var p in Cat.PackTypes)
        {
            var pool = PackRoller.Pool(Cat, p).Select(f => f.Id).ToHashSet();
            for (int i = 0; i < 500; i++)
            {
                var cards = PackRoller.Roll(Cat, p, rng);
                Assert.Equal(5, cards.Count);
                Assert.Equal(new[] { 1, 2, 3, 4, 5 }, cards.Select(c => c.Slot));
                Assert.All(cards, c => Assert.Contains(c.FormId, pool));
            }
        }
    }

    [Fact]
    public void Basic_pack_pool_is_stage1_and_standalone_only()
    {
        var pool = PackRoller.Pool(Cat, Cat.PackType("basic_pack")!);
        Assert.All(pool, f => Assert.True(f.Stage == 1));
        Assert.Contains(pool, f => f.Id == "pet_halowl");
        Assert.DoesNotContain(pool, f => f.Id == "pet_cinderlynx");
    }

    [Fact]
    public void Final_slot_uses_its_own_table()
    {
        // premium final slot has N weight 0: slot 5 must never be N, while slots 1-4 sometimes are
        var p = Cat.PackType("premium_pack")!;
        var rng = new SeededRandom(99);
        int nInSlots1To4 = 0;
        for (int i = 0; i < 5000; i++)
        {
            var cards = PackRoller.Roll(Cat, p, rng);
            Assert.NotEqual("N", cards[4].Rarity);
            nInSlots1To4 += cards.Take(4).Count(c => c.Rarity == "N");
        }
        Assert.True(nInSlots1To4 > 0);
    }

    [Fact]
    public void Observed_rarity_rates_match_configured_odds()
    {
        var p = Cat.PackType("basic_pack")!;
        var rng = new SeededRandom(2026);
        const int n = 200_000;
        var slot = new Dictionary<string, int>(); var last = new Dictionary<string, int>();
        for (int i = 0; i < n; i++)
        {
            var cards = PackRoller.Roll(Cat, p, rng);
            slot[cards[0].Rarity] = slot.GetValueOrDefault(cards[0].Rarity) + 1;
            last[cards[4].Rarity] = last.GetValueOrDefault(cards[4].Rarity) + 1;
        }
        foreach (var r in new[] { "N", "R", "SR", "SSR" })
        {
            Assert.InRange(slot.GetValueOrDefault(r) / (double)n, p.SlotTable[r] / 1e6 * 0.95, p.SlotTable[r] / 1e6 * 1.05);
            Assert.InRange(last.GetValueOrDefault(r) / (double)n, p.FinalSlotTable[r] / 1e6 * 0.95, p.FinalSlotTable[r] / 1e6 * 1.05);
        }
    }
}

public class StatAndProgressTests
{
    private static readonly GameCatalog Cat = TestCatalog.Get();

    [Fact]
    public void Stats_scale_with_level_rarity_quality()
    {
        var baseS = StatCalculator.Compute(Cat, "pet_emberkit", 1, "N", "B");
        Assert.Equal(420, baseS.Hp);
        Assert.Equal(68, baseS.Atk);
        var lvl = StatCalculator.Compute(Cat, "pet_emberkit", 11, "N", "B");
        Assert.Equal((int)Math.Round(420 * 1.4), lvl.Hp);
        var rare = StatCalculator.Compute(Cat, "pet_emberkit", 1, "Secret", "SSS");
        Assert.True(rare.Hp > baseS.Hp && rare.Power > baseS.Power);
    }

    [Fact]
    public void Exp_levels_up_and_caps()
    {
        var (lv, exp) = StatCalculator.AddExp(1, 0, StatCalculator.ExpToNext(1) + 5);
        Assert.Equal(2, lv); Assert.Equal(5, exp);
        var (max, e2) = StatCalculator.AddExp(49, 0, 1_000_000);
        Assert.Equal(50, max); Assert.Equal(0, e2);
    }

    [Theory]
    [InlineData("pet_emberkit", 15, 5, 500, false, EvolutionCheck.Ok)]
    [InlineData("pet_emberkit", 14, 5, 500, false, EvolutionCheck.LevelTooLow)]
    [InlineData("pet_emberkit", 15, 4, 500, false, EvolutionCheck.NotEnoughEssence)]
    [InlineData("pet_emberkit", 15, 5, 499, false, EvolutionCheck.NotEnoughBeans)]
    [InlineData("pet_emberkit", 15, 5, 500, true, EvolutionCheck.CardLocked)]
    [InlineData("pet_cinderlynx", 30, 15, 2000, false, EvolutionCheck.Ok)]
    [InlineData("pet_pyrelynx", 50, 99, 99999, false, EvolutionCheck.FinalForm)]
    [InlineData("pet_gloomoth", 50, 99, 99999, false, EvolutionCheck.FinalForm)]
    public void Evolution_requirements(string form, int lvl, long ess, long beans, bool busy, EvolutionCheck expected) =>
        Assert.Equal(expected, EvolutionRules.Check(Cat, form, lvl, ess, beans, busy));

    [Fact]
    public void Deck_rules()
    {
        DeckCardInfo C(long id, string f, int pos) => new() { CardId = id, FormId = f, Position = pos };
        var ok = new List<DeckCardInfo> { C(1, "pet_emberkit", 0), C(2, "pet_puddlotl", 1), C(3, "pet_zipmunk", 2), C(4, "pet_halowl", 3), C(5, "pet_gloomoth", 4) };
        Assert.Null(DeckRules.Validate(Cat, ok));
        Assert.Equal("deck_wrong_size", DeckRules.Validate(Cat, ok.Take(4).ToList()));
        var dup = ok.Select(x => x).ToList(); dup[4] = C(1, "pet_emberkit", 4);
        Assert.Equal("deck_duplicate_card", DeckRules.Validate(Cat, dup));
        var pos = ok.ToList(); pos[4] = C(5, "pet_gloomoth", 0);
        Assert.Equal("deck_invalid_positions", DeckRules.Validate(Cat, pos));
        var fam = new List<DeckCardInfo> { C(1, "pet_emberkit", 0), C(2, "pet_cinderlynx", 1), C(3, "pet_pyrelynx", 2), C(4, "pet_halowl", 3), C(5, "pet_gloomoth", 4) };
        Assert.Equal("deck_too_many_same_family", DeckRules.Validate(Cat, fam));
    }
}

public class BattleTests
{
    private static readonly GameCatalog Cat = TestCatalog.Get();

    private static BattleState NewBattle(ulong seed = 1, string[]? a = null, string[]? b = null, int levelA = 10, int levelB = 10)
    {
        a ??= new[] { "pet_puddlotl", "pet_emberkit", "pet_zipmunk", "pet_halowl", "pet_gloomoth" };
        b ??= a;
        var ua = a.Select((f, i) => BattleEngine.CreateUnit(Cat, 0, i, i, i + 1, f, levelA, "N", "B")).ToList();
        var ub = b.Select((f, i) => BattleEngine.CreateUnit(Cat, 1, i, i, 0, f, levelB, "N", "B")).ToList();
        return new BattleEngine(Cat).Start(ua, ub, seed);
    }

    [Fact]
    public void Energy_starts_at_three_gains_three_caps_at_ten()
    {
        var st = NewBattle();
        var e = new BattleEngine(Cat);
        Assert.Equal(new[] { 3, 3 }, st.Energy);
        e.ResolveRound(st, new List<PlannedAction>(), new List<PlannedAction>());
        Assert.Equal(6, st.Energy[0]);
        e.ResolveRound(st, Defend(st, 0), Defend(st, 1));
        e.ResolveRound(st, Defend(st, 0), Defend(st, 1));
        Assert.Equal(10, st.Energy[0]);
    }

    private static List<PlannedAction> Defend(BattleState st, int side) =>
        st.Side(side).Where(u => u.Alive && !u.Has("stun")).Select(u => new PlannedAction { UnitIndex = u.Index, Type = ActionType.Defend }).ToList();

    [Fact]
    public void Plan_exceeding_energy_is_rejected()
    {
        var st = NewBattle();
        var plan = new List<PlannedAction>
        {
            new() { UnitIndex = 3, Type = ActionType.Ultimate }, // halowl ult costs 4 > 3
        };
        var ex = Assert.Throws<BattleRuleException>(() => new BattleEngine(Cat).ResolveRound(st, plan, null));
        Assert.Equal("not_enough_energy", ex.Code);
    }

    [Fact]
    public void Unit_cannot_act_twice_or_use_missing_skill()
    {
        var st = NewBattle();
        var e = new BattleEngine(Cat);
        Assert.Equal("duplicate_action", Assert.Throws<BattleRuleException>(() => e.ResolveRound(st,
            new List<PlannedAction> { new() { UnitIndex = 0 }, new() { UnitIndex = 0 } }, null)).Code);
        Assert.Equal("no_such_skill", Assert.Throws<BattleRuleException>(() => e.ResolveRound(st,
            new List<PlannedAction> { new() { UnitIndex = 0, Type = ActionType.Ultimate } }, null)).Code); // puddlotl has no ultimate
    }

    [Fact]
    public void Backline_is_protected_while_frontline_lives()
    {
        var st = NewBattle();
        var e = new BattleEngine(Cat);
        var plan = new List<PlannedAction> { new() { UnitIndex = 1, Type = ActionType.Basic, TargetIndex = 4 } };
        var events = e.ResolveRound(st, plan, Defend(st, 1));
        var hit = events.First(ev => ev.Type == "damage" && ev.ActorSide == 0 && ev.ActorIndex == 1);
        Assert.True(hit.TargetIndex < 3, "attack on backline must be redirected to frontline");
    }

    [Fact]
    public void Battle_is_deterministic_for_same_seed_and_plans()
    {
        string Run(ulong seed)
        {
            var st = NewBattle(seed);
            var e = new BattleEngine(Cat); var ai = new BattleAi(Cat);
            var log = new List<string>();
            while (!st.Finished)
                foreach (var ev in e.ResolveRound(st, ai.Plan(st, 0, 2), ai.Plan(st, 1, 2)))
                    log.Add($"{ev.Type}:{ev.ActorSide}{ev.ActorIndex}>{ev.TargetSide}{ev.TargetIndex}={ev.Amount}");
            return string.Join("|", log) + "#" + st.Winner;
        }
        Assert.Equal(Run(12345), Run(12345));
    }

    [Fact]
    public void Ai_battles_always_terminate_with_legal_plans()
    {
        var ai = new BattleAi(Cat); var e = new BattleEngine(Cat);
        for (ulong seed = 1; seed <= 200; seed++)
        {
            var st = NewBattle(seed, levelA: 5 + (int)(seed % 10), levelB: 8);
            int guard = 0;
            while (!st.Finished && guard++ < 100) e.ResolveRound(st, ai.Plan(st, 0, (int)(seed % 3)), ai.Plan(st, 1, 2));
            Assert.True(st.Finished);
            Assert.InRange(st.Round, 1, st.Config.MaxRounds);
            Assert.NotNull(st.Winner);
        }
    }

    [Fact]
    public void Stronger_team_wins_most_battles()
    {
        var ai = new BattleAi(Cat); var e = new BattleEngine(Cat);
        int wins = 0;
        for (ulong seed = 1; seed <= 100; seed++)
        {
            var st = NewBattle(seed, levelA: 25, levelB: 5);
            while (!st.Finished) e.ResolveRound(st, ai.Plan(st, 0, 2), ai.Plan(st, 1, 2));
            if (st.Winner == 0) wins++;
        }
        Assert.True(wins >= 95, $"strong team won only {wins}/100");
    }

    [Fact]
    public void Damage_respects_element_multiplier_and_defend()
    {
        // water attacker vs fire defender (1.5x) and vs defending target (0.5x)
        var a = new[] { "pet_puddlotl", "pet_puddlotl", "pet_puddlotl", "pet_halowl", "pet_halowl" };
        var b = new[] { "pet_emberkit", "pet_zipmunk", "pet_zipmunk", "pet_halowl", "pet_halowl" };
        var st = NewBattle(5, a, b);
        foreach (var u in st.Units) u.Crit = 0;
        var e = new BattleEngine(Cat);
        var ev = e.ResolveRound(st, new List<PlannedAction> { new() { UnitIndex = 0, TargetIndex = 0 }, new() { UnitIndex = 1, TargetIndex = 1 } },
            new List<PlannedAction> { new() { UnitIndex = 1, Type = ActionType.Defend } });
        var vsFire = ev.First(x => x.Type == "damage" && x.ActorSide == 0 && x.ActorIndex == 0);
        var vsDefend = ev.First(x => x.Type == "damage" && x.ActorSide == 0 && x.ActorIndex == 1);
        Assert.Equal(1.5, vsFire.ElementMultiplier);
        Assert.True(vsFire.Amount > vsDefend.Amount * 2);
    }

    [Fact]
    public void Stunned_unit_cannot_be_ordered_to_act()
    {
        var st = NewBattle();
        st.Unit(0, 0).Statuses.Add(new StatusInstance { Type = "stun", Remaining = 1 });
        var ex = Assert.Throws<BattleRuleException>(() => new BattleEngine(Cat).ResolveRound(st, new List<PlannedAction> { new() { UnitIndex = 0 } }, null));
        Assert.Equal("unit_stunned", ex.Code);
    }

    [Fact]
    public void Taunt_forces_single_target_attacks()
    {
        var st = NewBattle();
        st.Unit(1, 2).Statuses.Add(new StatusInstance { Type = "taunt", Remaining = 2 });
        var legal = BattleEngine.LegalEnemyTargets(st, 0);
        Assert.Single(legal);
        Assert.Equal(2, legal[0].Index);
    }

    [Fact]
    public void Round_limit_tiebreak_by_survivors_then_hp()
    {
        var st = NewBattle();
        st.Config.MaxRounds = 1;
        st.Unit(1, 4).Hp = 0;
        var events = new BattleEngine(Cat).ResolveRound(st, Defend(st, 0), Defend(st, 1));
        Assert.True(st.Finished);
        Assert.Equal(0, st.Winner);
        Assert.Equal("round_limit_survivors", st.EndReason);
    }

    [Fact]
    public void Knockout_ends_battle_and_further_rounds_are_rejected()
    {
        var st = NewBattle();
        foreach (var u in st.Side(1)) u.Hp = 1;
        foreach (var u in st.Side(1)) u.Def = 0;
        var e = new BattleEngine(Cat);
        e.ResolveRound(st, null, Defend(st, 1).Take(0).ToList());
        // 5 attackers can only defeat front-line first; play until finished
        while (!st.Finished) e.ResolveRound(st, null, null);
        Assert.Equal(0, st.Winner);
        Assert.Equal("battle_finished", Assert.Throws<BattleRuleException>(() => e.ResolveRound(st, null, null)).Code);
    }

    [Fact]
    public void Forfeit_gives_win_to_other_side()
    {
        var st = NewBattle();
        var ev = new List<BattleEvent>();
        BattleEngine.Forfeit(st, 0, "surrender", ev);
        Assert.Equal(1, st.Winner);
        Assert.Equal("surrender", st.EndReason);
    }
}
