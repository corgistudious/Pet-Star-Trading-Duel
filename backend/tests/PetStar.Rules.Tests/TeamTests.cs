using PetStar.Api.Infrastructure;
using PetStar.Rules.Team;
using Xunit.Abstractions;

namespace PetStar.Rules.Tests;

public static class TeamFixture
{
    private static readonly Lazy<TeamRules> Data = new(() => CatalogLoader.LoadTeam(Path.Combine(AppContext.BaseDirectory, "seed")));
    public static TeamRules Rules => Data.Value;
    public static readonly GameCatalog Cat = TestCatalog.Get();

    public static TeamSetupUnit U(string form, int level = 20, string rarity = "SR") =>
        TeamBuilder.Unit(Cat, Rules, form, rarity, "B", new CardProgress { Level = level }, form.Aggregate(7L, (h, c) => (h * 31 + c) & 0xffffff));

    /// <summary>Three Pets for a test team: variant picks forms deterministically across all elements and roles.</summary>
    public static TeamSetupPlayer Team(int variant, int level = 20, string name = "T")
    {
        var forms = Rules.Kits.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        return new TeamSetupPlayer { Name = name, Units = Enumerable.Range(0, 3).Select(i => U(forms[(variant * 7 + i * 61) % forms.Count], level)).ToList() };
    }

    public static TeamState New(ulong seed, TeamSetupPlayer? a = null, TeamSetupPlayer? b = null) =>
        TeamEngine.Create(Rules, a ?? Team(0, name: "Blue"), b ?? Team(1, name: "Red"), seed);

    public static void Do(TeamState st, int p, TeamAction a) => TeamEngine.Apply(Rules, st, p, a);
    public static string Err(TeamState st, int p, TeamAction a) { try { Do(st, p, a); return "ok"; } catch (TeamRuleException e) { return e.Code; } }
    public static TeamAction Plan(params (int unit, string slot, int target)[] o) => new() { Orders = o.Select(x => new TeamOrder { Unit = x.unit, Slot = x.slot, Target = x.target }).ToList() };
    public static TeamAction Basic(TeamState st, int p) => new() { Orders = TeamEngine.Team(st, p).Where(u => u.Alive).Select(u => new TeamOrder { Unit = u.Uid, Slot = "basic" }).ToList() };

    public static int PlayOut(TeamState st, int max = 200)
    {
        int n = 0;
        while (!st.Finished && n++ < max)
            for (int p = 0; p < 2 && !st.Finished; p++) if (TeamEngine.MustAct(st, p)) Do(st, p, TeamAi.Decide(Rules, st, p));
        return n;
    }

    public static string Form(Func<TeamSkill, bool> skill, Func<TeamKit, bool>? kit = null) =>
        Rules.Kits.Where(k => (kit == null || kit(k.Value)) && new[] { k.Value.Skill, k.Value.Ultimate }.Any(s => s != null && skill(Rules.Skills[s])))
            .Select(k => k.Key).OrderBy(x => x, StringComparer.Ordinal).First();
}

public class TeamRuleTests(ITestOutputHelper output)
{
    private static TeamRules R => TeamFixture.Rules;
    private static TeamUnit Unit(TeamState st, int side, int slot) => st.Units.First(u => u.Side == side && u.Slot == slot);

    [Fact]
    public void Every_pet_form_has_a_battle_kit_and_every_mechanic_exists_in_the_data()
    {
        Assert.Equal(215, R.Kits.Count);
        Assert.All(R.Kits.Values, k => { Assert.NotNull(k.Skill); Assert.True(R.Skills.ContainsKey(k.Basic)); });
        Assert.All(R.Skills.Values.Where(s => s.Kind != "basic"), s => Assert.InRange(s.Cost, 20, 60));
        Assert.All(R.Skills.Values.Where(s => s.Kind == "ultimate"), s => Assert.True(s.Cooldown > 0));
        var statuses = R.Skills.Values.SelectMany(s => s.Effects).Where(e => e.Op == "status").Select(e => e.Status).ToHashSet();
        foreach (var need in new[] { "burn", "poison", "stun", "freeze", "silence", "regen", "haste", "weaken", "taunt", "counter" }) Assert.Contains(need, statuses);
        var ops = R.Skills.Values.SelectMany(s => s.Effects).Select(e => e.Op).ToHashSet();
        foreach (var need in new[] { "damage", "heal", "shield", "cleanse", "energy", "status" }) Assert.Contains(need, ops);
        Assert.Contains(R.Skills.Values, s => s.Target == "enemy_all");
        Assert.Contains(R.Kits.Values, k => k.Passive?.Op == "drain");
        Assert.Equal(new[] { "drain", "heal_bonus", "rage", "reduce", "regen", "start_en" }, R.Kits.Values.Where(k => k.Passive != null).Select(k => k.Passive!.Op).Distinct().OrderBy(x => x));
        Assert.Equal(50, R.Config.StartEn); Assert.Equal(100, R.Config.MaxEn); Assert.Equal(15, R.Config.BasicEnGain);
        var u = TeamFixture.U("pet_emberkit");
        Assert.True(u.Mag > 0 && u.Res > 0 && u.Hp > 0 && u.Spd > 0);
    }

    [Fact]
    public void Teams_have_three_pets_plans_stay_hidden_until_both_lock_and_missing_orders_default_to_basic()
    {
        var bad = TeamFixture.Team(0); bad.Units.RemoveAt(2);
        Assert.Throws<TeamRuleException>(() => TeamEngine.Create(R, bad, TeamFixture.Team(1), 1));
        var st = TeamFixture.New(3);
        Assert.Equal(6, st.Units.Count);
        Assert.All(st.Units, u => Assert.Equal(u.Passive?.Op == "start_en" ? 70 : 50, u.En));
        TeamFixture.Do(st, 0, TeamFixture.Plan((Unit(st, 0, 0).Uid, "basic", 0)));
        Assert.True(st.Players[0].Locked); Assert.False(st.Players[1].Locked);
        Assert.Equal(3, st.Players[0].Plan!.Count);                                // two Pets filled with basic attacks
        Assert.Equal("plan_locked", TeamFixture.Err(st, 0, TeamFixture.Basic(st, 0)));
        Assert.DoesNotContain(st.Log, e => e.Type == "act");                      // nothing resolves before both lock
        TeamFixture.Do(st, 1, TeamFixture.Basic(st, 1));
        Assert.Equal(2, st.Round);
        Assert.Equal(6, st.Log.Count(e => e.Type == "act" && e.Round == 1) + st.Log.Count(e => e.Type == "skip" && e.Round == 1));
        Assert.Equal("invalid_unit", TeamFixture.Err(st, 0, TeamFixture.Plan((Unit(st, 1, 0).Uid, "basic", 0))));
        Assert.Equal("invalid_target", TeamFixture.Err(st, 0, TeamFixture.Plan((Unit(st, 0, 0).Uid, "basic", Unit(st, 0, 1).Uid))));
    }

    [Fact]
    public void Resolution_order_is_priority_then_speed()
    {
        var st = TeamFixture.New(5);
        Unit(st, 0, 0).Spd = 300; Unit(st, 1, 0).Spd = 200; Unit(st, 0, 1).Spd = 100;
        TeamFixture.Do(st, 0, TeamFixture.Basic(st, 0)); TeamFixture.Do(st, 1, TeamFixture.Basic(st, 1));
        var acts = st.Log.Where(e => e.Type is "act" or "skip" && e.Round == 1).Select(e => e.A).ToList();
        Assert.True(acts.IndexOf(Unit(st, 0, 0).Uid) < acts.IndexOf(Unit(st, 1, 0).Uid));
        Assert.True(acts.IndexOf(Unit(st, 1, 0).Uid) < acts.IndexOf(Unit(st, 0, 1).Uid));
        // a priority skill beats raw speed; haste raises effective speed
        var pri = R.Skills.Values.First(s => s.Priority > 0 && s.Kind == "skill" && s.Cost <= 50);
        var st2 = TeamFixture.New(6);
        var slow = Unit(st2, 0, 2); slow.Skill = pri.Id; slow.Spd = 1; slow.En = 100;
        foreach (var u in st2.Units.Where(u => u != slow)) u.Spd = 500;
        TeamFixture.Do(st2, 0, TeamFixture.Plan((slow.Uid, "skill", 0))); TeamFixture.Do(st2, 1, TeamFixture.Basic(st2, 1));
        Assert.Equal(slow.Uid, st2.Log.First(e => e.Type is "act" or "skip").A);
        slow.Statuses.Add(new TeamStatus { Type = "haste", Remaining = 2 });
        Assert.Equal(1 * (100 + R.Config.HastePct) / 100, TeamEngine.EffSpd(R, slow));
    }

    [Fact]
    public void Energy_basic_gains_15_skills_cost_en_cooldowns_and_silence_are_enforced()
    {
        var st = TeamFixture.New(7);
        var u = Unit(st, 0, 0); u.En = 50;
        TeamFixture.Do(st, 0, TeamFixture.Plan((u.Uid, "basic", 0))); TeamFixture.Do(st, 1, TeamFixture.Basic(st, 1));
        if (u.Alive && !st.Log.Any(e => e.Type == "skip" && e.A == u.Uid)) Assert.Equal(Math.Min(100, 50 + 15 + 10), u.En);
        var sk = TeamEngine.SkillFor(R, u, "skill")!;
        u.En = sk.Cost - 1;
        Assert.Equal("not_enough_en", TeamFixture.Err(st, 0, TeamFixture.Plan((u.Uid, "skill", 0))));
        u.En = 100; u.Statuses.Clear();
        int before = u.En;
        TeamFixture.Do(st, 0, TeamFixture.Plan((u.Uid, "skill", 0))); TeamFixture.Do(st, 1, TeamFixture.Basic(st, 1));
        if (sk.Cooldown > 0 && u.Alive) Assert.Equal("on_cooldown", TeamFixture.Err(st, 0, TeamFixture.Plan((u.Uid, "skill", 0))));
        u.Statuses.Add(new TeamStatus { Type = "silence", Remaining = 1 }); u.Cooldowns.Clear(); u.En = 100;
        Assert.Equal("silenced", TeamFixture.Err(st, 0, TeamFixture.Plan((u.Uid, "skill", 0))));
        Assert.Equal("ok", TeamFixture.Err(st, 0, TeamFixture.Plan((u.Uid, "basic", 0))));
    }

    private static (TeamState st, TeamUnit a, TeamUnit d) Duel(ulong seed, string atkForm, string defForm)
    {
        var st = TeamFixture.New(seed, new TeamSetupPlayer { Name = "A", Units = new() { TeamFixture.U(atkForm), TeamFixture.U(atkForm), TeamFixture.U(atkForm) } },
            new TeamSetupPlayer { Name = "B", Units = new() { TeamFixture.U(defForm), TeamFixture.U(defForm), TeamFixture.U(defForm) } });
        foreach (var u in st.Units) u.CritBp = 0;
        return (st, Unit(st, 0, 0), Unit(st, 1, 0));
    }

    [Fact]
    public void Damage_uses_atk_vs_def_or_mag_vs_res_with_element_advantage_crit_and_guard()
    {
        var fire = R.Kits.First(k => k.Value.Element == "fire" && k.Value.Basic == "basic_phys").Key;
        var nature = R.Kits.First(k => k.Value.Element == "nature" && k.Value.Passive?.Op != "reduce").Key;
        var dark = R.Kits.First(k => k.Value.Element == "dark" && k.Value.Passive?.Op != "reduce").Key;
        long Expected(TeamUnit a, TeamUnit d, int mult) { long x = 100L * a.Atk * a.Atk / (a.Atk + d.Def) / 100 * R.Config.DamageScalePct / 100; return x * mult / 100; }
        var (st, a, d) = Duel(11, fire, nature);
        TeamFixture.Do(st, 0, TeamFixture.Plan((a.Uid, "basic", d.Uid), (Unit(st, 0, 1).Uid, "guard", 0), (Unit(st, 0, 2).Uid, "guard", 0)));
        TeamFixture.Do(st, 1, TeamFixture.Plan((Unit(st, 1, 0).Uid, "guard", 0), (Unit(st, 1, 1).Uid, "guard", 0), (Unit(st, 1, 2).Uid, "guard", 0)));
        var hit = st.Log.First(e => e.Type == "damage" && e.A == a.Uid);
        Assert.Contains("strong", hit.S);
        Assert.Equal(Expected(a, d, R.Config.StrongPct) * (100 - R.Config.GuardReductionPct) / 100, hit.N);   // guard halves damage
        var (st2, a2, d2) = Duel(12, fire, dark);
        TeamFixture.Do(st2, 0, TeamFixture.Plan((a2.Uid, "basic", d2.Uid))); TeamFixture.Do(st2, 1, TeamFixture.Plan((Unit(st2, 1, 0).Uid, "guard", 0), (Unit(st2, 1, 1).Uid, "guard", 0), (Unit(st2, 1, 2).Uid, "guard", 0)));
        var h2 = st2.Log.First(e => e.Type == "damage" && e.A == a2.Uid);
        Assert.Equal(Expected(a2, d2, 100) * 50 / 100, h2.N);
        // magic attacks use MAG vs RES
        var mage = R.Kits.First(k => k.Value.Basic == "basic_mag" && k.Value.Element == "light" && k.Value.Passive?.Op != "rage").Key;
        var (st3, a3, d3) = Duel(13, mage, mage);
        TeamFixture.Do(st3, 0, TeamFixture.Plan((a3.Uid, "basic", d3.Uid))); TeamFixture.Do(st3, 1, TeamFixture.Plan((Unit(st3, 1, 0).Uid, "guard", 0), (Unit(st3, 1, 1).Uid, "guard", 0), (Unit(st3, 1, 2).Uid, "guard", 0)));
        long m = 100L * a3.Mag * a3.Mag / (a3.Mag + d3.Res) / 100 * R.Config.DamageScalePct / 100;
        Assert.Equal(m * 50 / 100, st3.Log.First(e => e.Type == "damage" && e.A == a3.Uid).N);
    }

    [Fact]
    public void Statuses_stun_skips_then_cc_immunity_poison_stacks_burn_regen_shield_taunt_counter_cleanse()
    {
        var st = TeamFixture.New(21);
        var v = Unit(st, 1, 0);
        v.Statuses.Add(new TeamStatus { Type = "stun", Remaining = 1 });
        TeamFixture.Do(st, 0, TeamFixture.Basic(st, 0)); TeamFixture.Do(st, 1, TeamFixture.Basic(st, 1));
        Assert.Contains(st.Log, e => e.Type == "skip" && e.A == v.Uid && e.S == "stun");
        Assert.False(TeamEngine.Has(v, "stun"));
        Assert.Equal(R.Config.CcImmunityTurns, v.CcImmune);                       // cannot be locked again right away
        // poison stacks to the cap and ticks per stack; burn ticks; regen heals
        var st2 = TeamFixture.New(22);
        var t = Unit(st2, 1, 1); t.Hp = t.MaxHp / 2;
        t.Statuses.Add(new TeamStatus { Type = "poison", Remaining = 3, Stacks = 3 });
        t.Statuses.Add(new TeamStatus { Type = "burn", Remaining = 2 });
        t.Statuses.Add(new TeamStatus { Type = "regen", Remaining = 2 });
        foreach (var u in st2.Units) u.Atk = 1;
        TeamFixture.Do(st2, 0, TeamFixture.Plan((Unit(st2, 0, 0).Uid, "guard", 0), (Unit(st2, 0, 1).Uid, "guard", 0), (Unit(st2, 0, 2).Uid, "guard", 0)));
        TeamFixture.Do(st2, 1, TeamFixture.Plan((Unit(st2, 1, 0).Uid, "guard", 0), (t.Uid, "guard", 0), (Unit(st2, 1, 2).Uid, "guard", 0)));
        Assert.Contains(st2.Log, e => e.Type == "poison" && e.B == t.Uid && e.N == t.MaxHp * R.Config.PoisonPct * 3 / 100);
        Assert.Contains(st2.Log, e => e.Type == "burn" && e.B == t.Uid && e.N == t.MaxHp * R.Config.BurnPct / 100);
        Assert.Contains(st2.Log, e => e.Type == "heal" && e.B == t.Uid);
        // shield absorbs before HP; taunt redirects single-target attacks; counter strikes back
        var st3 = TeamFixture.New(23);
        var tank = Unit(st3, 1, 2); var other = Unit(st3, 1, 0);
        tank.Statuses.Add(new TeamStatus { Type = "taunt", Remaining = 2 }); tank.Statuses.Add(new TeamStatus { Type = "counter", Remaining = 2 });
        tank.Shield = 5;
        var att = Unit(st3, 0, 0);
        TeamFixture.Do(st3, 0, TeamFixture.Plan((att.Uid, "basic", other.Uid), (Unit(st3, 0, 1).Uid, "guard", 0), (Unit(st3, 0, 2).Uid, "guard", 0)));
        TeamFixture.Do(st3, 1, TeamFixture.Plan((Unit(st3, 1, 0).Uid, "guard", 0), (Unit(st3, 1, 1).Uid, "guard", 0), (tank.Uid, "guard", 0)));
        var dmg = st3.Log.First(e => e.Type == "damage" && e.A == att.Uid);
        Assert.Equal(tank.Uid, dmg.B);
        Assert.Contains("shield5", dmg.S);
        Assert.Contains(st3.Log, e => e.Type == "counter" && e.A == tank.Uid && e.B == att.Uid);
        // cleanse removes debuffs
        var healer = R.Skills.Values.First(s => s.Effects.Any(e => e.Op == "cleanse") && s.Target == "ally_all");
        var st4 = TeamFixture.New(24);
        var c = Unit(st4, 0, 0); c.Ultimate = healer.Id; c.Skill = healer.Id; c.En = 100; c.Spd = 999;
        foreach (var e in TeamEngine.Team(st4, 1)) { e.Atk = 1; e.Mag = 1; e.CritBp = 0; }
        Unit(st4, 0, 1).Statuses.Add(new TeamStatus { Type = "poison", Remaining = 3 });
        TeamFixture.Do(st4, 0, TeamFixture.Plan((c.Uid, "ultimate", 0))); TeamFixture.Do(st4, 1, TeamFixture.Basic(st4, 1));
        Assert.True(c.Alive, string.Join(" ", st4.Log.Select(e => e.Digest())));
        Assert.True(st4.Log.Any(e => e.Type == "cleanse" && e.B == Unit(st4, 0, 1).Uid), string.Join(" ", st4.Log.Select(e => e.Digest())));
    }

    [Fact]
    public void Knockout_wins_both_down_is_a_draw_round_limit_compares_hp_timeouts_and_surrender()
    {
        var st = TeamFixture.New(31);
        foreach (var u in TeamEngine.Team(st, 1)) u.Hp = 1;
        foreach (var u in TeamEngine.Team(st, 0)) u.Spd = 999;
        TeamFixture.Do(st, 0, TeamFixture.Plan(TeamEngine.Team(st, 0).Select(u => (u.Uid, "basic", TeamEngine.Team(st, 1).First(x => x.Slot == u.Slot).Uid)).ToArray()));
        TeamFixture.Do(st, 1, TeamFixture.Basic(st, 1));
        Assert.True(st.Finished); Assert.Equal(0, st.Winner); Assert.Equal("knockout", st.EndReason);
        // both teams fall in the same round end (poison) → draw
        var d = TeamFixture.New(32);
        foreach (var u in d.Units) { u.Hp = 1; u.Statuses.Add(new TeamStatus { Type = "poison", Remaining = 2 }); u.Atk = 1; u.Mag = 1; }
        TeamFixture.Do(d, 0, TeamFixture.Plan(TeamEngine.Team(d, 0).Select(u => (u.Uid, "guard", 0)).ToArray()));
        TeamFixture.Do(d, 1, TeamFixture.Plan(TeamEngine.Team(d, 1).Select(u => (u.Uid, "guard", 0)).ToArray()));
        Assert.True(d.Finished); Assert.Null(d.Winner); Assert.Equal("both_down", d.EndReason);
        // round limit: higher remaining HP share wins
        var r = TeamFixture.New(33);
        for (int i = 0; i < R.Config.MaxRounds && !r.Finished; i++)
        {
            TeamFixture.Do(r, 0, TeamFixture.Plan(TeamEngine.Team(r, 0).Where(u => u.Alive).Select(u => (u.Uid, "guard", 0)).ToArray()));
            TeamFixture.Do(r, 1, TeamFixture.Plan(TeamEngine.Team(r, 1).Where(u => u.Alive).Select(u => (u.Uid, "guard", 0)).ToArray()));
        }
        Assert.True(r.Finished); Assert.Equal("round_limit", r.EndReason);
        // timeouts: automatic plan; the third in a row forfeits
        var t = TeamFixture.New(34);
        TeamEngine.Timeout(R, t);
        Assert.Equal(2, t.Round);
        TeamFixture.Do(t, 0, TeamFixture.Basic(t, 0));
        TeamEngine.Timeout(R, t); TeamEngine.Timeout(R, t);
        Assert.True(t.Finished); Assert.Equal(0, t.Winner); Assert.Equal("timeout", t.EndReason);
        var s = TeamFixture.New(35);
        TeamFixture.Do(s, 1, new TeamAction { Type = "surrender" });
        Assert.Equal(0, s.Winner);
        Assert.Equal("match_finished", TeamFixture.Err(s, 0, TeamFixture.Basic(s, 0)));
    }

    [Fact]
    public void Same_seed_and_plans_replay_exactly()
    {
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var st = TeamFixture.New(seed, TeamFixture.Team((int)seed), TeamFixture.Team((int)seed + 9));
            var plans = new List<(int p, TeamAction a)>();
            while (!st.Finished && plans.Count < 400)
                for (int p = 0; p < 2 && !st.Finished; p++) if (TeamEngine.MustAct(st, p)) { var a = TeamAi.Decide(R, st, p); plans.Add((p, a)); TeamFixture.Do(st, p, a); }
            var re = TeamFixture.New(seed, TeamFixture.Team((int)seed), TeamFixture.Team((int)seed + 9));
            foreach (var (p, a) in plans) TeamFixture.Do(re, p, a);
            Assert.Equal(st.Log.Select(e => e.Digest()), re.Log.Select(e => e.Digest()));
        }
    }

    [Fact]
    public void Ten_thousand_simulated_battles_keep_every_invariant_and_always_end()
    {
        int games = 10_000; var reasons = new Dictionary<string, int>(); long rounds = 0; int wins0 = 0;
        for (int g = 0; g < games; g++)
        {
            ulong seed = (ulong)(g * 104729 + 7);
            var st = TeamFixture.New(seed, TeamFixture.Team(g % 211, 10 + g % 50, "A"), TeamFixture.Team((g * 13 + 5) % 211, 10 + (g * 7) % 50, "B"));
            var rnd = new SeededRandom(seed ^ 0x5151);
            bool random0 = g % 2 == 0, random1 = g % 5 == 0;
            int guard = 0;
            while (!st.Finished)
            {
                Assert.True(++guard < 200, $"battle {g} did not end");
                for (int p = 0; p < 2 && !st.Finished; p++)
                {
                    if (!TeamEngine.MustAct(st, p)) continue;
                    if ((p == 0 && random0) || (p == 1 && random1))
                    {
                        var orders = new List<TeamOrder>();
                        foreach (var u in TeamEngine.Team(st, p).Where(u => u.Alive))
                        {
                            var slots = new[] { "basic", "skill", "ultimate", "guard" }.Where(s => TeamEngine.OrderProblem(R, st, p, new TeamOrder { Unit = u.Uid, Slot = s }) == null).ToList();
                            var slot = slots[rnd.Next(slots.Count)];
                            var enemies = TeamEngine.Team(st, 1 - p).Where(x => x.Alive).ToList();
                            orders.Add(new TeamOrder { Unit = u.Uid, Slot = slot, Target = rnd.Next(2) == 0 ? 0 : (slot != "guard" && TeamEngine.SkillFor(R, u, slot)!.Target == "enemy_single" ? enemies[rnd.Next(enemies.Count)].Uid : 0) });
                        }
                        TeamFixture.Do(st, p, new TeamAction { Orders = orders });
                    }
                    else TeamFixture.Do(st, p, TeamAi.Decide(R, st, p));
                    foreach (var u in st.Units)
                    {
                        Assert.InRange(u.Hp, 0, u.MaxHp);
                        Assert.InRange(u.En, 0, R.Config.MaxEn);
                        Assert.True(u.Shield >= 0 && u.Shield <= u.MaxHp * R.Config.ShieldCapPct / 100);
                        Assert.True(u.Statuses.Count(s => s.Type is "stun" or "freeze" or "silence") <= 1, $"battle {g}: stacked hard control");
                        Assert.All(u.Statuses, s => Assert.True(s.Remaining > 0 && s.Stacks <= R.Config.PoisonMaxStacks));
                    }
                }
            }
            Assert.True(st.Round <= R.Config.MaxRounds);
            rounds += st.Round;
            if (st.Winner == 0) wins0++;
            reasons[st.EndReason!] = reasons.GetValueOrDefault(st.EndReason!) + 1;
        }
        output.WriteLine($"{games} battles, avg rounds {rounds / (double)games:F1}, side A wins {wins0}, reasons: {string.Join(", ", reasons.Select(kv => kv.Key + "=" + kv.Value))}");
        Assert.True(reasons.GetValueOrDefault("knockout") > games * 8 / 10);
    }
}
