using PetStar.Api.Infrastructure;
using PetStar.Rules.Arena;
using Xunit.Abstractions;

namespace PetStar.Rules.Tests;

public static class ArenaFixture
{
    private static readonly Lazy<ArenaRules> Data = new(() => CatalogLoader.LoadArena(Path.Combine(AppContext.BaseDirectory, "seed")));
    public static ArenaRules Rules => Data.Value;

    public static long Inst(string defId, int n) => (defId.Aggregate(17L, (h, ch) => (h * 31 + ch) & 0x3fffffff) << 3) + n;

    public static List<ArenaDeckCard> DeckOf(params (string id, int n)[] parts) =>
        parts.SelectMany(x => Enumerable.Range(0, x.n).Select(i => new ArenaDeckCard { DefId = x.id, CardInstanceId = Rules.Def(x.id).Kind == "pet" ? Inst(x.id, i) : 0 })).ToList();

    /// <summary>A legal 20-card deck: one element's evolution lines + trainers (variant picks the element/lines).</summary>
    public static List<ArenaDeckCard> Deck(int variant)
    {
        var els = Rules.Elements;
        var el = els[variant % els.Count];
        var pets = Rules.Cards.Where(c => c.Kind == "pet" && (c.Element == el || (variant % 3 == 0 && c.Ex))).ToList();
        var pool = pets.SelectMany(c => new[] { (c.Id, Inst(c.Id, 0)), (c.Id, Inst(c.Id, 1)) }).ToList();
        var rot = variant % Math.Max(1, pool.Count);
        pool = pool.Skip(rot).Concat(pool.Take(rot)).ToList();
        var trainers = Rules.Cards.Where(c => c.Kind != "pet").ToDictionary(c => c.Id, _ => 2);
        var entries = ArenaDecks.Auto(Rules, pool, trainers, 10 + variant % 5);
        return ArenaDecks.Expand(entries);
    }

    public static ArenaState New(ulong seed, List<ArenaDeckCard>? a = null, List<ArenaDeckCard>? b = null, bool pve = false) =>
        ArenaEngine.Create(Rules, new ArenaSetupPlayer { Name = "Blue", Deck = a ?? Deck(0) }, new ArenaSetupPlayer { Name = "Red", Deck = b ?? Deck(1) }, seed, pve);

    public static void Do(ArenaState st, int p, ArenaAction a) => ArenaEngine.Apply(Rules, st, p, a);
    public static string Err(ArenaState st, int p, ArenaAction a) { try { Do(st, p, a); return "ok"; } catch (ArenaRuleException e) { return e.Code; } }

    public static void SetupBoth(ArenaState st) { for (int p = 0; p < 2; p++) Do(st, p, ArenaAi.SetupChoice(Rules, st, p)); }

    /// <summary>Test setup: moves a card with this definition from the deck (or creates the move from discard) into the hand.</summary>
    public static int Give(ArenaState st, int p, string defId)
    {
        var pl = st.Players[p];
        int c = pl.Deck.FirstOrDefault(x => st.Card(x).DefId == defId);
        if (c == 0)
        {
            // test setup only: re-label one of this player's deck trainer cards (card count and ownership unchanged)
            c = pl.Deck.FirstOrDefault(x => Rules.Def(st.Card(x).DefId).Kind != "pet" || Rules.Def(defId).Kind == "pet");
            if (c == 0) throw new InvalidOperationException("no deck card to use for " + defId);
            st.Card(c).DefId = defId;
        }
        pl.Deck.Remove(c); pl.Hand.Add(c);
        return c;
    }

    /// <summary>Plays the match to the end with the AI (both sides), returning the number of actions.</summary>
    public static int PlayOut(ArenaState st, int max = 4000)
    {
        int n = 0;
        while (!st.Finished && n < max)
        {
            int p = Enumerable.Range(0, 2).First(q => ArenaEngine.MustAct(st, q));
            Do(st, p, ArenaAi.Decide(Rules, st, p));
            n++;
        }
        return n;
    }

    public static string Pick(string kind, Func<ArenaCardDef, bool>? f = null) =>
        Rules.Cards.Where(c => c.Kind == kind && (f == null || f(c))).OrderBy(c => c.Id).First().Id;
}

public class ArenaRuleTests(ITestOutputHelper output)
{
    private static ArenaRules R => ArenaFixture.Rules;

    // ---------------------------------------------------------------- data & decks

    [Fact]
    public void Card_pool_has_every_pet_form_with_battle_data_and_original_trainers()
    {
        var pets = R.Cards.Where(c => c.Kind == "pet").ToList();
        Assert.Equal(215, pets.Count);                                  // every existing Pet form (incl. evolutions and 15 Divine)
        Assert.Equal(15, pets.Count(p => p.Ex));
        Assert.All(pets, p => { Assert.True(p.Hp > 0); Assert.NotEmpty(p.Attacks); Assert.Contains(p.Attacks, a => a.Damage > 0); Assert.InRange(p.Stage, 0, 2); });
        Assert.All(pets.Where(p => p.Stage > 0), p => Assert.Equal(p.Stage - 1, R.Def(p.EvolvesFrom!).Stage));
        Assert.All(pets.Where(p => p.Ex), p => Assert.Equal(2, p.KoPoints));
        Assert.True(R.Cards.Count(c => c.Kind == "supporter") >= 6 && R.Cards.Count(c => c.Kind == "item") >= 3 && R.Cards.Count(c => c.Kind == "tool") >= 3);
        Assert.False(string.IsNullOrEmpty(R.Version));
        var ops = R.Cards.SelectMany(c => c.Effects.Concat(c.Attacks.SelectMany(a => a.Effects)).Concat(c.Ability?.Effects ?? new())).Select(e => e.Op).Distinct().ToHashSet();
        foreach (var need in new[] { "heal", "status", "shield", "draw", "search", "switch_self", "force_switch", "retreat_mod", "energy_attach", "energy_discard", "energy_transfer", "coin_damage", "bonus", "bench_damage", "ability_lock", "attack_lock_self", "status_immunity", "on_ko_damage", "reduce_taken" })
            Assert.Contains(need, ops);
        Assert.Equal(new[] { "asleep", "burned", "confused", "paralyzed", "poisoned" }, R.Cards.SelectMany(c => c.Attacks.SelectMany(a => a.Effects)).Where(e => e.Op == "status").Select(e => e.Status!).Distinct().OrderBy(x => x));
    }

    [Fact]
    public void Deck_rules_20_cards_max_2_same_card_at_least_one_basic_owned_instances()
    {
        var basic = ArenaFixture.Pick("pet", c => c.IsBasic && !c.Ex && R.Cards.Any(x => x.EvolvesFrom == c.Id));
        var stage1 = R.Cards.First(c => c.Kind == "pet" && c.EvolvesFrom == basic).Id;
        List<ArenaDeckEntry> Ok() => new()
        {
            new() { DefId = basic, CardInstanceId = 1 }, new() { DefId = basic, CardInstanceId = 2 }, new() { DefId = stage1, CardInstanceId = 3 },
            new() { DefId = "su_scholar", Count = 2 }, new() { DefId = "su_medic", Count = 2 }, new() { DefId = "su_captain", Count = 2 }, new() { DefId = "su_guide", Count = 2 },
            new() { DefId = "at_herb_salve", Count = 2 }, new() { DefId = "at_star_lantern", Count = 2 }, new() { DefId = "at_leaf_armor", Count = 2 }, new() { DefId = "su_strategist", Count = 2 }, new() { DefId = "at_lucky_coin", Count = 1 },
        };
        Assert.Null(ArenaDecks.Validate(R, Ok(), new[] { "fire" }));
        var d = Ok(); d[^1].Count = 2; Assert.Equal("deck_must_have_20", ArenaDecks.Validate(R, d, null));
        d = Ok(); d[^1] = new() { DefId = basic, CardInstanceId = 9 }; Assert.Equal("deck_too_many_copies", ArenaDecks.Validate(R, d, null));
        d = Ok(); d[1].CardInstanceId = 1; Assert.Equal("deck_duplicate_instance", ArenaDecks.Validate(R, d, null));
        d = Ok(); d[0] = new() { DefId = stage1, CardInstanceId = 7 }; d[1] = new() { DefId = "at_wind_boots", Count = 1 }; Assert.Equal("deck_needs_basic", ArenaDecks.Validate(R, d, null));
        d = Ok(); d[^1] = new() { DefId = "energy_fire", Count = 1 }; Assert.Equal("deck_unknown_card", ArenaDecks.Validate(R, d, null));   // no Energy cards exist
        Assert.Equal("deck_energy_types", ArenaDecks.Validate(R, Ok(), new[] { "fire", "water", "wind", "dark" }));
        Assert.Equal("deck_energy_types", ArenaDecks.Validate(R, Ok(), new[] { "plasma" }));
        for (int v = 0; v < 24; v++)
        {
            var deck = ArenaFixture.Deck(v);
            var entries = deck.GroupBy(x => (x.DefId, x.CardInstanceId)).Select(g => new ArenaDeckEntry { DefId = g.Key.DefId, CardInstanceId = g.Key.CardInstanceId, Count = g.Count() }).ToList();
            Assert.Null(ArenaDecks.Validate(R, entries, null));
        }
    }

    // ---------------------------------------------------------------- setup & turns

    [Fact]
    public void Shuffle_deal_and_coin_are_deterministic_fair_and_always_give_a_basic()
    {
        int first0 = 0;
        for (ulong s = 1; s <= 600; s++)
        {
            var st = ArenaFixture.New(s, ArenaFixture.Deck((int)s % 9), ArenaFixture.Deck((int)s % 7 + 2));
            for (int p = 0; p < 2; p++)
            {
                var pl = st.Players[p];
                Assert.Equal(5, pl.Hand.Count); Assert.Equal(15, pl.Deck.Count);
                Assert.Contains(pl.Hand, c => R.Def(st.Card(c).DefId).IsBasic);
                Assert.Equal(20, pl.Hand.Concat(pl.Deck).Distinct().Count());
                Assert.All(pl.Hand.Concat(pl.Deck), c => Assert.Equal(p, st.Card(c).Owner));
            }
            if (st.First == 0) first0++;
        }
        Assert.InRange(first0, 250, 350);
        var x = ArenaFixture.New(77); var y = ArenaFixture.New(77);
        Assert.Equal(x.Players[0].Hand, y.Players[0].Hand); Assert.Equal(x.Players[1].Deck, y.Players[1].Deck); Assert.Equal(x.First, y.First);
        // a deck with a single Basic still always opens with it (controlled redeal, no card created)
        var basic = ArenaFixture.Pick("pet", c => c.IsBasic && !c.Ex);
        var one = ArenaFixture.DeckOf((basic, 1), ("su_scholar", 2), ("su_medic", 2), ("su_captain", 2), ("su_guide", 2), ("su_smith", 2), ("su_strategist", 2), ("at_herb_salve", 2), ("at_star_lantern", 2), ("at_leaf_armor", 2), ("at_lucky_coin", 1));
        for (ulong s = 1; s <= 200; s++)
        {
            var st = ArenaFixture.New(s, one, one);
            Assert.Contains(st.Players[0].Hand, c => st.Card(c).DefId == basic);
            Assert.Equal(20, st.Players[0].Hand.Count + st.Players[0].Deck.Count);
        }
    }

    [Fact]
    public void Setup_requires_basics_and_the_first_turns_follow_the_configured_rules()
    {
        var st = ArenaFixture.New(5);
        var pl = st.Players[0];
        var nonBasic = pl.Hand.FirstOrDefault(c => !R.Def(st.Card(c).DefId).IsBasic);
        if (nonBasic != 0) Assert.Equal("setup_needs_basic", ArenaFixture.Err(st, 0, new ArenaAction { Type = "setup", Card = nonBasic }));
        Assert.Equal("setup_required", ArenaFixture.Err(st, 0, new ArenaAction { Type = "end" }));
        ArenaFixture.SetupBoth(st);
        Assert.Equal("main", st.Phase);
        int f = st.First, s = 1 - f;
        var fp = st.Players[f];
        Assert.Equal(1, fp.TurnsTaken);
        Assert.Null(fp.ZoneCurrent);                                    // first player: no energy on turn 1
        Assert.NotNull(fp.ZoneNext);                                    // ...but the next energy is already shown
        int handBefore = st.Players[f].Hand.Count;
        Assert.Equal("no_attack_first_turn", ArenaFixture.Err(st, f, new ArenaAction { Type = "attack", Index = 0 }));
        Assert.Equal("no_energy_available", ArenaFixture.Err(st, f, new ArenaAction { Type = "attach", Uid = fp.Active!.Uid }));
        Assert.Equal("not_your_turn", ArenaFixture.Err(st, s, new ArenaAction { Type = "end" }));
        int sHand = st.Players[s].Hand.Count;
        ArenaFixture.Do(st, f, new ArenaAction { Type = "end" });
        var sp = st.Players[s];
        Assert.Equal(s, st.Active);
        Assert.NotNull(sp.ZoneCurrent);                                 // second player gets energy on their first turn
        Assert.Equal(sHand + 1, sp.Hand.Count);                          // second player draws on their first turn
        Assert.Equal("no_attack_first_turn", ArenaFixture.Err(st, s, new ArenaAction { Type = "attack", Index = 0 }));   // "both": neither attacks on their own first turn
        Assert.Equal("ok", ArenaFixture.Err(st, s, new ArenaAction { Type = "attach", Uid = sp.Active!.Uid }));
        Assert.Equal("energy_already_attached", ArenaFixture.Err(st, s, new ArenaAction { Type = "attach", Uid = sp.Active!.Uid }));
        var sup = ArenaFixture.Give(st, s, "su_scholar");
        Assert.Equal("ok", ArenaFixture.Err(st, s, new ArenaAction { Type = "supporter", Card = sup }));   // Supporters allowed on the first turn
        if (st.Players[s].Deck.Any(c => st.Card(c).DefId == "su_scholar"))
            Assert.Equal("supporter_used", ArenaFixture.Err(st, s, new ArenaAction { Type = "supporter", Card = ArenaFixture.Give(st, s, "su_scholar") }));
        Assert.Equal(handBefore, st.Players[f].Hand.Count);           // first player did not draw on turn 1 (config firstPlayerDraws=false)
    }

    [Fact]
    public void First_player_draw_and_attack_rules_are_configurable()
    {
        var rules = new ArenaRules { Version = R.Version, Elements = R.Elements, Cards = R.Cards, Config = new ArenaConfig { FirstPlayerDraws = true, AttackBanFirstTurn = "first_player" } };
        var st = ArenaEngine.Create(rules, new ArenaSetupPlayer { Name = "A", Deck = ArenaFixture.Deck(0) }, new ArenaSetupPlayer { Name = "B", Deck = ArenaFixture.Deck(1) }, 9);
        for (int p = 0; p < 2; p++) ArenaEngine.Apply(rules, st, p, ArenaAi.SetupChoice(rules, st, p));
        Assert.Contains(st.Log, e => e.Type == "draw" && e.P == st.First);
        Assert.True(ArenaEngine.AttackBanned(rules, st, st.First));
        ArenaEngine.Apply(rules, st, st.First, new ArenaAction { Type = "end" });
        Assert.False(ArenaEngine.AttackBanned(rules, st, 1 - st.First));   // second player may attack on their first turn with this setting
    }

    [Fact]
    public void Energy_zone_one_attach_per_turn_unused_energy_is_lost_and_energy_stays_after_attacking()
    {
        var st = ArenaFixture.New(11); ArenaFixture.SetupBoth(st);
        ArenaFixture.Do(st, st.First, new ArenaAction { Type = "end" });
        int p = st.Active; var pl = st.Players[p];
        var zone = pl.ZoneCurrent!;
        Assert.Contains(zone, pl.EnergyTypes);
        ArenaFixture.Do(st, p, new ArenaAction { Type = "end" });       // not attached
        Assert.Null(pl.ZoneCurrent);                                     // energyCarryOver=false: lost
        Assert.DoesNotContain(pl.InPlay(), x => x.Energy.Count > 0);
        // attach and attack: energy is not consumed by attacking
        var q = st.Active; var ql = st.Players[q];
        ArenaFixture.Do(st, q, new ArenaAction { Type = "attach", Uid = ql.Active!.Uid });
        int before = ql.Active.Energy.Count;
        var cheap = R.Def(ql.Active.DefId).Attacks.FindIndex(a => ArenaEngine.CostPaid(ql.Active.Energy, a.Cost));
        if (cheap >= 0)
        {
            var attacker = ql.Active;
            ArenaFixture.Do(st, q, new ArenaAction { Type = "attack", Index = cheap });
            Assert.Equal(before, attacker.Energy.Count);
        }
    }

    // ---------------------------------------------------------------- evolution, weakness, statuses

    private static (ArenaState st, int p, ArenaPet me, ArenaPet foe) Duel(ulong seed, string? myDef = null, string? foeDef = null)
    {
        var st = ArenaFixture.New(seed); ArenaFixture.SetupBoth(st);
        ArenaFixture.Do(st, st.First, new ArenaAction { Type = "end" });   // past the first player's restricted turn
        ArenaFixture.Do(st, st.Active, new ArenaAction { Type = "end" });   // past the second player's first turn
        int p = st.Active;
        var me = st.Players[p].Active!; var foe = st.Players[1 - p].Active!;
        if (myDef != null) me.DefId = myDef;
        if (foeDef != null) foe.DefId = foeDef;
        return (st, p, me, foe);
    }

    [Fact]
    public void Evolution_follows_the_line_keeps_energy_damage_and_tool_and_clears_conditions()
    {
        var basic = R.Cards.First(c => c.IsBasic && R.Cards.Any(x => x.EvolvesFrom == c.Id) && !c.Ex);
        var evo = R.Cards.First(c => c.EvolvesFrom == basic.Id);
        var other = R.Cards.First(c => c.Kind == "pet" && c.Stage == 1 && c.EvolvesFrom != basic.Id);
        var deck = ArenaFixture.DeckOf((basic.Id, 2), (evo.Id, 2), (other.Id, 1), ("su_scholar", 2), ("su_medic", 2), ("su_captain", 2), ("su_guide", 2), ("at_herb_salve", 2), ("at_star_lantern", 2), ("at_leaf_armor", 2), ("at_lucky_coin", 1));
        var st = ArenaFixture.New(3, deck, deck);
        for (int p = 0; p < 2; p++) { var b = st.Players[p].Hand.First(c => st.Card(c).DefId == basic.Id); ArenaFixture.Do(st, p, new ArenaAction { Type = "setup", Card = b }); }
        int f = st.First;
        var e1 = ArenaFixture.Give(st, f, evo.Id);
        Assert.Equal("no_evolve_first_turn", ArenaFixture.Err(st, f, new ArenaAction { Type = "evolve", Card = e1, Uid = st.Players[f].Active!.Uid }));
        ArenaFixture.Do(st, f, new ArenaAction { Type = "end" });
        ArenaFixture.Do(st, 1 - f, new ArenaAction { Type = "end" });
        var pet = st.Players[f].Active!;
        pet.Energy.AddRange(new[] { "fire", "water" }); pet.Damage = 30; pet.Poisoned = true; pet.Special = "asleep";
        var tool = ArenaFixture.Give(st, f, "at_leaf_armor");
        ArenaFixture.Do(st, f, new ArenaAction { Type = "tool", Card = tool, Uid = pet.Uid });
        var wrong = ArenaFixture.Give(st, f, other.Id);
        Assert.Equal("wrong_evolution_line", ArenaFixture.Err(st, f, new ArenaAction { Type = "evolve", Card = wrong, Uid = pet.Uid }));
        ArenaFixture.Do(st, f, new ArenaAction { Type = "evolve", Card = e1, Uid = pet.Uid });
        Assert.Same(pet, st.Players[f].Active);                                    // the same Pet on the field, no copy
        Assert.Equal(evo.Id, pet.DefId); Assert.Equal(2, pet.Cards.Count);
        Assert.Equal(new[] { "fire", "water" }, pet.Energy); Assert.Equal(30, pet.Damage); Assert.Equal(tool, pet.Tool);
        Assert.False(pet.Poisoned); Assert.Null(pet.Special);
        Assert.Equal(evo.Hp + 20, ArenaEngine.MaxHp(R, st, pet));
        // no second evolution of the same Pet in one turn; a Pet placed this turn cannot evolve
        var b2 = ArenaFixture.Give(st, f, basic.Id);
        ArenaFixture.Do(st, f, new ArenaAction { Type = "bench", Card = b2 });
        var e2 = ArenaFixture.Give(st, f, evo.Id);
        Assert.Equal("played_this_turn", ArenaFixture.Err(st, f, new ArenaAction { Type = "evolve", Card = e2, Uid = st.Players[f].Bench.Last().Uid }));
    }

    [Fact]
    public void Weakness_adds_20_after_modifiers_and_reductions_apply_after_weakness()
    {
        var fire = ArenaFixture.Pick("pet", c => c.Element == "fire" && c.Attacks[0].Damage >= 30 && c.Ability == null && !c.Attacks[0].Effects.Any(e => e.Op is "coin_damage" or "bonus"));
        var natureWeakToFire = ArenaFixture.Pick("pet", c => c.Weakness == "fire" && c.IsBasic && c.Ability == null);
        var waterNotWeak = ArenaFixture.Pick("pet", c => c.Weakness != "fire" && c.IsBasic && c.Ability == null);
        var (st, p, me, foe) = Duel(21, fire, natureWeakToFire);
        var atk = R.Def(fire).Attacks[0];
        Assert.Equal(atk.Damage + 20, ArenaEngine.AttackDamage(R, st, p, me, atk, foe, 0));
        foe.DefId = waterNotWeak;
        Assert.Equal(atk.Damage, ArenaEngine.AttackDamage(R, st, p, me, atk, foe, 0));
        foe.DefId = natureWeakToFire;
        foe.Mods.Add(new ArenaMod { Kind = "shield", Amount = 20, FromTurn = st.Turn, UntilTurn = st.Turn, Source = "a" });
        Assert.Equal(atk.Damage, ArenaEngine.AttackDamage(R, st, p, me, atk, foe, 0));
        foe.Mods.Add(new ArenaMod { Kind = "shield", Amount = 10, FromTurn = st.Turn, UntilTurn = st.Turn, Source = "b" });   // different sources stack
        Assert.Equal(Math.Max(0, atk.Damage - 10), ArenaEngine.AttackDamage(R, st, p, me, atk, foe, 0));
        foe.Mods.Add(new ArenaMod { Kind = "prevent", FromTurn = st.Turn, UntilTurn = st.Turn, Source = "c" });
        Assert.Equal(0, ArenaEngine.AttackDamage(R, st, p, me, atk, foe, 0));
    }

    [Fact]
    public void Special_conditions_poison_burn_sleep_paralysis_confusion()
    {
        var (st, p, me, foe) = Duel(31);
        int o = 1 - p;
        foe.Poisoned = true; foe.Burned = true;
        int d0 = foe.Damage;
        ArenaFixture.Do(st, p, new ArenaAction { Type = "end" });                    // checkup after p's turn
        Assert.True(foe.Damage >= d0 + 30 || st.Finished || st.Players[o].Active != foe);   // poison 10 + burn 20 (both stack)
        Assert.Contains(st.Log, e => e.Type == "poison" && e.N == 10);
        Assert.Contains(st.Log, e => e.Type == "burn" && e.N == 20);
        Assert.Contains(st.Log, e => e.Type == "coin" && e.S == "burn");            // burn: coin flip to recover

        // asleep / paralyzed: cannot attack or retreat; asleep and paralyzed replace each other
        var (s2, p2, m2, f2) = Duel(32);
        m2.Special = "asleep";
        m2.Energy.AddRange(Enumerable.Repeat(R.Def(m2.DefId).Element, 4));
        Assert.Equal("cannot_attack_now", ArenaFixture.Err(s2, p2, new ArenaAction { Type = "attack", Index = 0 }));
        if (s2.Players[p2].Bench.Count > 0) Assert.Equal("cannot_retreat_now", ArenaFixture.Err(s2, p2, new ArenaAction { Type = "retreat", Uid = s2.Players[p2].Bench[0].Uid }));
        m2.Special = "paralyzed"; m2.SpecialTurn = s2.Turn - 1;
        Assert.Equal("cannot_attack_now", ArenaFixture.Err(s2, p2, new ArenaAction { Type = "attack", Index = 0 }));
        ArenaFixture.Do(s2, p2, new ArenaAction { Type = "end" });                   // paralysis ends after its owner's turn
        if (!s2.Finished && s2.Players[p2].Active == m2) Assert.Null(m2.Special);

        // confused: a failed flip means the attack does nothing (configured self-damage 0)
        int fails = 0, hits = 0;
        for (ulong seed = 40; seed < 80; seed++)
        {
            var (s3, p3, m3, f3) = Duel(seed);
            m3.Special = "confused";
            m3.Energy.AddRange(Enumerable.Repeat(R.Def(m3.DefId).Element, 4));
            m3.Energy.AddRange(Enumerable.Repeat("colorless", 0));
            foreach (var t in R.Def(m3.DefId).Attacks[0].Cost) m3.Energy.Add(t == "colorless" ? R.Def(m3.DefId).Element : t);
            int before = f3.Damage, selfBefore = m3.Damage;
            ArenaFixture.Do(s3, p3, new ArenaAction { Type = "attack", Index = 0 });
            if (s3.Log.Any(e => e.Type == "attack_failed")) { fails++; Assert.Equal(selfBefore, m3.Damage - (s3.Log.Where(e => e.Type is "poison" or "burn" && e.A == m3.Uid).Sum(e => e.N))); }
            else hits++;
        }
        Assert.True(fails > 5 && hits > 5, $"confusion flips should vary: {fails}/{hits}");

        // a Dewdrop Charm blocks Special Conditions
        var (s4, p4, m4, f4) = Duel(33);
        f4.Tool = ArenaFixture.Give(s4, 1 - p4, "at_purity_charm"); s4.Players[1 - p4].Hand.Remove(f4.Tool);
        var poisoner = R.Cards.First(c => c.Kind == "pet" && c.Attacks.Count > 0 && c.Attacks[0].Effects.Any(e => e.Op == "status" && e.Status == "poisoned" && e.Cond == null));
        m4.DefId = poisoner.Id; foreach (var t in poisoner.Attacks[0].Cost) m4.Energy.Add(t == "colorless" ? poisoner.Element : t);
        ArenaFixture.Do(s4, p4, new ArenaAction { Type = "attack", Index = 0 });
        Assert.Contains(s4.Log, e => e.Type == "immune");
        Assert.False(f4.Poisoned);
    }

    // ---------------------------------------------------------------- knockouts and winning

    [Fact]
    public void Knockouts_award_points_ex_counts_double_promotion_is_required_and_three_points_win()
    {
        var (st, p, me, foe) = Duel(41);
        int o = 1 - p;
        var ex = ArenaFixture.Pick("pet", c => c.Ex);
        foe.DefId = ex; foe.Damage = R.Def(ex).Hp - 10;
        var hitter = ArenaFixture.Pick("pet", c => c.IsBasic && c.Attacks[0].Damage >= 20 && c.Attacks[0].Effects.Count == 0 && c.Attacks[0].Cost.Count == 1);
        me.DefId = hitter; me.Energy.Add(R.Def(hitter).Attacks[0].Cost[0] == "colorless" ? R.Def(hitter).Element : R.Def(hitter).Attacks[0].Cost[0]);
        bool foeHadBench = st.Players[o].Bench.Count > 0;
        ArenaFixture.Do(st, p, new ArenaAction { Type = "attack", Index = 0 });
        Assert.Equal(2, st.Players[p].Points);                                      // EX knockout = 2 points
        Assert.Contains(st.Log, e => e.Type == "knockout" && e.N == 2);
        if (foeHadBench)
        {
            Assert.True(st.Players[o].MustPromote);
            Assert.Equal("promote_required", ArenaFixture.Err(st, o, new ArenaAction { Type = "end" }));
            Assert.Equal("waiting_for_opponent", ArenaFixture.Err(st, p, new ArenaAction { Type = "end" }));
            ArenaFixture.Do(st, o, new ArenaAction { Type = "promote", Uid = st.Players[o].Bench[0].Uid });
            Assert.Equal(o, st.Active);
            // one more knockout of a normal Pet → 3 points → win
            ArenaFixture.Do(st, o, new ArenaAction { Type = "end" });
            var foe2 = st.Players[o].Active!; foe2.DefId = ArenaFixture.Pick("pet", c => c.IsBasic && !c.Ex); foe2.Damage = R.Def(foe2.DefId).Hp - 10; foe2.Tool = 0;
            st.Players[p].Active!.Special = null;
            ArenaFixture.Do(st, p, new ArenaAction { Type = "attack", Index = 0 });
            Assert.True(st.Finished); Assert.Equal(p, st.Winner); Assert.Equal("points", st.EndReason);
        }
        else { Assert.True(st.Finished); Assert.Equal(p, st.Winner); Assert.Equal("no_pets", st.EndReason); }
    }

    [Fact]
    public void Simultaneous_knockouts_that_win_for_both_players_are_a_draw()
    {
        var (st, p, me, foe) = Duel(51);
        int o = 1 - p;
        st.Players[p].Points = 2; st.Players[o].Points = 2;
        foe.DefId = ArenaFixture.Pick("pet", c => c.IsBasic && !c.Ex); foe.Damage = R.Def(foe.DefId).Hp - 10;
        me.DefId = ArenaFixture.Pick("pet", c => c.IsBasic && !c.Ex && c.Attacks[0].Damage >= 20 && c.Attacks[0].Effects.Count == 0 && c.Attacks[0].Cost.Count == 1 && c.Ability == null);
        me.Damage = R.Def(me.DefId).Hp - 10;
        me.Energy.Add(R.Def(me.DefId).Attacks[0].Cost[0] == "colorless" ? R.Def(me.DefId).Element : R.Def(me.DefId).Attacks[0].Cost[0]);
        foe.Tool = ArenaFixture.Give(st, o, "at_ember_charm"); st.Players[o].Hand.Remove(foe.Tool);   // KO trigger strikes back for 30
        ArenaFixture.Do(st, p, new ArenaAction { Type = "attack", Index = 0 });
        Assert.True(st.Finished);
        Assert.Null(st.Winner);
        Assert.Equal("simultaneous", st.EndReason);
        Assert.Equal(2, st.Log.Count(e => e.Type == "knockout"));
    }

    [Fact]
    public void Retreat_costs_energy_once_per_turn_and_moving_to_bench_clears_conditions()
    {
        var (st, p, me, foe) = Duel(61);
        var pl = st.Players[p];
        if (pl.Bench.Count == 0) { var b = pl.Deck.Concat(pl.Hand).First(c => R.Def(st.Card(c).DefId).IsBasic); pl.Deck.Remove(b); pl.Hand.Remove(b); pl.Hand.Add(b); ArenaFixture.Do(st, p, new ArenaAction { Type = "bench", Card = b }); }
        me.DefId = ArenaFixture.Pick("pet", c => c.Retreat == 2);
        me.Energy.Clear(); me.Energy.Add("fire");
        Assert.Equal("not_enough_energy", ArenaFixture.Err(st, p, new ArenaAction { Type = "retreat", Uid = pl.Bench[0].Uid }));
        var boots = pl.Deck.Any(c => st.Card(c).DefId == "at_wind_boots") ? ArenaFixture.Give(st, p, "at_wind_boots") : 0;
        if (boots != 0) ArenaFixture.Do(st, p, new ArenaAction { Type = "item", Card = boots }); else me.Energy.Add("fire");
        me.Poisoned = true;
        int target = pl.Bench[0].Uid;
        ArenaFixture.Do(st, p, new ArenaAction { Type = "retreat", Uid = target });
        Assert.Equal(target, pl.Active!.Uid);
        Assert.False(me.Poisoned);
        if (pl.Bench.Count > 0) Assert.Equal("already_retreated", ArenaFixture.Err(st, p, new ArenaAction { Type = "retreat", Uid = pl.Bench[0].Uid }));
    }

    [Fact]
    public void Turn_limit_is_a_draw_and_three_timeouts_forfeit()
    {
        var st = ArenaFixture.New(71); ArenaFixture.SetupBoth(st);
        for (int i = 0; i < 200 && !st.Finished; i++) ArenaFixture.Do(st, st.Active, new ArenaAction { Type = "end" });
        Assert.True(st.Finished); Assert.Null(st.Winner); Assert.Equal("turn_limit", st.EndReason);
        Assert.Equal(R.Config.MaxTurnsPerPlayer, st.Players[0].TurnsTaken);
        var t = ArenaFixture.New(72);
        ArenaEngine.Timeout(R, t);                              // both late in setup: auto-setup + 1 timeout each
        Assert.Equal("main", t.Phase);
        Assert.All(t.Players, x => Assert.Equal(1, x.Timeouts));
        int late = t.Active;
        ArenaEngine.Timeout(R, t);
        ArenaEngine.Timeout(R, t);                              // late player's 2nd...
        for (int i = 0; i < 6 && !t.Finished; i++) ArenaEngine.Timeout(R, t);
        Assert.True(t.Finished); Assert.Equal("timeout", t.EndReason);
    }

    [Fact]
    public void Surrender_ends_the_match_and_finished_matches_reject_actions()
    {
        var st = ArenaFixture.New(81); ArenaFixture.SetupBoth(st);
        ArenaFixture.Do(st, 1 - st.Active, new ArenaAction { Type = "surrender" });
        Assert.Equal(st.Active, st.Winner);
        Assert.Equal("match_finished", ArenaFixture.Err(st, 0, new ArenaAction { Type = "end" }));
    }

    [Fact]
    public void A_chosen_bench_target_is_chosen_once_and_respected()
    {
        // regression: the choice used to be re-evaluated per Benched Pet, so a non-first target could be skipped
        var (st, p, me, foe) = Duel(91);
        var pl = st.Players[p]; var opp = st.Players[1 - p];
        while (opp.Bench.Count < 2) { var b = opp.Deck.Concat(opp.Hand).First(c => R.Def(st.Card(c).DefId).IsBasic); opp.Deck.Remove(b); opp.Hand.Remove(b); opp.Bench.Add(new ArenaPet { Uid = st.NextUid++, Owner = 1 - p, Cards = new() { b }, DefId = st.Card(b).DefId }); }
        var sniper = R.Cards.First(c => c.Kind == "pet" && c.Attacks.Any(a => a.Effects.Any(e => e.Op == "bench_damage" && e.Target == "opp_bench_chosen")));
        int idx = sniper.Attacks.FindIndex(a => a.Effects.Any(e => e.Target == "opp_bench_chosen"));
        me.DefId = sniper.Id; me.Special = null;
        foreach (var t in sniper.Attacks[idx].Cost) me.Energy.Add(t == "colorless" ? sniper.Element : t);
        var target = opp.Bench[1]; var other = opp.Bench[0];
        ArenaFixture.Do(st, p, new ArenaAction { Type = "attack", Index = idx, Targets = new() { target.Uid } });
        Assert.Contains(st.Log, e => e.Type == "damage" && e.A == target.Uid && e.S == "bench");
        Assert.DoesNotContain(st.Log, e => e.Type == "damage" && e.A == other.Uid && e.S == "bench");
        // a target that is not a legal choice is refused
        var (s2, p2, m2, f2) = Duel(92);
        m2.DefId = sniper.Id; foreach (var t in sniper.Attacks[idx].Cost) m2.Energy.Add(t == "colorless" ? sniper.Element : t);
        if (s2.Players[1 - p2].Bench.Count > 0)
        {
            int logBefore = s2.Log.Count, v = s2.Version;
            Assert.Equal("invalid_target", ArenaFixture.Err(s2, p2, new ArenaAction { Type = "attack", Index = idx, Targets = new() { m2.Uid } }));
            Assert.Equal(logBefore, s2.Log.Count);                       // refused before anything happened
            Assert.Equal(v, s2.Version);
        }
    }

    // ---------------------------------------------------------------- replay & simulation

    [Fact]
    public void Replaying_seed_and_actions_reproduces_the_match_exactly()
    {
        for (ulong s = 1; s <= 30; s++)
        {
            var st = ArenaFixture.New(s, ArenaFixture.Deck((int)s), ArenaFixture.Deck((int)s + 4));
            var actions = new List<(int p, ArenaAction a)>();
            while (!st.Finished && actions.Count < 3000)
            {
                int p = Enumerable.Range(0, 2).First(q => ArenaEngine.MustAct(st, q));
                var a = ArenaAi.Decide(R, st, p); actions.Add((p, a)); ArenaFixture.Do(st, p, a);
            }
            var re = ArenaFixture.New(s, ArenaFixture.Deck((int)s), ArenaFixture.Deck((int)s + 4));
            foreach (var (p, a) in actions) ArenaFixture.Do(re, p, a);
            Assert.Equal(st.Log.Select(e => e.Digest()), re.Log.Select(e => e.Digest()));
            Assert.Equal(st.Winner, re.Winner); Assert.Equal(st.Rng, re.Rng);
        }
    }

    [Fact]
    public void Ten_thousand_simulated_matches_keep_every_invariant_and_always_finish()
    {
        int games = 10_000, steps = 0, aiWins0 = 0, draws = 0;
        var reasons = new Dictionary<string, int>();
        for (int g = 0; g < games; g++)
        {
            ulong seed = (ulong)(g * 7919 + 13);
            var st = ArenaFixture.New(seed, ArenaFixture.Deck(g % 37), ArenaFixture.Deck((g * 5 + 3) % 41), pve: g % 10 == 0);
            var rnd = new SeededRandom(seed ^ 0xABCDEF);
            bool randomSide0 = g % 2 == 0, randomSide1 = g % 3 == 0;   // mix of random-legal and AI play
            int n = 0;
            while (!st.Finished)
            {
                Assert.True(++n < 5000, $"game {g} did not finish");
                int p = Enumerable.Range(0, 2).First(q => ArenaEngine.MustAct(st, q));
                ArenaAction a;
                if ((p == 0 && randomSide0) || (p == 1 && randomSide1))
                {
                    var legal = ArenaAi.LegalActions(R, st, p);
                    Assert.NotEmpty(legal);
                    a = legal[rnd.Next(legal.Count)];
                    if (a.Type == "end" && legal.Count > 1 && rnd.Next(3) > 0) a = legal[rnd.Next(legal.Count - 1)];
                }
                else a = ArenaAi.Decide(R, st, p);
                ArenaFixture.Do(st, p, a);
                Invariants(st, g);
            }
            steps += n;
            if (st.Winner == null) draws++; else if (st.Winner == 0) aiWins0++;
            reasons[st.EndReason!] = reasons.GetValueOrDefault(st.EndReason!) + 1;
        }
        output.WriteLine($"{games} matches, {steps} actions, draws {draws}, reasons: {string.Join(", ", reasons.Select(kv => kv.Key + "=" + kv.Value))}");
        Assert.True(reasons.GetValueOrDefault("points") > games / 3);
    }

    private static void Invariants(ArenaState st, int g)
    {
        var cfg = R.Config;
        for (int p = 0; p < 2; p++)
        {
            var pl = st.Players[p];
            var inPlay = pl.InPlay().SelectMany(x => x.Cards.Concat(x.Tool != 0 ? new[] { x.Tool } : Array.Empty<int>())).ToList();
            var all = pl.Deck.Concat(pl.Hand).Concat(pl.Discard).Concat(inPlay).ToList();
            Assert.True(all.Count == 20 && all.Distinct().Count() == 20, $"g{g}: card conservation broken for p{p}");
            Assert.All(all, c => Assert.Equal(p, st.Card(c).Owner));
            Assert.True(pl.Bench.Count <= cfg.BenchSize, $"g{g}: bench");
            Assert.True(pl.Hand.Count <= cfg.HandLimit, $"g{g}: hand {pl.Hand.Count}");
            Assert.InRange(pl.Points, 0, 7);
            foreach (var pet in pl.InPlay())
            {
                Assert.True(pet.Damage >= 0 && pet.Damage < ArenaEngine.MaxHp(R, st, pet), $"g{g}: damage {pet.Damage}");
                Assert.Equal(p, pet.Owner);
                Assert.True(R.Def(pet.DefId).Kind == "pet");
                Assert.False(pet.Special != null && !new[] { "asleep", "paralyzed", "confused" }.Contains(pet.Special));
            }
            if (st.Phase == "main" && !st.Finished && pl.Active == null) Assert.True(pl.MustPromote, $"g{g}: no active without a promotion");
        }
        if (st.Finished) Assert.True(st.Winner == null || st.Winner is 0 or 1);
    }
}
