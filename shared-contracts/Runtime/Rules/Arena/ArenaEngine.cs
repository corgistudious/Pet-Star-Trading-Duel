using System;
using System.Collections.Generic;
using System.Linq;

namespace PetStar.Rules.Arena
{
    /// <summary>
    /// Pet Battle Arena rules engine (original implementation). Pure functions over ArenaState: Create → Apply(action)*.
    /// Turn flow: TURN_START → DRAW → ENERGY (zone) → MAIN (bench/evolve/attach/item/tool/supporter/ability/retreat)
    /// → ATTACK resolution → KO check (+ promotions) → BETWEEN_TURNS checkup → KO check → next TURN_START, or FINISHED.
    /// Every effect resolves in card order; triggered effects (tools/abilities) cannot trigger again (depth guard).
    /// </summary>
    public static class ArenaEngine
    {
        // ------------------------------------------------------------ creation & setup

        public static ArenaState Create(ArenaRules rules, ArenaSetupPlayer a, ArenaSetupPlayer b, ulong seed, bool pve = false)
        {
            var cfg = rules.Config;
            var st = new ArenaState { Rng = seed, DefsVersion = rules.Version, MaxTurns = pve ? cfg.PveMaxTurnsPerPlayer : cfg.MaxTurnsPerPlayer };
            foreach (var (sp, p) in new[] { (a, 0), (b, 1) })
            {
                var pl = new ArenaPlayerState { Name = sp.Name, EnergyTypes = sp.EnergyTypes.Count > 0 ? sp.EnergyTypes.ToList() : ArenaDecks.DefaultEnergy(rules, sp.Deck.Select(d => d.DefId)) };
                foreach (var c in sp.Deck)
                {
                    var card = new ArenaCard { Id = st.Cards.Count + 1, DefId = c.DefId, Owner = p, CardInstanceId = c.CardInstanceId };
                    st.Cards.Add(card);
                    pl.Deck.Add(card.Id);
                }
                st.Players.Add(pl);
            }
            for (int p = 0; p < 2; p++) { Shuffle(st, st.Players[p].Deck); DealOpening(rules, st, p); }
            st.First = R(st, 2);
            Log(st, "first", st.First);
            for (int p = 0; p < 2; p++) st.Players[p].ZoneNext = RollEnergy(st, st.Players[p]);
            return st;
        }

        /// <summary>Opening hand of StartHand cards with at least one Basic Pet: the hand is returned and the deck reshuffled
        /// until one appears (no card is created or moved between decks). Deck validation guarantees a Basic exists.</summary>
        private static void DealOpening(ArenaRules rules, ArenaState st, int p)
        {
            var pl = st.Players[p];
            for (int attempt = 0; attempt < 64; attempt++)
            {
                for (int i = 0; i < rules.Config.StartHand && pl.Deck.Count > 0; i++) { pl.Hand.Add(pl.Deck[0]); pl.Deck.RemoveAt(0); }
                if (pl.Hand.Any(c => rules.Def(st.Card(c).DefId).IsBasic)) { if (attempt > 0) Log(st, "redeal", p, attempt); return; }
                pl.Deck.AddRange(pl.Hand); pl.Hand.Clear();
                Shuffle(st, pl.Deck);
            }
            // practically unreachable (needs 64 basic-less hands in a row): take the first Basic in deck order for the last slot
            for (int i = 0; i < rules.Config.StartHand && pl.Deck.Count > 0; i++) { pl.Hand.Add(pl.Deck[0]); pl.Deck.RemoveAt(0); }
            int bi = pl.Deck.FindIndex(c => rules.Def(st.Card(c).DefId).IsBasic);
            if (bi >= 0 && pl.Hand.Count > 0) { int back = pl.Hand[pl.Hand.Count - 1]; pl.Hand[pl.Hand.Count - 1] = pl.Deck[bi]; pl.Deck[bi] = back; }
            Log(st, "redeal", p, 64);
        }

        public static void Apply(ArenaRules rules, ArenaState st, int p, ArenaAction a)
        {
            if (st.Finished) throw new ArenaRuleException("match_finished");
            if (p != 0 && p != 1) throw new ArenaRuleException("invalid_player");
            if (a.Type == "surrender") { Finish(st, 1 - p, "surrender"); Bump(st); return; }
            if (st.Phase == "setup") { if (a.Type != "setup") throw new ArenaRuleException("setup_required"); Setup(rules, st, p, a); Bump(st); return; }
            var pl = st.Players[p];
            if (AnyPromote(st))
            {
                if (!pl.MustPromote) throw new ArenaRuleException("waiting_for_opponent");
                if (a.Type != "promote") throw new ArenaRuleException("promote_required");
                Promote(rules, st, p, a.Uid);
                Bump(st); return;
            }
            if (st.Active != p) throw new ArenaRuleException("not_your_turn");
            if (++st.ActionsThisTurn > rules.Config.MaxActionsPerTurn) throw new ArenaRuleException("too_many_actions");
            switch (a.Type)
            {
                case "bench": PlayBasic(rules, st, p, a.Card); break;
                case "evolve": Evolve(rules, st, p, a.Card, a.Uid); break;
                case "attach": Attach(st, p, a.Uid); break;
                case "item": case "supporter": case "tool": PlayTrainer(rules, st, p, a); break;
                case "ability": UseAbility(rules, st, p, a); break;
                case "retreat": Retreat(rules, st, p, a.Uid); break;
                case "attack": Attack(rules, st, p, a.Index, a.Targets); break;
                case "end": EndTurn(rules, st, p); break;
                default: throw new ArenaRuleException("invalid_action");
            }
            Bump(st);
        }

        private static void Bump(ArenaState st) => st.Version++;

        private static void Setup(ArenaRules rules, ArenaState st, int p, ArenaAction a)
        {
            var pl = st.Players[p];
            if (pl.SetupDone) throw new ArenaRuleException("setup_done");
            var picks = new List<int> { a.Card }.Concat(a.Bench).ToList();
            if (picks.Distinct().Count() != picks.Count || a.Bench.Count > rules.Config.BenchSize) throw new ArenaRuleException("invalid_setup");
            foreach (var c in picks)
                if (!pl.Hand.Contains(c) || !rules.Def(st.Card(c).DefId).IsBasic) throw new ArenaRuleException("setup_needs_basic");
            pl.Active = NewPet(st, p, a.Card, 0);
            foreach (var c in a.Bench) pl.Bench.Add(NewPet(st, p, c, 0));
            foreach (var c in picks) pl.Hand.Remove(c);
            pl.SetupDone = true;
            Log(st, "setup", p, pl.Bench.Count);
            if (st.Players.All(x => x.SetupDone)) { st.Phase = "main"; StartTurn(rules, st, st.First); }
        }

        private static ArenaPet NewPet(ArenaState st, int p, int card, int turn) =>
            new ArenaPet { Uid = st.NextUid++, Owner = p, Cards = new List<int> { card }, DefId = st.Card(card).DefId, PlayedTurn = turn };

        // ------------------------------------------------------------ turn flow

        private static void StartTurn(ArenaRules rules, ArenaState st, int p)
        {
            var cfg = rules.Config; var pl = st.Players[p];
            if (pl.TurnsTaken >= st.MaxTurns) { Finish(st, null, "turn_limit"); return; }
            st.Active = p; st.Turn++; st.ActionsThisTurn = 0; st.Continuation = null;
            pl.TurnsTaken++; pl.EnergyAttached = false; pl.SupportersUsed = 0; pl.Retreats = 0;
            ExpireMods(st);
            Log(st, "turn", p, st.Turn);
            bool firstTurnOfFirst = p == st.First && pl.TurnsTaken == 1;
            if (!firstTurnOfFirst || cfg.FirstPlayerDraws) Draw(rules, st, p, 1);
            if (!cfg.EnergyCarryOver || pl.ZoneCurrent == null)
                pl.ZoneCurrent = firstTurnOfFirst && !cfg.FirstPlayerEnergy ? null : pl.ZoneNext;
            pl.ZoneNext = RollEnergy(st, pl);
            Log(st, "energy_zone", p, 0, 0, pl.ZoneCurrent ?? "-");
        }

        private static void ExpireMods(ArenaState st)
        {
            foreach (var pl in st.Players)
            {
                pl.TurnMods.RemoveAll(m => m.UntilTurn < st.Turn);
                foreach (var pet in pl.InPlay()) pet.Mods.RemoveAll(m => m.UntilTurn < st.Turn);
            }
        }

        private static void EndTurn(ArenaRules rules, ArenaState st, int p)
        {
            var pl = st.Players[p];
            if (!rules.Config.EnergyCarryOver) pl.ZoneCurrent = null;
            Log(st, "end_turn", p);
            st.Continuation = "checkup";
            Continue(rules, st);
        }

        /// <summary>Runs the between-turns steps; stops while a player must choose a new Active Pet.</summary>
        private static void Continue(ArenaRules rules, ArenaState st)
        {
            while (!st.Finished && !AnyPromote(st) && st.Continuation != null)
            {
                if (st.Continuation == "checkup")
                {
                    st.Continuation = "next_turn";
                    Checkup(rules, st);
                    ResolveKnockouts(rules, st, null, 0);
                }
                else if (st.Continuation == "next_turn")
                {
                    StartTurn(rules, st, 1 - st.Active);
                }
            }
        }

        /// <summary>Between turns: Poisoned, Burned, Asleep, Paralyzed (in that order) for both Active Pets, then between-turn abilities.</summary>
        private static void Checkup(ArenaRules rules, ArenaState st)
        {
            var cfg = rules.Config; int ended = st.Active;
            foreach (int q in new[] { ended, 1 - ended })
            {
                var pet = st.Players[q].Active; if (pet == null) continue;
                if (pet.Poisoned) { pet.Damage += cfg.PoisonDamage; Log(st, "poison", q, pet.Uid, cfg.PoisonDamage); }
                if (pet.Burned)
                {
                    pet.Damage += cfg.BurnDamage; Log(st, "burn", q, pet.Uid, cfg.BurnDamage);
                    if (cfg.BurnCureOnHeads && Flip(st, q, "burn")) { pet.Burned = false; Log(st, "recover", q, pet.Uid, 0, "burned"); }
                }
                if (pet.Special == "asleep" && Flip(st, q, "asleep")) { pet.Special = null; Log(st, "recover", q, pet.Uid, 0, "asleep"); }
                if (pet.Special == "paralyzed" && pet.Owner == ended && pet.SpecialTurn < st.Turn) { pet.Special = null; Log(st, "recover", q, pet.Uid, 0, "paralyzed"); }
            }
            foreach (int q in new[] { ended, 1 - ended })
            {
                var pet = st.Players[q].Active; if (pet == null) continue;
                var ab = rules.Def(pet.DefId).Ability;
                if (ab == null || ab.Trigger != "between_turns" || AbilityLocked(st, pet)) continue;
                RunEffects(rules, st, q, pet, null, ab.Effects, new List<int>(), "ability:" + ab.Id, 1);
            }
        }

        private static void Promote(ArenaRules rules, ArenaState st, int p, int uid)
        {
            var pl = st.Players[p];
            var pet = pl.Bench.FirstOrDefault(b => b.Uid == uid) ?? throw new ArenaRuleException("invalid_target");
            pl.Bench.Remove(pet); pl.Active = pet; pl.MustPromote = false;
            Log(st, "promote", p, pet.Uid);
            Continue(rules, st);
        }

        public static bool AnyPromote(ArenaState st) => st.Players.Any(x => x.MustPromote);

        /// <summary>Who must act now: during setup any player who has not set up; a pending promotion; else the active player.</summary>
        public static bool MustAct(ArenaState st, int p)
        {
            if (st.Finished) return false;
            if (st.Phase == "setup") return !st.Players[p].SetupDone;
            if (AnyPromote(st)) return st.Players[p].MustPromote;
            return st.Active == p;
        }

        // ------------------------------------------------------------ main-phase actions

        private static void PlayBasic(ArenaRules rules, ArenaState st, int p, int card)
        {
            var pl = st.Players[p];
            if (!pl.Hand.Contains(card)) throw new ArenaRuleException("card_not_in_hand");
            if (!rules.Def(st.Card(card).DefId).IsBasic) throw new ArenaRuleException("not_a_basic_pet");
            if (pl.Bench.Count >= rules.Config.BenchSize) throw new ArenaRuleException("bench_full");
            pl.Hand.Remove(card);
            var pet = NewPet(st, p, card, st.Turn);
            pl.Bench.Add(pet);
            Log(st, "bench", p, pet.Uid, card, pet.DefId);
        }

        private static void Evolve(ArenaRules rules, ArenaState st, int p, int card, int uid)
        {
            var pl = st.Players[p];
            if (!pl.Hand.Contains(card)) throw new ArenaRuleException("card_not_in_hand");
            var def = rules.Def(st.Card(card).DefId);
            if (def.Kind != "pet" || def.Stage == 0) throw new ArenaRuleException("not_an_evolution");
            if (rules.Config.NoEvolveFirstTurn && pl.TurnsTaken == 1) throw new ArenaRuleException("no_evolve_first_turn");
            var pet = pl.InPlay().FirstOrDefault(x => x.Uid == uid) ?? throw new ArenaRuleException("invalid_target");
            if (def.EvolvesFrom != pet.DefId) throw new ArenaRuleException("wrong_evolution_line");
            if (pet.PlayedTurn == st.Turn) throw new ArenaRuleException("played_this_turn");
            if (pet.EvolvedTurn == st.Turn) throw new ArenaRuleException("evolved_this_turn");
            pl.Hand.Remove(card);
            pet.Cards.Add(card); pet.DefId = def.Id; pet.EvolvedTurn = st.Turn;
            ClearConditions(pet);                         // evolving removes Special Conditions; energy, tool and damage stay
            Log(st, "evolve", p, pet.Uid, card, def.Id);
            ResolveKnockouts(rules, st, null, 0);         // a lower max HP (not in the current card pool) would KO here
        }

        private static void Attach(ArenaState st, int p, int uid)
        {
            var pl = st.Players[p];
            if (pl.EnergyAttached) throw new ArenaRuleException("energy_already_attached");
            if (pl.ZoneCurrent == null) throw new ArenaRuleException("no_energy_available");
            var pet = pl.InPlay().FirstOrDefault(x => x.Uid == uid) ?? throw new ArenaRuleException("invalid_target");
            pet.Energy.Add(pl.ZoneCurrent);
            Log(st, "attach", p, pet.Uid, 0, pl.ZoneCurrent);
            pl.ZoneCurrent = null; pl.EnergyAttached = true;
        }

        private static void PlayTrainer(ArenaRules rules, ArenaState st, int p, ArenaAction a)
        {
            var pl = st.Players[p];
            if (!pl.Hand.Contains(a.Card)) throw new ArenaRuleException("card_not_in_hand");
            var def = rules.Def(st.Card(a.Card).DefId);
            if (def.Kind != a.Type) throw new ArenaRuleException("wrong_card_kind");
            if (def.Kind == "supporter")
            {
                if (pl.SupportersUsed >= rules.Config.SupportersPerTurn) throw new ArenaRuleException("supporter_used");
                if (!rules.Config.SupporterFirstTurn && pl.TurnsTaken == 1) throw new ArenaRuleException("no_supporter_first_turn");
            }
            if (def.Kind == "tool")
            {
                var holder = pl.InPlay().FirstOrDefault(x => x.Uid == a.Uid) ?? throw new ArenaRuleException("invalid_target");
                if (holder.Tool != 0) throw new ArenaRuleException("tool_already_attached");
                pl.Hand.Remove(a.Card);
                holder.Tool = a.Card;
                Log(st, "tool", p, holder.Uid, a.Card, def.Id);
                return;
            }
            if (!TrainerPlayable(rules, st, p, def, a.Targets)) throw new ArenaRuleException("no_valid_target");
            pl.Hand.Remove(a.Card);
            if (def.Kind == "supporter") pl.SupportersUsed++;
            Log(st, def.Kind, p, 0, a.Card, def.Id);
            RunEffects(rules, st, p, pl.Active, null, def.Effects, a.Targets, def.Kind + ":" + def.Id, 0);
            pl.Discard.Add(a.Card);
            ResolveKnockouts(rules, st, null, 0);
        }

        /// <summary>A trainer whose required choice has no legal target cannot be played (it would do nothing).</summary>
        public static bool TrainerPlayable(ArenaRules rules, ArenaState st, int p, ArenaCardDef def, List<int> targets)
        {
            var pl = st.Players[p]; var opp = st.Players[1 - p];
            bool heals = def.Effects.Any(e => e.Op == "heal" && e.Target == "own_chosen"), cures = def.Effects.Any(e => e.Op == "cure");
            if (heals && !pl.InPlay().Any(x => x.Damage > 0 || (cures && (x.Poisoned || x.Burned || x.Special != null)))) return false;
            foreach (var e in def.Effects)
            {
                if (e.Target == "own_bench_chosen" && (pl.Bench.Count == 0 || pl.Active == null)) return false;
                if (e.Target == "opp_bench_chosen" && (opp.Bench.Count == 0 || opp.Active == null)) return false;
                if (e.Op == "energy_transfer" && !pl.Bench.Any(b => b.Energy.Count > 0)) return false;
                if (e.Op == "energy_discard" && (opp.Active == null || opp.Active.Energy.Count == 0)) return false;
                if (e.Op == "switch_self" && pl.Active == null) return false;
            }
            return true;
        }

        private static void UseAbility(ArenaRules rules, ArenaState st, int p, ArenaAction a)
        {
            var pl = st.Players[p];
            var pet = pl.InPlay().FirstOrDefault(x => x.Uid == a.Uid) ?? throw new ArenaRuleException("invalid_target");
            var ab = rules.Def(pet.DefId).Ability;
            if (ab == null || ab.Trigger != "activated") throw new ArenaRuleException("no_ability");
            if (pet.AbilityTurn == st.Turn) throw new ArenaRuleException("ability_used");
            if (AbilityLocked(st, pet)) throw new ArenaRuleException("ability_locked");
            if (ab.Effects.Any(e => e.Cond == "is_active") && pl.Active != pet) throw new ArenaRuleException("must_be_active");
            if (ab.Effects.All(e => e.Op == "heal") && (pl.Active == null || pl.Active.Damage == 0)) throw new ArenaRuleException("nothing_to_heal");
            pet.AbilityTurn = st.Turn;
            Log(st, "ability", p, pet.Uid, 0, ab.Id);
            RunEffects(rules, st, p, pet, null, ab.Effects, a.Targets, "ability:" + ab.Id, 0);
        }

        public static int RetreatCost(ArenaRules rules, ArenaState st, int p)
        {
            var pl = st.Players[p]; var pet = pl.Active; if (pet == null) return 99;
            int c = rules.Def(pet.DefId).Retreat + pl.TurnMods.Where(m => m.Kind == "retreat_mod").Sum(m => m.Amount) + ActiveMods(st, pet, "retreat_mod").Sum(m => m.Amount);
            return Math.Max(0, c);
        }

        private static void Retreat(ArenaRules rules, ArenaState st, int p, int uid)
        {
            var pl = st.Players[p]; var pet = pl.Active ?? throw new ArenaRuleException("no_active");
            if (pl.Retreats >= rules.Config.RetreatsPerTurn) throw new ArenaRuleException("already_retreated");
            if (pet.Special == "asleep" || pet.Special == "paralyzed") throw new ArenaRuleException("cannot_retreat_now");
            int cost = RetreatCost(rules, st, p);
            if (cost >= 50) throw new ArenaRuleException("retreat_blocked");
            if (pet.Energy.Count < cost) throw new ArenaRuleException("not_enough_energy");
            var to = pl.Bench.FirstOrDefault(b => b.Uid == uid) ?? throw new ArenaRuleException("invalid_target");
            for (int i = 0; i < cost; i++) pet.Energy.RemoveAt(pet.Energy.Count - 1);
            SwapIn(pl, to);
            pl.Retreats++;
            Log(st, "retreat", p, pet.Uid, to.Uid, cost.ToString());
        }

        /// <summary>Moves a Benched Pet to Active; the old Active goes to the Bench and loses Special Conditions and attack effects.</summary>
        private static void SwapIn(ArenaPlayerState pl, ArenaPet to)
        {
            var old = pl.Active;
            pl.Bench.Remove(to);
            if (old != null) { ClearConditions(old); old.Mods.Clear(); pl.Bench.Add(old); }
            pl.Active = to;
        }

        private static void ClearConditions(ArenaPet pet) { pet.Poisoned = false; pet.Burned = false; pet.Special = null; }

        // ------------------------------------------------------------ attacks

        public static bool AttackBanned(ArenaRules rules, ArenaState st, int p)
        {
            var pl = st.Players[p];
            return rules.Config.AttackBanFirstTurn == "both" ? pl.TurnsTaken == 1 : (p == st.First && pl.TurnsTaken == 1);
        }

        public static bool CostPaid(IList<string> energy, IList<string> cost)
        {
            var pool = energy.ToList();
            foreach (var c in cost.Where(c => c != "colorless")) { int i = pool.IndexOf(c); if (i < 0) return false; pool.RemoveAt(i); }
            return pool.Count >= cost.Count(c => c == "colorless");
        }

        public static string? AttackProblem(ArenaRules rules, ArenaState st, int p, int index)
        {
            var pl = st.Players[p]; var pet = pl.Active;
            if (pet == null) return "no_active";
            if (AttackBanned(rules, st, p)) return "no_attack_first_turn";
            var def = rules.Def(pet.DefId);
            if (index < 0 || index >= def.Attacks.Count) return "invalid_attack";
            if (pet.Special == "asleep" || pet.Special == "paralyzed") return "cannot_attack_now";
            if (ActiveMods(st, pet, "attack_lock").Any()) return "attack_locked";
            if (!CostPaid(pet.Energy, def.Attacks[index].Cost)) return "not_enough_energy";
            if (st.Players[1 - p].Active == null) return "no_target";
            return null;
        }

        private static void Attack(ArenaRules rules, ArenaState st, int p, int index, List<int> targets)
        {
            var problem = AttackProblem(rules, st, p, index);
            if (problem != null) throw new ArenaRuleException(problem);
            var pl = st.Players[p]; var att = pl.Active!; var atk = rules.Def(att.DefId).Attacks[index];
            Log(st, "attack", p, att.Uid, index, atk.Id);
            if (att.Special == "confused" && !Flip(st, p, "confused"))
            {
                Log(st, "attack_failed", p, att.Uid, 0, "confused");
                if (rules.Config.ConfusedSelfDamage > 0) { att.Damage += rules.Config.ConfusedSelfDamage; Log(st, "damage", p, att.Uid, rules.Config.ConfusedSelfDamage, "confused"); }
            }
            else
            {
                RunEffects(rules, st, p, att, atk, atk.Effects, targets, "attack:" + atk.Id, 0);
            }
            ResolveKnockouts(rules, st, att, 0);
            if (!st.Finished) EndTurn(rules, st, p);
        }

        /// <summary>Damage from an attack to the opponent's Active: base (+coins, +conditions, +boosts) → weakness → reductions → prevention.</summary>
        public static int AttackDamage(ArenaRules rules, ArenaState st, int p, ArenaPet att, ArenaAttack atk, ArenaPet def, int baseExtra)
        {
            int dmg = atk.Damage + baseExtra;
            if (dmg <= 0) return 0;
            dmg += st.Players[p].TurnMods.Where(m => m.Kind == "damage_boost").Sum(m => m.Amount);
            var ab = rules.Def(att.DefId).Ability;
            if (ab != null && ab.Trigger == "passive" && !AbilityLocked(st, att))
                foreach (var e in ab.Effects.Where(e => e.Op == "damage_boost"))
                    if (e.Cond != "self_damaged" || att.Damage > 0) dmg += e.Amount;
            dmg -= ActiveMods(st, att, "attack_reduce").Sum(m => m.Amount);
            if (dmg <= 0) return 0;
            if (rules.Def(def.DefId).Weakness == rules.Def(att.DefId).Element) dmg += rules.Config.WeaknessBonus;
            dmg -= ActiveMods(st, def, "shield").Sum(m => m.Amount);
            var dab = rules.Def(def.DefId).Ability;
            if (dab != null && dab.Trigger == "passive" && !AbilityLocked(st, def))
                dmg -= dab.Effects.Where(e => e.Op == "reduce_taken").Sum(e => e.Amount);
            if (ActiveMods(st, def, "prevent").Any()) return 0;
            return Math.Max(0, dmg);
        }

        // ------------------------------------------------------------ effect engine

        /// <summary>
        /// Resolves a card's effects in order. `chosen` supplies player choices (uids) for *_chosen targets in order.
        /// `atk` is set for attacks (the first damage step uses it). `depth` guards trigger chains.
        /// </summary>
        private static void RunEffects(ArenaRules rules, ArenaState st, int p, ArenaPet? src, ArenaAttack? atk, List<ArenaEffect> effects, List<int> chosen, string source, int depth)
        {
            if (depth > rules.Config.MaxTriggerDepth) return;
            var pl = st.Players[p]; var opp = st.Players[1 - p];
            int choice = 0;
            int Choose(IEnumerable<ArenaPet> pool)
            {
                var list = pool.ToList();
                if (list.Count == 0) return 0;
                if (choice < chosen.Count) { int uid = chosen[choice++]; if (list.Any(x => x.Uid == uid)) return uid; throw new ArenaRuleException("invalid_target"); }
                return list.Count == 1 ? list[0].Uid : list.OrderBy(x => x.Uid).First().Uid;   // missing choice: lowest uid (deterministic)
            }

            int extra = 0; bool attackDone = atk == null;
            int dealt = 0;
            ArenaPet? defender = opp.Active;
            // pre-damage modifiers (coins / conditions) are collected before the damage is dealt
            if (atk != null)
            {
                foreach (var e in effects)
                {
                    if (e.Op == "coin_damage") { int heads = 0; for (int i = 0; i < Math.Max(1, e.Coins); i++) if (Flip(st, p, "coin_damage")) heads++; extra += heads * e.Amount; }
                    else if (e.Op == "bonus" && defender != null && Cond(rules, st, p, src, defender, e.Cond)) extra += e.Amount;
                }
                if (defender != null)
                {
                    dealt = AttackDamage(rules, st, p, src!, atk, defender, extra);
                    if (dealt > 0 || atk.Damage + extra > 0) Damage(st, defender, dealt, p, "attack");
                }
                attackDone = true;
            }
            foreach (var e in effects)
            {
                if (e.Op == "coin_damage" || e.Op == "bonus") continue;
                if (e.Cond == "heads" && !Flip(st, p, e.Op)) { Log(st, "fizzle", p, 0, 0, e.Op); continue; }
                if (e.Cond == "is_active" && src != null && pl.Active != src) continue;
                switch (e.Op)
                {
                    case "status":
                        if (defender != null && defender.Damage < MaxHp(rules, st, defender)) ApplyStatus(rules, st, defender, e.Status!);
                        break;
                    case "bench_damage":
                        var benchTargets = e.Target == "opp_bench_all" ? opp.Bench.ToList() : opp.Bench.Where(b => b.Uid == Choose(opp.Bench)).ToList();
                        foreach (var b in benchTargets) Damage(st, b, e.Amount, p, "bench");
                        break;
                    case "heal":
                        foreach (var t in HealTargets(rules, st, p, src, e.Target, Choose)) Heal(rules, st, t, e.Amount);
                        break;
                    case "cure":
                        var ct = e.Target == "own_chosen" ? pl.InPlay().FirstOrDefault(x => x.Uid == (chosen.Count > 0 ? chosen[0] : 0)) ?? src : src;
                        if (ct != null) { ClearConditions(ct); Log(st, "cure", ct.Owner, ct.Uid); }
                        break;
                    case "shield": if (src != null) AddMod(st, src, "shield", e.Amount, st.Turn + 1, st.Turn + 1, source); break;
                    case "prevent": if (src != null) AddMod(st, src, "prevent", 0, st.Turn + 1, st.Turn + 1, source); break;
                    case "attack_reduce": if (defender != null && opp.Active == defender) AddMod(st, defender, "attack_reduce", e.Amount, st.Turn + 1, st.Turn + 1, source); break;
                    case "attack_lock_self": if (src != null) AddMod(st, src, "attack_lock", 0, st.Turn + 1, st.Turn + 2, source); break;
                    case "attack_lock_target": if (opp.Active != null) AddMod(st, opp.Active, "attack_lock", 0, st.Turn + 1, st.Turn + 1, source); break;
                    case "ability_lock": if (opp.Active != null) AddMod(st, opp.Active, "ability_lock", 0, st.Turn + 1, st.Turn + 1, source); break;
                    case "retreat_mod":
                        if (e.Target == "own_active") pl.TurnMods.Add(new ArenaMod { Kind = "retreat_mod", Amount = e.Amount, FromTurn = st.Turn, UntilTurn = st.Turn, Source = source });
                        else if (opp.Active != null) AddMod(st, opp.Active, "retreat_mod", e.Amount, st.Turn + 1, st.Turn + 1, source);
                        break;
                    case "damage_boost":
                        if (e.Target == "own_turn") pl.TurnMods.Add(new ArenaMod { Kind = "damage_boost", Amount = e.Amount, FromTurn = st.Turn, UntilTurn = st.Turn, Source = source });
                        break;
                    case "draw": Draw(rules, st, p, Math.Max(1, e.Count)); break;
                    case "search":
                        var basics = pl.Deck.Where(c => rules.Def(st.Card(c).DefId).IsBasic).ToList();
                        if (basics.Count > 0 && pl.Hand.Count < rules.Config.HandLimit)
                        {
                            int pick = basics[R(st, basics.Count)];
                            pl.Deck.Remove(pick); pl.Hand.Add(pick);
                            Log(st, "search", p, 0, 1);
                        }
                        else Log(st, "fizzle", p, 0, 0, "search");
                        Shuffle(st, pl.Deck);
                        break;
                    case "shuffle": Shuffle(st, pl.Deck); Log(st, "shuffle", p); break;
                    case "switch_self":
                        { int uid = Choose(pl.Bench); var to = pl.Bench.FirstOrDefault(b => b.Uid == uid); if (to != null) { SwapIn(pl, to); Log(st, "switch", p, to.Uid); } }
                        break;
                    case "force_switch":
                        { int uid = Choose(opp.Bench); var to = opp.Bench.FirstOrDefault(b => b.Uid == uid); if (to != null) { SwapIn(opp, to); Log(st, "switch", 1 - p, to.Uid); } }
                        break;
                    case "energy_attach":
                        {
                            var el = e.Element ?? rules.Def(src?.DefId ?? pl.Active!.DefId).Element;
                            ArenaPet? to = e.Target == "self" ? src : e.Target == "own_active" ? pl.Active : pl.Bench.FirstOrDefault(b => b.Uid == Choose(pl.Bench));
                            if (to != null) for (int i = 0; i < Math.Max(1, e.Count); i++) { to.Energy.Add(el); Log(st, "energy_gain", p, to.Uid, 1, el); }
                        }
                        break;
                    case "energy_discard":
                        if (opp.Active != null && opp.Active.Energy.Count > 0)
                        {
                            int i = R(st, opp.Active.Energy.Count); var el = opp.Active.Energy[i];
                            opp.Active.Energy.RemoveAt(i); Log(st, "energy_discard", 1 - p, opp.Active.Uid, 1, el);
                        }
                        break;
                    case "energy_transfer":
                        {
                            int uid = Choose(pl.Bench.Where(b => b.Energy.Count > 0)); var from = pl.Bench.FirstOrDefault(b => b.Uid == uid);
                            if (from != null && pl.Active != null) { int n = from.Energy.Count; pl.Active.Energy.AddRange(from.Energy); from.Energy.Clear(); Log(st, "energy_move", p, from.Uid, n, pl.Active.Uid.ToString()); }
                        }
                        break;
                }
            }
            if (atk != null && dealt > 0 && src != null && depth == 0)
            {
                var ab = rules.Def(src.DefId).Ability;
                if (ab != null && ab.Trigger == "on_attack" && !AbilityLocked(st, src)) RunEffects(rules, st, p, src, null, ab.Effects, new List<int>(), "ability:" + ab.Id, depth + 1);
                // a tool on the defender may strike back once (never re-triggers)
                if (defender != null && defender.Tool != 0)
                {
                    var tool = rules.Def(st.Card(defender.Tool).DefId);
                    foreach (var te in tool.Effects.Where(x => x.Op == "retaliate")) { Damage(st, src, te.Amount, 1 - p, "retaliate"); }
                }
            }
        }

        private static IEnumerable<ArenaPet> HealTargets(ArenaRules rules, ArenaState st, int p, ArenaPet? src, string target, Func<IEnumerable<ArenaPet>, int> choose)
        {
            var pl = st.Players[p];
            switch (target)
            {
                case "self": if (src != null) yield return src; break;
                case "own_active": if (pl.Active != null) yield return pl.Active; break;
                case "own_all": foreach (var x in pl.InPlay().ToList()) yield return x; break;
                case "own_lowest":
                    var low = pl.InPlay().Where(x => x.Damage > 0).OrderByDescending(x => x.Damage).ThenBy(x => x.Uid).FirstOrDefault();
                    if (low != null) yield return low; break;
                case "own_chosen":
                    int uid = choose(pl.InPlay()); var t = pl.InPlay().FirstOrDefault(x => x.Uid == uid); if (t != null) yield return t; break;
            }
        }

        private static bool Cond(ArenaRules rules, ArenaState st, int p, ArenaPet? src, ArenaPet target, string? cond)
        {
            switch (cond)
            {
                case "target_status": return target.Poisoned || target.Burned || target.Special != null;
                case "target_damaged": return target.Damage > 0;
                case "target_ex": return rules.Def(target.DefId).Ex;
                case "self_damaged": return src != null && src.Damage > 0;
                default: return true;
            }
        }

        private static void ApplyStatus(ArenaRules rules, ArenaState st, ArenaPet pet, string status)
        {
            if (pet.Tool != 0 && rules.Def(st.Card(pet.Tool).DefId).Effects.Any(e => e.Op == "status_immunity")) { Log(st, "immune", pet.Owner, pet.Uid, 0, status); return; }
            switch (status)
            {
                case "poisoned": pet.Poisoned = true; break;
                case "burned": pet.Burned = true; break;
                default: pet.Special = status; pet.SpecialTurn = st.Turn; break;   // asleep / paralyzed / confused replace each other
            }
            Log(st, "status", pet.Owner, pet.Uid, 0, status);
        }

        private static void Damage(ArenaState st, ArenaPet pet, int amount, int byPlayer, string kind)
        {
            if (amount <= 0 && kind != "attack") return;
            pet.Damage += Math.Max(0, amount);
            Log(st, "damage", pet.Owner, pet.Uid, Math.Max(0, amount), kind);
        }

        private static void Heal(ArenaRules rules, ArenaState st, ArenaPet pet, int amount)
        {
            int h = Math.Min(pet.Damage, amount);
            if (h <= 0) return;
            pet.Damage -= h;
            Log(st, "heal", pet.Owner, pet.Uid, h);
        }

        private static void AddMod(ArenaState st, ArenaPet pet, string kind, int amount, int from, int until, string source)
        {
            pet.Mods.RemoveAll(m => m.Kind == kind && m.Source == source);   // the same source refreshes instead of stacking
            pet.Mods.Add(new ArenaMod { Kind = kind, Amount = amount, FromTurn = from, UntilTurn = until, Source = source });
            Log(st, "effect", pet.Owner, pet.Uid, amount, kind);
        }

        public static IEnumerable<ArenaMod> ActiveMods(ArenaState st, ArenaPet pet, string kind) =>
            pet.Mods.Where(m => m.Kind == kind && m.FromTurn <= st.Turn && st.Turn <= m.UntilTurn);

        public static bool AbilityLocked(ArenaState st, ArenaPet pet) => ActiveMods(st, pet, "ability_lock").Any();

        public static int MaxHp(ArenaRules rules, ArenaState st, ArenaPet pet)
        {
            int hp = rules.Def(pet.DefId).Hp;
            if (pet.Tool != 0) hp += rules.Def(st.Card(pet.Tool).DefId).Effects.Where(e => e.Op == "max_hp").Sum(e => e.Amount);
            return hp;
        }

        // ------------------------------------------------------------ knockouts & winning

        /// <summary>Removes every Pet whose damage reached its HP, awards KO points (EX = 2), runs KO triggers (bounded),
        /// asks for promotions and decides the match: points ≥ goal or an opponent without Pets wins; both at once = draw.</summary>
        private static void ResolveKnockouts(ArenaRules rules, ArenaState st, ArenaPet? attacker, int depth)
        {
            var cfg = rules.Config;
            for (int guard = 0; guard <= cfg.MaxTriggerDepth; guard++)
            {
                var kos = st.Players.SelectMany(pl => pl.InPlay()).Where(x => x.Damage >= MaxHp(rules, st, x)).OrderBy(x => x.Owner == st.Active ? 1 : 0).ThenBy(x => x.Uid).ToList();
                if (kos.Count == 0) break;
                foreach (var ko in kos)
                {
                    var owner = st.Players[ko.Owner];
                    if (owner.Active == ko) owner.Active = null; else owner.Bench.Remove(ko);
                    owner.Discard.AddRange(ko.Cards);
                    if (ko.Tool != 0) owner.Discard.Add(ko.Tool);
                    var def = rules.Def(ko.DefId);
                    st.Players[1 - ko.Owner].Points += def.KoPoints;
                    Log(st, "knockout", ko.Owner, ko.Uid, def.KoPoints, ko.DefId);
                    if (attacker != null && ko.Owner != attacker.Owner && ko.Tool != 0 && st.Players[attacker.Owner].InPlay().Contains(attacker))
                        foreach (var te in rules.Def(st.Card(ko.Tool).DefId).Effects.Where(e => e.Op == "on_ko_damage"))
                            Damage(st, attacker, te.Amount, ko.Owner, "ko_trigger");
                }
            }
            bool[] wins = new bool[2];
            for (int q = 0; q < 2; q++)
            {
                var me = st.Players[q]; var other = st.Players[1 - q];
                wins[q] = me.Points >= cfg.PointsToWin || (st.Phase != "setup" && !other.InPlay().Any());
            }
            if (wins[0] && wins[1]) { Finish(st, cfg.SimultaneousWin == "draw" ? (int?)null : st.Active, "simultaneous"); return; }
            if (wins[0] || wins[1])
            {
                int w = wins[0] ? 0 : 1;
                Finish(st, w, st.Players[w].Points >= cfg.PointsToWin ? "points" : "no_pets");
                return;
            }
            for (int q = 0; q < 2; q++)
            {
                var pl = st.Players[q];
                if (pl.Active == null && pl.Bench.Count > 0 && !pl.MustPromote) { pl.MustPromote = true; Log(st, "must_promote", q); }
            }
        }

        private static void Finish(ArenaState st, int? winner, string reason)
        {
            if (st.Finished) return;
            st.Phase = "finished"; st.Winner = winner; st.EndReason = reason;
            foreach (var pl in st.Players) pl.MustPromote = false;
            Log(st, "finish", winner ?? -1, 0, 0, reason);
        }

        // ------------------------------------------------------------ timers

        /// <summary>The acting player ran out of time: auto-setup, auto-promote or end the turn. Repeated timeouts forfeit.</summary>
        public static void Timeout(ArenaRules rules, ArenaState st)
        {
            if (st.Finished) return;
            var late = Enumerable.Range(0, 2).Where(q => MustAct(st, q)).ToList();
            foreach (int q in late)
            {
                var pl = st.Players[q];
                pl.Timeouts++;
                Log(st, "timeout", q, pl.Timeouts);
                if (pl.Timeouts >= rules.Config.TimeoutsToForfeit) { Finish(st, 1 - q, "timeout"); Bump(st); return; }
            }
            foreach (int q in late)
            {
                if (st.Finished) break;
                if (st.Phase == "setup") Setup(rules, st, q, ArenaAi.SetupChoice(rules, st, q));
                else if (st.Players[q].MustPromote) Promote(rules, st, q, ArenaAi.PromoteChoice(rules, st, q));
                else if (st.Active == q) EndTurn(rules, st, q);
            }
            Bump(st);
        }

        // ------------------------------------------------------------ helpers

        private static void Draw(ArenaRules rules, ArenaState st, int p, int n)
        {
            var pl = st.Players[p]; int drawn = 0;
            for (int i = 0; i < n; i++)
            {
                if (pl.Deck.Count == 0) { Log(st, "deck_empty", p); break; }        // an empty deck only stops drawing
                if (pl.Hand.Count >= rules.Config.HandLimit) { Log(st, "hand_full", p); break; }
                pl.Hand.Add(pl.Deck[0]); pl.Deck.RemoveAt(0); drawn++;
            }
            if (drawn > 0) Log(st, "draw", p, drawn);
        }

        private static string RollEnergy(ArenaState st, ArenaPlayerState pl) => pl.EnergyTypes.Count == 0 ? "colorless" : pl.EnergyTypes[R(st, pl.EnergyTypes.Count)];

        private static bool Flip(ArenaState st, int p, string why)
        {
            bool heads = R(st, 2) == 1;
            Log(st, "coin", p, heads ? 1 : 0, 0, why);
            return heads;
        }

        private static void Shuffle(ArenaState st, List<int> deck)
        {
            for (int i = deck.Count - 1; i > 0; i--) { int j = R(st, i + 1); (deck[i], deck[j]) = (deck[j], deck[i]); }
        }

        internal static int R(ArenaState st, int n)
        {
            var r = new SeededRandom(st.Rng);
            int v = r.Next(n);
            st.Rng = r.State;
            return v;
        }

        private static void Log(ArenaState st, string type, int p = -1, int a = 0, int n = 0, string s = "") =>
            st.Log.Add(new ArenaEvent { Type = type, P = p, A = a, N = n, S = s });
    }
}
