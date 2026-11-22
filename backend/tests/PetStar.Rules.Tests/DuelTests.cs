using PetStar.Api.Infrastructure;
using PetStar.Rules;
using PetStar.Rules.Duel;
using Xunit.Abstractions;

namespace PetStar.Rules.Tests;

public static class DuelFixture
{
    private static readonly Lazy<DuelData> Data = new(() => CatalogLoader.LoadDuel(Path.Combine(AppContext.BaseDirectory, "seed")));
    public static readonly GameCatalog Cat = TestCatalog.Get();
    public static DuelRules Rules => DuelBuilder.Rules(Cat, Data.Value.Config, Data.Value.Tactics);
    public static List<TacticDef> Tactics => Data.Value.Tactics;

    public static DuelCard Pet(string formId, int level = 20, string rarity = "SR", CardProgress? prog = null, bool ranked = false) => new()
    {
        Kind = "pet", CardInstanceId = formId.Aggregate(17L, (h, ch) => (h * 31 + ch) & 0x7fffffff),   // stable across processes (string.GetHashCode is randomized)
        Forms = DuelBuilder.PetForms(Cat, Data.Value.Config, formId, rarity, "B", prog ?? new CardProgress { Level = level }, ranked),
    };

    public static DuelCard Tactic(string id) => new() { Kind = "tactic", TacticId = id };

    /// <summary>40 cards: 16 Pets (stage-1 + a few later forms) and 24 Spells/Traps.</summary>
    public static List<DuelCard> Deck(int variant, int level = 20)
    {
        var stage1 = Cat.Forms.Values.Where(f => f.DivineSet == null && f.Stage == 1 && f.FamilyStages > 1).OrderBy(f => f.Id).Select(f => f.Id).ToList();
        var stage2 = Cat.Forms.Values.Where(f => f.DivineSet == null && f.Stage == 2).OrderBy(f => f.Id).Select(f => f.Id).ToList();
        var deck = new List<DuelCard>();
        for (int i = 0; i < 13; i++) deck.Add(Pet(stage1[(i * 7 + variant * 3) % stage1.Count], level));
        for (int i = 0; i < 3; i++) deck.Add(Pet(stage2[(i * 5 + variant) % stage2.Count], level));
        var t = Tactics.OrderBy(x => x.Id).ToList();
        for (int i = 0; i < 24; i++) deck.Add(Tactic(t[(i + variant) % t.Count].Id));
        return deck;
    }

    public static DuelState New(ulong seed, List<DuelCard>? a = null, List<DuelCard>? b = null) =>
        DuelEngine.Create(Rules, new DuelSetupPlayer { Name = "Blue", Deck = a ?? Deck(0) }, new DuelSetupPlayer { Name = "Red", Deck = b ?? Deck(1) }, seed);

    /// <summary>Moves the first deck card matching <paramref name="pick"/> into the hand (test setup).</summary>
    public static int Give(DuelState st, int p, Func<DuelCard, bool> pick)
    {
        var pl = st.Players[p];
        int id = pl.Deck.First(c => pick(st.Card(c)));
        pl.Deck.Remove(id); pl.Hand.Add(id);
        return id;
    }

    public static int GiveTactic(DuelState st, int p, string tacticId)
    {
        var pl = st.Players[p];
        var id = pl.Deck.FirstOrDefault(c => st.Card(c).TacticId == tacticId);
        if (id == 0)
        {   // not in the random deck: add a copy (test-only)
            st.Cards.Add(new DuelCard { Id = st.Cards.Count + 1, Kind = "tactic", TacticId = tacticId });
            id = st.Cards.Count;
        }
        else pl.Deck.Remove(id);
        pl.Hand.Add(id);
        return id;
    }

    public static void Do(DuelState st, int p, DuelAction a) => DuelEngine.Apply(Rules, st, p, a);
    public static void Act(DuelState st, int p, string type) => Do(st, p, new DuelAction { Type = type });

    public static int SummonStage1(DuelState st, int p, int zone)
    {
        int c = Give(st, p, x => x.Kind == "pet" && x.Forms[0].Stage == 1 && !x.Forms[0].Divine);
        st.Players[p].NormalSummonUsed = false;
        Do(st, p, new DuelAction { Type = "summon", Card = c, Zone = zone });
        while (st.Priority >= 0) Act(st, st.Priority, "pass");
        return st.Players[p].Units.First(u => u.Zone == zone).Uid;
    }

    /// <summary>Plays the game out with the AI on both sides.</summary>
    public static int PlayOut(DuelState st, int maxActions = 5000)
    {
        int n = 0;
        while (!st.Finished && n++ < maxActions) DuelEngine.Apply(Rules, st, st.ToAct, DuelAi.Decide(Rules, st, st.ToAct));
        return n;
    }
}

public class DuelRuleTests(ITestOutputHelper output)
{
    private static DuelRules R => DuelFixture.Rules;

    [Fact]
    public void Deck_must_have_exactly_40_cards_10_to_25_pets_and_copy_limits()
    {
        DeckEntryInfo P(string f, long id, bool divine = false) => new() { Kind = "pet", FormId = f, CardInstanceId = id, Divine = divine };
        DeckEntryInfo T(string t, int n) => new() { Kind = "tactic", TacticId = t, Count = n };
        var pets = Enumerable.Range(0, 12).Select(i => P("pet_f" + (i / 3), i + 1)).ToList();   // 4 forms × 3 copies
        var tactics = DuelFixture.Tactics.Take(10).Select(t => T(t.Id, 2)).Concat(new[] { T(DuelFixture.Tactics[10].Id, 3), T(DuelFixture.Tactics[11].Id, 3), T(DuelFixture.Tactics[12].Id, 2) }).ToList();
        var ok = pets.Concat(tactics).ToList();
        Assert.Equal(40, ok.Sum(e => e.Count));
        Assert.Null(DuelDeckRules.Validate(R, ok));
        Assert.Equal("deck_must_have_40", DuelDeckRules.Validate(R, ok.Take(ok.Count - 1).ToList()));
        var fourCopies = ok.ToList(); fourCopies[^1] = T(DuelFixture.Tactics[12].Id, 1); fourCopies.Add(P("pet_f0", 99));
        Assert.Equal("deck_too_many_copies", DuelDeckRules.Validate(R, fourCopies));
        var twoDivine = ok.ToList(); twoDivine[0] = P("pet_dv_nebulon", 501, true); twoDivine[1] = P("pet_dv_nebulon", 502, true);
        Assert.Equal("deck_divine_copies", DuelDeckRules.Validate(R, twoDivine));
        var fewPets = pets.Take(9).Concat(tactics).Append(T(DuelFixture.Tactics[13].Id, 3)).ToList();
        Assert.Equal("deck_pet_count", DuelDeckRules.Validate(R, fewPets));
        var dupInstance = ok.ToList(); dupInstance[1] = P("pet_f0", 1);
        Assert.Equal("deck_duplicate_instance", DuelDeckRules.Validate(R, dupInstance));
    }

    [Fact]
    public void Setup_shuffles_deals_five_and_the_coin_is_fair()
    {
        int first0 = 0;
        for (ulong s = 1; s <= 2000; s++) { var st = DuelFixture.New(s); first0 += st.First == 0 ? 1 : 0; }
        Assert.InRange(first0 / 2000.0, 0.46, 0.54);
        var d = DuelFixture.New(7);
        Assert.Equal(6, d.Players[d.First].Hand.Count);       // 5 + draw phase
        Assert.Equal(5, d.Players[1 - d.First].Hand.Count);
        Assert.Equal(2500, d.Players[0].Lp); Assert.Equal(2500, d.Players[1].Lp);
        Assert.Equal(40, d.Players[0].Deck.Count + d.Players[0].Hand.Count);
        Assert.Equal(d.Log.Select(e => e.Digest()), DuelFixture.New(7).Log.Select(e => e.Digest()));    // deterministic
        Assert.NotEqual(DuelFixture.New(7).Players[0].Deck, DuelFixture.New(8).Players[0].Deck);
    }

    [Fact]
    public void First_player_cannot_attack_on_turn_one_and_new_pets_cannot_attack()
    {
        var st = DuelFixture.New(11);
        int a = st.First;
        DuelFixture.SummonStage1(st, a, 0);
        var ex = Assert.Throws<DuelRuleException>(() => DuelFixture.Act(st, a, "battle"));
        Assert.Equal("first_turn_no_battle", ex.Code);
        DuelFixture.Act(st, a, "end");
        int b = 1 - a;
        int uid = DuelFixture.SummonStage1(st, b, 0);
        DuelFixture.Act(st, b, "battle");
        var target = st.Players[a].Units[0].Uid;
        Assert.Equal("summoned_this_turn", Assert.Throws<DuelRuleException>(() => DuelFixture.Do(st, b, new DuelAction { Type = "attack", Unit = uid, Target = target })).Code);
        Assert.Equal("not_your_turn", Assert.Throws<DuelRuleException>(() => DuelFixture.Act(st, a, "end")).Code);
    }

    [Fact]
    public void At_most_five_pets_and_three_spell_trap_zones_and_one_normal_summon()
    {
        var st = DuelFixture.New(12);
        int p = st.First;
        for (int z = 0; z < 5; z++) DuelFixture.SummonStage1(st, p, z);
        Assert.Equal(5, st.Players[p].Units.Count);
        int c = DuelFixture.Give(st, p, x => x.Kind == "pet" && x.Forms[0].Stage == 1);
        st.Players[p].NormalSummonUsed = false;
        Assert.Equal("field_full", Assert.Throws<DuelRuleException>(() => DuelFixture.Do(st, p, new DuelAction { Type = "summon", Card = c, Zone = 0 })).Code);
        Assert.Equal("summon_used", Assert.Throws<DuelRuleException>(() =>
        {
            st.Players[p].NormalSummonUsed = true; DuelFixture.Do(st, p, new DuelAction { Type = "summon", Card = c, Zone = 0 });
        }).Code);
        var traps = DuelFixture.Tactics.Where(t => t.Type == "trap").Take(4).Select(t => DuelFixture.GiveTactic(st, p, t.Id)).ToList();
        for (int z = 0; z < 3; z++) DuelFixture.Do(st, p, new DuelAction { Type = "set", Card = traps[z], Zone = z });
        Assert.Equal("zone_occupied", Assert.Throws<DuelRuleException>(() => DuelFixture.Do(st, p, new DuelAction { Type = "set", Card = traps[3], Zone = 0 })).Code);
        Assert.Equal("invalid_zone", Assert.Throws<DuelRuleException>(() => DuelFixture.Do(st, p, new DuelAction { Type = "set", Card = traps[3], Zone = 3 })).Code);
        int spell = DuelFixture.GiveTactic(st, p, "sp_tactical_draw");
        Assert.Equal("only_traps_can_be_set", Assert.Throws<DuelRuleException>(() => DuelFixture.Do(st, p, new DuelAction { Type = "set", Card = spell, Zone = 0 })).Code);
    }

    [Fact]
    public void Pet_hp_and_player_lp_are_separate_and_a_death_costs_exactly_500_once()
    {
        var st = DuelFixture.New(13);
        int a = st.First, b = 1 - a;
        int ua = DuelFixture.SummonStage1(st, a, 0);
        DuelFixture.Act(st, a, "end");
        DuelFixture.SummonStage1(st, b, 0);
        DuelFixture.Act(st, b, "end");
        var target = st.Players[b].Units[0];
        target.Hp = 1;                                         // next hit destroys it
        DuelFixture.Act(st, a, "battle");
        DuelFixture.Do(st, a, new DuelAction { Type = "attack", Unit = ua, Target = target.Uid });
        while (st.Priority >= 0) DuelFixture.Act(st, st.Priority, "pass");
        Assert.Empty(st.Players[b].Units);
        Assert.Equal(2000, st.Players[b].Lp);
        Assert.Equal(2500, st.Players[a].Lp);
        Assert.Single(st.Log, e => e.Type == "destroyed");
        Assert.Contains(target.CardId, st.Players[b].Grave);
        // HP damage alone never touches LP
        var lpBefore = st.Players[a].Lp;
        DuelEngine.ApplyDamage(R, st, st.Players[a].Units[0], 10, "test");
        Assert.Equal(lpBefore, st.Players[a].Lp);
    }

    [Fact]
    public void Tribute_summon_replaces_pets_without_lp_loss()
    {
        var st = DuelFixture.New(14);
        int p = st.First;
        int u1 = DuelFixture.SummonStage1(st, p, 0);
        DuelFixture.Act(st, p, "end"); DuelFixture.Act(st, 1 - p, "end");
        int c = DuelFixture.Give(st, p, x => x.Kind == "pet" && x.Forms[0].Stage == 2);
        Assert.Equal("tributes_required", Assert.Throws<DuelRuleException>(() => DuelFixture.Do(st, p, new DuelAction { Type = "summon", Card = c, Zone = 1 })).Code);
        DuelFixture.Do(st, p, new DuelAction { Type = "summon", Card = c, Zone = 0, Tributes = new() { u1 } });
        while (st.Priority >= 0) DuelFixture.Act(st, st.Priority, "pass");
        Assert.Single(st.Players[p].Units);
        Assert.Equal(2, st.Players[p].Units[0].Stage);
        Assert.Equal(2500, st.Players[p].Lp);
        Assert.DoesNotContain(st.Log, e => e.Type == "destroyed");
        Assert.Contains(st.Log, e => e.Type == "tribute");
    }

    [Fact]
    public void Spells_heal_damage_draw_and_respect_cost_and_limits()
    {
        var st = DuelFixture.New(15);
        int p = st.First, e = 1 - p;
        int u = DuelFixture.SummonStage1(st, p, 0);
        var unit = DuelEngine.Unit(st, u)!;
        unit.Hp = unit.MaxHp / 4;
        int tonic = DuelFixture.GiveTactic(st, p, "sp_herbal_tonic");
        DuelFixture.Do(st, p, new DuelAction { Type = "spell", Card = tonic, Targets = new() { u } });
        while (st.Priority >= 0) DuelFixture.Act(st, st.Priority, "pass");
        Assert.Equal(unit.MaxHp / 4 + unit.MaxHp * 30 / 100, unit.Hp);
        int hand = st.Players[p].Hand.Count;
        int draw = DuelFixture.GiveTactic(st, p, "sp_tactical_draw");
        DuelFixture.Do(st, p, new DuelAction { Type = "spell", Card = draw });
        while (st.Priority >= 0) DuelFixture.Act(st, st.Priority, "pass");
        Assert.Equal(hand + 2, st.Players[p].Hand.Count);   // +1 given, −1 played, +2 drawn
        st.Players[p].Energy = 0;
        int storm = DuelFixture.GiveTactic(st, p, "sp_fire_storm");
        Assert.Equal("not_enough_energy", Assert.Throws<DuelRuleException>(() => DuelFixture.Do(st, p, new DuelAction { Type = "spell", Card = storm })).Code);
        st.Players[p].Energy = 8;
        int w1 = DuelFixture.GiveTactic(st, p, "sp_energy_well");
        DuelFixture.Do(st, p, new DuelAction { Type = "spell", Card = w1 });
        while (st.Priority >= 0) DuelFixture.Act(st, st.Priority, "pass");
        int w2 = DuelFixture.GiveTactic(st, p, "sp_energy_well");
        Assert.Equal("card_limit_reached", Assert.Throws<DuelRuleException>(() => DuelFixture.Do(st, p, new DuelAction { Type = "spell", Card = w2 })).Code);
        // elemental damage scales with the target's max HP and element
        DuelFixture.Act(st, p, "end");
        int eu = DuelFixture.SummonStage1(st, e, 0);
        DuelFixture.Act(st, e, "end");
        var target = DuelEngine.Unit(st, eu)!;
        int bolt = DuelFixture.GiveTactic(st, p, "sp_thunderbolt");
        DuelFixture.Do(st, p, new DuelAction { Type = "spell", Card = bolt, Targets = new() { eu } });
        while (st.Priority >= 0) DuelFixture.Act(st, st.Priority, "pass");
        int expect = target.MaxHp * 28 / 100 * DuelEngine.ElementPct(R, "lightning", target.Element) / 100;
        Assert.Contains(st.Log, x => x.Type == "damage" && x.A == eu && x.N == expect && x.S == "sp_thunderbolt");
    }

    [Fact]
    public void Traps_cannot_fire_the_turn_they_are_set_and_react_to_attacks_with_a_chain()
    {
        var st = DuelFixture.New(16);
        int a = st.First, b = 1 - a;
        int ua = DuelFixture.SummonStage1(st, a, 0);
        DuelFixture.Act(st, a, "end");
        int ub = DuelFixture.SummonStage1(st, b, 0);
        int vines = DuelFixture.GiveTactic(st, b, "tr_binding_vines");
        DuelFixture.Do(st, b, new DuelAction { Type = "set", Card = vines, Zone = 0 });
        Assert.False(DuelEngine.TrapReady(R, st, b, st.Players[b].Zones[0]));      // same turn
        DuelFixture.Act(st, b, "end");
        st.Players[b].Energy = 5;
        DuelFixture.Act(st, a, "battle");
        DuelFixture.Do(st, a, new DuelAction { Type = "attack", Unit = ua, Target = ub });
        Assert.Equal(b, st.Priority);                          // reaction window for the defender
        int hpBefore = DuelEngine.Unit(st, ub)!.Hp;
        DuelFixture.Do(st, b, new DuelAction { Type = "activate", Zone = 0 });
        while (st.Priority >= 0) DuelFixture.Act(st, st.Priority, "pass");
        Assert.Equal(hpBefore, DuelEngine.Unit(st, ub)!.Hp);    // attack negated
        Assert.True(DuelEngine.Has(DuelEngine.Unit(st, ua)!, "stun"));
        Assert.Contains(st.Log, x => x.Type == "negated");
        Assert.Null(st.Players[b].Zones[0]);
    }

    [Fact]
    public void Chain_resolves_last_in_first_out_and_negations_target_the_previous_link()
    {
        var st = DuelFixture.New(17);
        int a = st.First, b = 1 - a;
        int ua = DuelFixture.SummonStage1(st, a, 0);
        DuelFixture.Act(st, a, "end");
        int seal = DuelFixture.GiveTactic(st, b, "tr_spell_seal");
        DuelFixture.Do(st, b, new DuelAction { Type = "set", Card = seal, Zone = 0 });
        DuelFixture.Act(st, b, "end");
        st.Players[a].Energy = 8; st.Players[b].Energy = 8;
        var unit = DuelEngine.Unit(st, ua)!; unit.Hp = unit.MaxHp / 4;
        int tonic = DuelFixture.GiveTactic(st, a, "sp_herbal_tonic");
        int breaker = DuelFixture.GiveTactic(st, a, "sp_counter_spell");   // quick spell, but it cannot negate a TRAP
        DuelFixture.Do(st, a, new DuelAction { Type = "spell", Card = tonic, Targets = new() { ua } });       // link 1
        Assert.Equal(b, st.Priority);
        DuelFixture.Do(st, b, new DuelAction { Type = "activate", Zone = 0 });                                 // link 2 negates link 1
        Assert.Equal("nothing_to_negate", Assert.Throws<DuelRuleException>(() => DuelFixture.Do(st, a, new DuelAction { Type = "spell", Card = breaker })).Code);
        // the initiator had no legal response left (a counter-spell cannot negate a trap), so the window closed itself
        Assert.Equal(-1, st.Priority);
        Assert.Equal(unit.MaxHp / 4, unit.Hp);                   // the heal was negated
        var order = st.Log.Where(x => x.Type is "negate" or "negated").Select(x => x.Type).ToList();
        Assert.Equal(new[] { "negate", "negated" }, order);       // link 2 resolved before link 1
    }

    [Fact]
    public void Evolution_keeps_the_same_pet_with_hp_ratio_and_no_copy()
    {
        var st = DuelFixture.New(18);
        int p = st.First;
        int u = DuelFixture.SummonStage1(st, p, 0);
        var unit = DuelEngine.Unit(st, u)!;
        Assert.Equal("summoned_this_turn", Assert.Throws<DuelRuleException>(() => DuelFixture.Do(st, p, new DuelAction { Type = "evolve", Unit = u })).Code);
        DuelFixture.Act(st, p, "end"); DuelFixture.Act(st, 1 - p, "end");
        unit.Hp = unit.MaxHp / 2;
        int oldMax = unit.MaxHp, oldCard = unit.CardId;
        st.Players[p].Energy = 8;
        DuelFixture.Do(st, p, new DuelAction { Type = "evolve", Unit = u });
        Assert.Equal(u, unit.Uid); Assert.Equal(oldCard, unit.CardId);
        Assert.Equal(2, unit.Stage);
        Assert.True(unit.MaxHp > oldMax);
        Assert.Equal(unit.MaxHp / 2, unit.Hp, 1.0);              // ratio kept, not refilled
        Assert.Single(st.Players[p].Units);
        Assert.Equal("already_evolved", Assert.Throws<DuelRuleException>(() => DuelFixture.Do(st, p, new DuelAction { Type = "evolve", Unit = u })).Code);
    }

    [Fact]
    public void Deck_out_loses_and_turn_limit_compares_lp()
    {
        var st = DuelFixture.New(19);
        int p = st.First;
        st.Players[1 - p].Deck.Clear();
        DuelFixture.Act(st, p, "end");
        Assert.True(st.Finished);
        Assert.Equal(p, st.Winner); Assert.Equal("deck_out", st.EndReason);

        var t = DuelFixture.New(20);
        t.Players[0].TurnsTaken = 40; t.Players[1].TurnsTaken = 40; t.Players[1 - t.First].Lp = 2000;
        DuelFixture.Act(t, t.First, "end");
        Assert.True(t.Finished); Assert.Equal(t.First, t.Winner); Assert.Equal("turn_limit_lp", t.EndReason);
    }

    [Fact]
    public void Timeouts_pass_or_end_the_turn_and_repeated_timeouts_forfeit()
    {
        var st = DuelFixture.New(21);
        int p = st.First;
        int turn = st.Turn;
        DuelEngine.Timeout(R, st);
        Assert.Equal(turn + 1, st.Turn);
        DuelEngine.Timeout(R, st); DuelEngine.Timeout(R, st);   // other player
        DuelEngine.Timeout(R, st); DuelEngine.Timeout(R, st);
        Assert.True(st.Finished);
        Assert.Equal("timeout", st.EndReason);
    }

    [Fact]
    public void Ai_vs_ai_duels_always_finish_with_a_legal_result()
    {
        var reasons = new Dictionary<string, int>();
        int turns = 0;
        for (ulong s = 1; s <= 60; s++)
        {
            var st = DuelFixture.New(s * 977, DuelFixture.Deck((int)(s % 7)), DuelFixture.Deck((int)(s % 5) + 2));
            DuelFixture.PlayOut(st);
            Assert.True(st.Finished, "duel did not finish");
            Assert.All(st.Players, pl => Assert.True(pl.Units.Count <= 5 && pl.Zones.Count == 3 && pl.Hand.Count <= 7 || st.Finished));
            reasons[st.EndReason!] = reasons.GetValueOrDefault(st.EndReason!) + 1;
            turns += st.Turn;
            Assert.Equal(st.Log.Count(e => e.Type == "destroyed" && e.P == 0) * 500, 2500 - st.Players[0].Lp);
            Assert.Equal(st.Log.Count(e => e.Type == "destroyed" && e.P == 1) * 500, 2500 - st.Players[1].Lp);
        }
        output.WriteLine("end reasons: " + string.Join(", ", reasons.Select(k => $"{k.Key}={k.Value}")) + $"; avg turns {turns / 60.0:0.0}");
        Assert.True(reasons.GetValueOrDefault("lp_zero") > 30, "most duels should end by LP");
    }

    [Fact]
    public void Divine_decks_are_strong_but_not_invincible_and_ranked_caps_growth()
    {
        var divine = DuelFixture.Cat.Forms.Values.Where(f => f.DivineSet != null).OrderBy(f => f.Id).Take(10).Select(f => f.Id).ToList();
        List<DuelCard> DivineDeck()
        {
            var d = DuelFixture.Deck(3, 50).Where(c => c.Kind == "tactic").ToList();
            var maxed = new CardProgress { Level = 70, Stars = 10, Enhance = 20, Breakthrough = 4, Awakened = true, SkillLevel = 10 };
            d.AddRange(divine.Select(f => DuelFixture.Pet(f, prog: maxed, rarity: "DV")));
            d.AddRange(DuelFixture.Deck(3, 50).Where(c => c.Kind == "pet").Take(6));
            return d.Take(40).ToList();
        }
        int dv = 0, games = 40;
        for (ulong s = 1; s <= (ulong)games; s++)
        {
            bool swap = s % 2 == 0;
            var a = DivineDeck(); var b = DuelFixture.Deck(4, 50);
            var st = DuelFixture.New(s * 31, swap ? b : a, swap ? a : b);
            DuelFixture.PlayOut(st);
            if (st.Winner == (swap ? 1 : 0)) dv++;
        }
        output.WriteLine($"maxed divine-heavy deck vs level-50 regular deck: {dv}/{games}");
        Assert.True(dv < games, "a divine deck must be beatable");
        // ranked: growth beyond RankedGrowthCap is clipped
        var full = new CardProgress { Level = 50, Stars = 10, Enhance = 20, Awakened = true };
        var casual = DuelFixture.Pet("pet_pyrelynx", prog: full)!.Forms[0];
        var ranked = DuelFixture.Pet("pet_pyrelynx", prog: full, ranked: true)!.Forms[0];
        var plain = DuelFixture.Pet("pet_pyrelynx", prog: new CardProgress { Level = 50 })!.Forms[0];
        Assert.True(ranked.Atk < casual.Atk);
        Assert.InRange(ranked.Atk, plain.Atk, (int)Math.Round(plain.Atk * 1.45) + 1);
    }
}
