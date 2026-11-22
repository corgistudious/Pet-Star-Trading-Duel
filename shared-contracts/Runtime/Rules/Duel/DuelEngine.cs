using System;
using System.Collections.Generic;
using System.Linq;

namespace PetStar.Rules.Duel
{
    /// <summary>
    /// Pet Card Tactical Duel — the authoritative rules (server) also compiled into Unity (display/prediction only).
    /// Turn: Draw (1 card) → Main (summon, spells, set traps, evolve, move) → Battle (attacks / skills) → End
    /// (statuses, hand limit 7). The first player skips Battle on their first turn. LP 2500; LP only drops when a
    /// Pet is destroyed (−500, exactly once per destruction). Reactions: after a spell, attack, trap or summon the other
    /// player may respond with quick Spells / set Traps; links alternate and resolve last-in-first-out.
    /// All randomness (shuffle, coin, crits, status chances) comes from the seeded RNG held in the state.
    /// </summary>
    public static class DuelEngine
    {
        // ------------------------------------------------------------------ setup

        public static DuelState Create(DuelRules rules, DuelSetupPlayer a, DuelSetupPlayer b, ulong seed)
        {
            var st = new DuelState { Rng = seed };
            var setups = new[] { a, b };
            for (int p = 0; p < 2; p++)
            {
                var pl = new DuelPlayerState { Name = setups[p].Name, Lp = rules.Config.StartLp };
                for (int z = 0; z < rules.Config.SpellTrapZones; z++) pl.Zones.Add(null);
                foreach (var c in setups[p].Deck)
                {
                    var copy = new DuelCard { Id = st.Cards.Count + 1, Kind = c.Kind, TacticId = c.TacticId, CardInstanceId = c.CardInstanceId, Forms = c.Forms };
                    st.Cards.Add(copy);
                    pl.Deck.Add(copy.Id);
                }
                st.Players.Add(pl);
            }
            for (int p = 0; p < 2; p++) Shuffle(st, st.Players[p].Deck);
            st.First = R(st, 2);                                   // coin flip 50/50 (Blue = you, Red = opponent on each client)
            Log(st, "coin", st.First);
            for (int p = 0; p < 2; p++) Draw(st, p, rules.Config.StartHand, false);
            StartTurn(rules, st, st.First);
            return st;
        }

        private static void Shuffle(DuelState st, List<int> deck)
        {
            for (int i = deck.Count - 1; i > 0; i--)
            {
                int j = R(st, i + 1);
                (deck[i], deck[j]) = (deck[j], deck[i]);
            }
        }

        private static int R(DuelState st, int n)
        {
            var r = new SeededRandom(st.Rng);
            int v = r.Next(n);
            st.Rng = r.State;
            return v;
        }

        private static void Log(DuelState st, string type, int p = -1, int a = 0, int b = 0, int n = 0, string s = "") =>
            st.Log.Add(new DuelEvent { Type = type, P = p, A = a, B = b, N = n, S = s });

        // ------------------------------------------------------------------ turn flow

        private static void StartTurn(DuelRules rules, DuelState st, int p)
        {
            var cfg = rules.Config;
            st.Active = p; st.Turn++;
            var pl = st.Players[p];
            pl.TurnsTaken++;
            if (pl.TurnsTaken > cfg.MaxTurnsPerPlayer) { FinishByLimit(st); return; }
            pl.MaxEnergy = Math.Min(cfg.MaxEnergy, cfg.StartEnergy + (pl.TurnsTaken - 1) * cfg.EnergyPerTurn);
            pl.Energy = pl.MaxEnergy;
            pl.NormalSummonUsed = false;
            pl.UsedThisTurn.Clear();
            foreach (var u in pl.Units) { u.Acted = false; u.Moved = false; }
            st.Phase = "main";
            Log(st, "turn", p, st.Turn, pl.TurnsTaken);
            if (pl.Deck.Count == 0) { Finish(st, 1 - p, "deck_out"); return; }   // must draw and cannot: loses
            Draw(st, p, 1, true);
        }

        private static void Draw(DuelState st, int p, int count, bool phase)
        {
            var pl = st.Players[p];
            int drawn = 0;
            for (int i = 0; i < count && pl.Deck.Count > 0; i++) { pl.Hand.Add(pl.Deck[0]); pl.Deck.RemoveAt(0); drawn++; }
            if (drawn > 0) Log(st, phase ? "draw" : "draw_extra", p, 0, 0, drawn);
        }

        private static void EndTurn(DuelRules rules, DuelState st)
        {
            var cfg = rules.Config;
            int p = st.Active;
            var pl = st.Players[p];
            // End Phase: damage over time and regeneration on the active player's Pets
            foreach (var u in pl.Units.OrderBy(x => x.Zone).ToList())
            {
                if (Has(u, "burn")) ApplyDamage(rules, st, u, Math.Max(1, u.MaxHp * cfg.BurnPct / 100), "burn");
                if (Has(u, "poison")) ApplyDamage(rules, st, u, Math.Max(1, u.MaxHp * cfg.PoisonPct / 100), "poison");
                var pas = Passive(rules, u);
                if (pas != null && pas.Type == "regen_round_end" && u.Hp > 0) Heal(st, u, u.MaxHp * pas.Pct / 100);
            }
            ProcessDeaths(rules, st);
            if (st.Finished) return;
            foreach (var pp in st.Players)
                foreach (var u in pp.Units)
                {
                    u.Buffs.RemoveAll(bf => bf.UntilTurn <= st.Turn);
                    u.Statuses.RemoveAll(s => s.Type == "guard" || s.Type == "endure");
                    if (u.Owner == p)
                    {
                        foreach (var s in u.Statuses) s.Remaining--;
                        u.Statuses.RemoveAll(s => s.Remaining <= 0);
                    }
                }
            while (pl.Hand.Count > cfg.MaxHand)
            {
                int c = pl.Hand[pl.Hand.Count - 1];
                pl.Hand.RemoveAt(pl.Hand.Count - 1);
                pl.Grave.Add(c);
                Log(st, "discard", p, c);
            }
            Log(st, "end_turn", p, st.Turn);
            StartTurn(rules, st, 1 - p);
        }

        private static void Finish(DuelState st, int winner, string reason)
        {
            if (st.Finished) return;
            st.Phase = "finished"; st.Winner = winner; st.EndReason = reason; st.Priority = -1; st.Chain.Clear(); st.Event = null;
            Log(st, "finish", winner, 0, 0, 0, reason);
        }

        private static void FinishByLimit(DuelState st)
        {
            int lp0 = st.Players[0].Lp, lp1 = st.Players[1].Lp;
            if (lp0 != lp1) { Finish(st, lp0 > lp1 ? 0 : 1, "turn_limit_lp"); return; }
            int hp0 = st.Players[0].Units.Sum(u => u.Hp), hp1 = st.Players[1].Units.Sum(u => u.Hp);
            if (hp0 != hp1) { Finish(st, hp0 > hp1 ? 0 : 1, "turn_limit_hp"); return; }
            Finish(st, -1, "turn_limit_draw");
        }

        // ------------------------------------------------------------------ public API

        /// <summary>Applies one player intent. Throws <see cref="DuelRuleException"/> for anything illegal.</summary>
        public static void Apply(DuelRules rules, DuelState st, int p, DuelAction a)
        {
            if (st.Finished) throw new DuelRuleException("duel_finished");
            if (p < 0 || p > 1) throw new DuelRuleException("invalid_player");
            if (a.Type == "surrender") { Finish(st, 1 - p, "surrender"); st.Version++; return; }
            if (p != st.ToAct) throw new DuelRuleException("not_your_turn");
            if (st.Priority >= 0)
            {
                switch (a.Type)
                {
                    case "pass": Pass(rules, st, p); break;
                    case "activate": ActivateTrap(rules, st, p, a); break;
                    case "spell": ActivateSpell(rules, st, p, a); break;
                    default: throw new DuelRuleException("reaction_window_open");
                }
            }
            else
            {
                switch (a.Type)
                {
                    case "summon": Summon(rules, st, p, a, false); break;
                    case "spell": ActivateSpell(rules, st, p, a); break;
                    case "set": Set(rules, st, p, a); break;
                    case "evolve": Evolve(rules, st, p, a); break;
                    case "move": Move(rules, st, p, a); break;
                    case "battle": EnterBattle(st, p); break;
                    case "attack": case "skill": case "ultimate": Attack(rules, st, p, a); break;
                    case "end": EndTurn(rules, st); break;
                    default: throw new DuelRuleException("invalid_action");
                }
            }
            AutoPass(rules, st);
            st.Version++;
        }

        /// <summary>Turn/reaction timer expired for whoever must act: pass the window or end the turn.</summary>
        public static void Timeout(DuelRules rules, DuelState st)
        {
            if (st.Finished) return;
            int p = st.ToAct;
            st.Players[p].Timeouts++;
            Log(st, "timeout", p, st.Players[p].Timeouts);
            if (st.Players[p].Timeouts >= rules.Config.TimeoutsToForfeit) { Finish(st, 1 - p, "timeout"); st.Version++; return; }
            if (st.Priority >= 0) Pass(rules, st, p); else EndTurn(rules, st);
            AutoPass(rules, st);
            st.Version++;
        }

        public static DuelUnit? Unit(DuelState st, int uid)
        {
            foreach (var pl in st.Players) foreach (var u in pl.Units) if (u.Uid == uid) return u;
            return null;
        }

        // ------------------------------------------------------------------ main phase actions

        private static void RequireMain(DuelState st, int p)
        {
            if (st.Active != p || st.Priority >= 0) throw new DuelRuleException("not_your_turn");
            if (st.Phase != "main") throw new DuelRuleException("not_main_phase");
        }

        public static int TributesFor(DuelRules rules, PetFormStats f)
        {
            var t = rules.Config.Tributes;
            if (f.Divine) return t.Divine;
            return f.Stage >= 3 ? t.Stage3 : f.Stage == 2 ? t.Stage2 : t.Stage1;
        }

        private static void Summon(DuelRules rules, DuelState st, int p, DuelAction a, bool special)
        {
            RequireMain(st, p);
            var pl = st.Players[p];
            if (!pl.Hand.Contains(a.Card)) throw new DuelRuleException("card_not_in_hand");
            var card = st.Card(a.Card);
            if (card.Kind != "pet") throw new DuelRuleException("not_a_pet");
            if (pl.NormalSummonUsed) throw new DuelRuleException("summon_used");
            var form = card.Forms[0];
            int need = TributesFor(rules, form);
            var tributes = a.Tributes.Distinct().ToList();
            if (tributes.Count != need) throw new DuelRuleException("tributes_required");
            foreach (var uid in tributes) if (!pl.Units.Any(u => u.Uid == uid)) throw new DuelRuleException("invalid_tribute");
            int zonesFree = rules.Config.PetZones - pl.Units.Count + tributes.Count;
            if (zonesFree <= 0) throw new DuelRuleException("field_full");
            if (a.Zone < 0 || a.Zone >= rules.Config.PetZones) throw new DuelRuleException("invalid_zone");
            if (pl.Units.Any(u => u.Zone == a.Zone && !tributes.Contains(u.Uid))) throw new DuelRuleException("zone_occupied");
            // tributes are sent to the graveyard: not "destroyed", no LP penalty
            foreach (var uid in tributes)
            {
                var u = pl.Units.First(x => x.Uid == uid);
                pl.Units.Remove(u); pl.Grave.Add(u.CardId);
                Log(st, "tribute", p, u.Uid, u.CardId);
            }
            pl.Hand.Remove(a.Card);
            pl.NormalSummonUsed = true;
            var unit = Place(rules, st, p, card, 0, a.Zone, 100, special ? "special" : "normal");
            st.Event = "summon"; st.EventPlayer = p; st.EventUnit = unit.Uid;
            OpenWindow(rules, st, 1 - p);
        }

        private static DuelUnit Place(DuelRules rules, DuelState st, int p, DuelCard card, int formIndex, int zone, int hpPct, string how)
        {
            var f = card.Forms[formIndex];
            var u = new DuelUnit { Uid = st.NextUid++, CardId = card.Id, Owner = p, Zone = zone, SummonedTurn = st.Turn };
            SetForm(rules, u, f, formIndex);
            u.Hp = Math.Max(1, (int)((long)u.MaxHp * hpPct / 100));
            st.Players[p].Units.Add(u);
            Log(st, "summon", p, u.Uid, card.Id, zone, how + "|" + f.FormId);
            var pas = Passive(rules, u);
            if (pas != null && pas.Type == "battle_start_energy") st.Players[p].Energy += pas.Amount;
            return u;
        }

        private static void SetForm(DuelRules rules, DuelUnit u, PetFormStats f, int formIndex)
        {
            u.FormIndex = formIndex; u.FormId = f.FormId; u.Stage = f.Stage; u.Divine = f.Divine; u.Element = f.Element; u.Role = f.Role;
            u.MaxHp = f.Hp; u.Atk = f.Atk; u.Def = f.Def; u.Spd = f.Spd; u.CritBp = f.CritBp;
            u.Skill = f.Skill; u.Ultimate = f.Ultimate; u.Passive = f.Passive;
            var pas = Passive(rules, u);
            if (pas != null && pas.Type == "stat_bonus")
            {
                if (pas.Stat == "atk") u.Atk = u.Atk * (100 + pas.Pct) / 100;
                if (pas.Stat == "def") u.Def = u.Def * (100 + pas.Pct) / 100;
                if (pas.Stat == "spd") u.Spd = u.Spd * (100 + pas.Pct) / 100;
                if (pas.Stat == "crit") u.CritBp = Math.Min(5000, u.CritBp + pas.Pct * 100);
            }
        }

        private static void Set(DuelRules rules, DuelState st, int p, DuelAction a)
        {
            RequireMain(st, p);
            var pl = st.Players[p];
            if (!pl.Hand.Contains(a.Card)) throw new DuelRuleException("card_not_in_hand");
            var card = st.Card(a.Card);
            if (card.Kind != "tactic" || rules.Tactics[card.TacticId!].Type != "trap") throw new DuelRuleException("only_traps_can_be_set");
            if (a.Zone < 0 || a.Zone >= pl.Zones.Count) throw new DuelRuleException("invalid_zone");
            if (pl.Zones[a.Zone] != null) throw new DuelRuleException("zone_occupied");
            pl.Hand.Remove(a.Card);
            pl.Zones[a.Zone] = new SetCard { CardId = a.Card, TacticId = card.TacticId!, SetTurn = st.Turn };
            Log(st, "set", p, a.Zone);
        }

        private static void Evolve(DuelRules rules, DuelState st, int p, DuelAction a)
        {
            RequireMain(st, p);
            var pl = st.Players[p];
            var u = pl.Units.FirstOrDefault(x => x.Uid == a.Unit) ?? throw new DuelRuleException("invalid_unit");
            if (u.SummonedTurn == st.Turn) throw new DuelRuleException("summoned_this_turn");
            if (u.EvolvedTurn == st.Turn) throw new DuelRuleException("already_evolved");
            var card = st.Card(u.CardId);
            if (u.FormIndex + 1 >= card.Forms.Count) throw new DuelRuleException("final_form");
            int cost = rules.Config.EvolveCost[Math.Min(rules.Config.EvolveCost.Count - 1, Math.Max(1, u.Stage))];
            if (pl.Energy < cost) throw new DuelRuleException("not_enough_energy");
            pl.Energy -= cost;
            int oldMax = u.MaxHp, oldHp = u.Hp;
            string from = u.FormId;
            SetForm(rules, u, card.Forms[u.FormIndex + 1], u.FormIndex + 1);
            // same Pet stays on the field: HP keeps its ratio (never refilled), statuses/buffs/shield stay
            u.Hp = Math.Max(1, (int)((long)oldHp * u.MaxHp / oldMax));
            u.EvolvedTurn = st.Turn;
            Log(st, "evolve", p, u.Uid, u.FormIndex, u.Hp, from + ">" + u.FormId);
        }

        private static void Move(DuelRules rules, DuelState st, int p, DuelAction a)
        {
            RequireMain(st, p);
            var pl = st.Players[p];
            var u = pl.Units.FirstOrDefault(x => x.Uid == a.Unit) ?? throw new DuelRuleException("invalid_unit");
            if (u.Moved) throw new DuelRuleException("already_moved");
            if (a.Zone < 0 || a.Zone >= rules.Config.PetZones || a.Zone == u.Zone) throw new DuelRuleException("invalid_zone");
            var other = pl.Units.FirstOrDefault(x => x.Zone == a.Zone);
            if (other != null) { other.Zone = u.Zone; other.Moved = true; }
            u.Zone = a.Zone; u.Moved = true;
            Log(st, "move", p, u.Uid, a.Zone);
        }

        private static void EnterBattle(DuelState st, int p)
        {
            RequireMain(st, p);
            if (p == st.First && st.Players[p].TurnsTaken == 1) throw new DuelRuleException("first_turn_no_battle");
            st.Phase = "battle";
            Log(st, "battle", p);
        }

        // ------------------------------------------------------------------ battle

        public static List<DuelUnit> LegalTargets(DuelState st, int attacker)
        {
            var enemy = st.Players[1 - attacker].Units;
            var taunt = enemy.Where(u => Has(u, "taunt")).ToList();
            if (taunt.Count > 0) return taunt.OrderBy(u => u.Zone).ToList();
            var front = enemy.Where(u => u.Zone < 3).ToList();
            return (front.Count > 0 ? front : enemy).OrderBy(u => u.Zone).ToList();
        }

        public static bool CanAct(DuelState st, DuelUnit u) => u.SummonedTurn != st.Turn && !u.Acted && !Has(u, "stun") && u.Hp > 0;

        private static void Attack(DuelRules rules, DuelState st, int p, DuelAction a)
        {
            if (st.Active != p || st.Priority >= 0) throw new DuelRuleException("not_your_turn");
            if (st.Phase != "battle") throw new DuelRuleException("not_battle_phase");
            var pl = st.Players[p];
            var u = pl.Units.FirstOrDefault(x => x.Uid == a.Unit) ?? throw new DuelRuleException("invalid_unit");
            if (u.SummonedTurn == st.Turn) throw new DuelRuleException("summoned_this_turn");
            if (u.Acted) throw new DuelRuleException("already_acted");
            if (Has(u, "stun")) throw new DuelRuleException("unit_stunned");
            string kind = a.Type;
            DuelSkill? sk = null;
            if (kind != "attack")
            {
                string? id = kind == "skill" ? u.Skill : u.Ultimate;
                if (id == null || !rules.Skills.TryGetValue(id, out sk)) throw new DuelRuleException("no_such_skill");
                if (pl.Energy < sk.Cost) throw new DuelRuleException("not_enough_energy");
            }
            bool offensive = sk == null || sk.Target.StartsWith("enemy");
            int target = 0;
            if (offensive)
            {
                var legal = LegalTargets(st, p);
                if (legal.Count == 0) throw new DuelRuleException("no_target");   // no direct LP damage by default
                bool single = sk == null || sk.Target == "enemy_single";
                if (single)
                {
                    if (!legal.Any(t => t.Uid == a.Target)) throw new DuelRuleException("invalid_target");
                    target = a.Target;
                }
                else target = legal[0].Uid;
            }
            if (sk != null) pl.Energy -= sk.Cost;
            u.Acted = true;
            Log(st, kind, p, u.Uid, target, 0, sk?.Id ?? "");
            AddLink(rules, st, new ChainLink { Player = p, Kind = kind, Attacker = u.Uid, Target = target });
        }

        // ------------------------------------------------------------------ spells & traps

        private static bool LastMatches(DuelState st, int p, string what)
        {
            if (st.Chain.Count == 0) return false;
            var last = st.Chain[st.Chain.Count - 1];
            if (last.Player == p) return false;
            return what == "spell" ? last.Kind == "spell" : what == "trap" ? last.Kind == "trap" : last.Kind == "attack" || last.Kind == "skill" || last.Kind == "ultimate";
        }

        private static bool NegateUsable(DuelState st, int p, TacticDef d)
        {
            var neg = d.Effects.FirstOrDefault(e => e.Op == "negate");
            return neg == null || LastMatches(st, p, neg.What ?? "");
        }

        private static bool LimitsOk(DuelPlayerState pl, TacticDef d) =>
            !(d.OncePerTurn && pl.UsedThisTurn.Contains(d.Id)) && !(d.OncePerDuel && pl.UsedThisDuel.Contains(d.Id));

        private static void ActivateSpell(DuelRules rules, DuelState st, int p, DuelAction a)
        {
            var pl = st.Players[p];
            if (!pl.Hand.Contains(a.Card)) throw new DuelRuleException("card_not_in_hand");
            var card = st.Card(a.Card);
            if (card.Kind != "tactic") throw new DuelRuleException("not_a_spell");
            var d = rules.Tactics[card.TacticId!];
            if (d.Type != "spell") throw new DuelRuleException("not_a_spell");
            if (d.Speed == "normal")
            {
                if (st.Priority >= 0 || st.Active != p) throw new DuelRuleException("quick_only_now");
                if (st.Phase != "main") throw new DuelRuleException("not_main_phase");
            }
            if (pl.Energy < d.Cost) throw new DuelRuleException("not_enough_energy");
            if (!LimitsOk(pl, d)) throw new DuelRuleException("card_limit_reached");
            if (!NegateUsable(st, p, d)) throw new DuelRuleException("nothing_to_negate");
            if (!TargetsValid(rules, st, p, d, a.Targets)) throw new DuelRuleException("invalid_target");
            pl.Energy -= d.Cost;
            pl.Hand.Remove(a.Card); pl.Grave.Add(a.Card);
            if (d.OncePerTurn) pl.UsedThisTurn.Add(d.Id);
            if (d.OncePerDuel) pl.UsedThisDuel.Add(d.Id);
            Log(st, "spell", p, a.Card, 0, d.Cost, d.Id);
            AddLink(rules, st, new ChainLink { Player = p, Kind = "spell", CardId = a.Card, TacticId = d.Id, Targets = a.Targets.ToList() });
        }

        public static bool TrapReady(DuelRules rules, DuelState st, int p, SetCard? s)
        {
            if (s == null || s.SetTurn >= st.Turn) return false;     // never in the turn it was set
            var d = rules.Tactics[s.TacticId];
            var pl = st.Players[p];
            return pl.Energy >= d.Cost && LimitsOk(pl, d) && TriggerMatches(st, p, d);
        }

        private static bool TriggerMatches(DuelState st, int p, TacticDef d)
        {
            var last = st.Chain.Count > 0 ? st.Chain[st.Chain.Count - 1] : null;
            switch (d.Trigger)
            {
                case "attack":
                    if (last == null || last.Player == p || !(last.Kind == "attack" || last.Kind == "skill" || last.Kind == "ultimate")) return false;
                    var t = Unit(st, last.Target);
                    return t != null && t.Owner == p;
                case "summon": return st.Chain.Count == 0 && st.Event == "summon" && st.EventPlayer != p && Unit(st, st.EventUnit) != null;
                case "spell": return last != null && last.Player != p && last.Kind == "spell";
                case "trap": return last != null && last.Player != p && last.Kind == "trap";
                case "destroyed": return st.Chain.Count == 0 && st.Event == "destroyed" && st.EventPlayer == p;
                default: return false;
            }
        }

        private static void ActivateTrap(DuelRules rules, DuelState st, int p, DuelAction a)
        {
            var pl = st.Players[p];
            if (a.Zone < 0 || a.Zone >= pl.Zones.Count || pl.Zones[a.Zone] == null) throw new DuelRuleException("no_trap_there");
            var s = pl.Zones[a.Zone]!;
            if (s.SetTurn >= st.Turn) throw new DuelRuleException("trap_set_this_turn");
            var d = rules.Tactics[s.TacticId];
            if (!TriggerMatches(st, p, d)) throw new DuelRuleException("trap_condition_not_met");
            if (pl.Energy < d.Cost) throw new DuelRuleException("not_enough_energy");
            if (!LimitsOk(pl, d)) throw new DuelRuleException("card_limit_reached");
            pl.Energy -= d.Cost;
            pl.Zones[a.Zone] = null; pl.Grave.Add(s.CardId);
            if (d.OncePerTurn) pl.UsedThisTurn.Add(d.Id);
            if (d.OncePerDuel) pl.UsedThisDuel.Add(d.Id);
            var link = new ChainLink { Player = p, Kind = "trap", CardId = s.CardId, TacticId = d.Id };
            var last = st.Chain.Count > 0 ? st.Chain[st.Chain.Count - 1] : null;
            if (d.Trigger == "attack" && last != null) { link.Attacker = last.Attacker; link.Target = last.Target; }
            if (d.Trigger == "summon") link.Target = st.EventUnit;
            Log(st, "trap", p, s.CardId, a.Zone, d.Cost, d.Id);
            AddLink(rules, st, link);
        }

        public static bool TargetsValid(DuelRules rules, DuelState st, int p, TacticDef d, List<int> t)
        {
            var pl = st.Players[p];
            var en = st.Players[1 - p];
            int maxStage = d.Effects.Select(e => e.MaxStage).DefaultIfEmpty(0).Max();
            bool zoneFree = pl.Units.Count < rules.Config.PetZones;
            switch (d.Targeting)
            {
                case "none": return true;
                case "own_pet": return t.Count >= 1 && pl.Units.Any(u => u.Uid == t[0]);
                case "enemy_pet": return t.Count >= 1 && en.Units.Any(u => u.Uid == t[0]);
                case "own_two": return t.Count >= 2 && t[0] != t[1] && pl.Units.Any(u => u.Uid == t[0]) && pl.Units.Any(u => u.Uid == t[1]);
                case "hand_pet":
                    return t.Count >= 1 && zoneFree && pl.Hand.Contains(t[0]) && st.Card(t[0]).Kind == "pet" && !st.Card(t[0]).Forms[0].Divine && st.Card(t[0]).Forms[0].Stage <= maxStage;
                case "grave_pet":
                    return t.Count >= 1 && zoneFree && pl.Grave.Contains(t[0]) && st.Card(t[0]).Kind == "pet" && !st.Card(t[0]).Forms[0].Divine && st.Card(t[0]).Forms[0].Stage <= maxStage;
                case "enemy_st": return t.Count >= 1 && t[0] >= 0 && t[0] < en.Zones.Count && en.Zones[t[0]] != null;
                default: return false;
            }
        }

        /// <summary>Could this player respond right now? (used to skip empty reaction windows)</summary>
        public static bool HasResponse(DuelRules rules, DuelState st, int p)
        {
            var pl = st.Players[p];
            for (int z = 0; z < pl.Zones.Count; z++) if (TrapReady(rules, st, p, pl.Zones[z])) return true;
            foreach (var c in pl.Hand)
            {
                var card = st.Card(c);
                if (card.Kind != "tactic") continue;
                var d = rules.Tactics[card.TacticId!];
                if (d.Type != "spell" || d.Speed != "quick" || pl.Energy < d.Cost || !LimitsOk(pl, d) || !NegateUsable(st, p, d)) continue;
                if (d.Targeting == "none" || (d.Targeting == "own_pet" && pl.Units.Count > 0) || (d.Targeting == "enemy_pet" && st.Players[1 - p].Units.Count > 0)) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ reaction windows & chain

        private static void OpenWindow(DuelRules rules, DuelState st, int responder)
        {
            st.Priority = responder; st.LastPassed = false;
        }

        private static void AddLink(DuelRules rules, DuelState st, ChainLink link)
        {
            if (st.Chain.Count >= rules.Config.ChainMaxDepth) throw new DuelRuleException("chain_too_deep");
            st.Chain.Add(link);
            st.Priority = 1 - link.Player; st.LastPassed = false;
        }

        private static void AutoPass(DuelRules rules, DuelState st)
        {
            int guard = 0;
            while (!st.Finished && st.Priority >= 0 && guard++ < 64 && !HasResponse(rules, st, st.Priority)) Pass(rules, st, st.Priority);
        }

        private static void Pass(DuelRules rules, DuelState st, int p)
        {
            if (st.Chain.Count == 0) { CloseWindow(st); return; }
            if (st.LastPassed) { ResolveChain(rules, st); return; }
            st.LastPassed = true;
            st.Priority = 1 - p;
        }

        private static void CloseWindow(DuelState st) { st.Priority = -1; st.Event = null; st.LastPassed = false; }


        private static void ResolveChain(DuelRules rules, DuelState st)
        {
            var destroyedBy = new HashSet<int>();
            for (int i = st.Chain.Count - 1; i >= 0 && !st.Finished; i--)
            {
                var link = st.Chain[i];
                if (link.Negated) { Log(st, "negated", link.Player, link.CardId, link.Attacker, i, link.TacticId ?? link.Kind); continue; }
                ResolveLink(rules, st, i);
                foreach (var owner in ProcessDeaths(rules, st)) destroyedBy.Add(owner);
            }
            st.Chain.Clear();
            CloseWindow(st);
            if (st.Finished) return;
            // "destroyed" trigger window for the owner whose Pet was destroyed (opponent of the active player first)
            foreach (var owner in new[] { 1 - st.Active, st.Active })
                if (destroyedBy.Contains(owner) && st.Players[owner].Zones.Any(z => z != null && rules.Tactics[z.TacticId].Trigger == "destroyed"))
                {
                    st.Event = "destroyed"; st.EventPlayer = owner;
                    OpenWindow(rules, st, owner);
                    break;
                }
        }

        private static void ResolveLink(DuelRules rules, DuelState st, int i)
        {
            var link = st.Chain[i];
            switch (link.Kind)
            {
                case "attack":
                {
                    var att = Unit(st, link.Attacker); var tgt = Unit(st, link.Target);
                    if (att == null || tgt == null) { Log(st, "fizzle", link.Player, link.Attacker, link.Target); return; }
                    int dmg = BasicDamage(rules, st, att, tgt, 100);
                    int dealt = ApplyDamage(rules, st, tgt, dmg, "attack");
                    var pas = Passive(rules, att);
                    if (pas != null && pas.Type == "lifesteal" && dealt > 0) Heal(st, att, dealt * pas.Pct / 100);
                    return;
                }
                case "skill":
                case "ultimate":
                    ResolveSkill(rules, st, link); return;
                default:
                {
                    var d = rules.Tactics[link.TacticId!];
                    if (link.Kind == "spell" && !TargetsValid(rules, st, link.Player, d, link.Targets) && d.Targeting != "none")
                    { Log(st, "fizzle", link.Player, link.CardId, 0, 0, d.Id); return; }
                    foreach (var e in d.Effects) ResolveTactic(rules, st, i, link, d, e);
                    return;
                }
            }
        }

        private static void ResolveSkill(DuelRules rules, DuelState st, ChainLink link)
        {
            var att = Unit(st, link.Attacker);
            if (att == null) { Log(st, "fizzle", link.Player, link.Attacker); return; }
            var sk = rules.Skills[link.Kind == "skill" ? att.Skill! : att.Ultimate!];
            var me = st.Players[att.Owner]; var en = st.Players[1 - att.Owner];
            List<DuelUnit> enemies, allies;
            switch (sk.Target)
            {
                case "enemy_single": { var t = Unit(st, link.Target); enemies = t == null ? new List<DuelUnit>() : new List<DuelUnit> { t }; break; }
                case "enemy_all": enemies = en.Units.OrderBy(u => u.Zone).ToList(); break;
                case "enemy_front_row": { var f = en.Units.Where(u => u.Zone < 3).OrderBy(u => u.Zone).ToList(); enemies = f.Count > 0 ? f : en.Units.OrderBy(u => u.Zone).ToList(); break; }
                default: enemies = new List<DuelUnit>(); break;
            }
            switch (sk.Target)
            {
                case "ally_all": allies = me.Units.OrderBy(u => u.Zone).ToList(); break;
                case "ally_lowest_hp": allies = me.Units.OrderBy(u => (long)u.Hp * 1000 / u.MaxHp).ThenBy(u => u.Zone).Take(1).ToList(); break;
                case "self": allies = new List<DuelUnit> { att }; break;
                default: allies = new List<DuelUnit>(); break;
            }
            bool offensive = sk.Target.StartsWith("enemy");
            if (offensive && enemies.Count == 0) { Log(st, "fizzle", link.Player, link.Attacker); return; }
            var cfg = rules.Config;
            foreach (var e in sk.Effects)
            {
                switch (e.Type)
                {
                    case "damage":
                        foreach (var t in enemies.ToList()) if (t.Hp > 0) ApplyDamage(rules, st, t, BasicDamage(rules, st, att, t, e.PowerPct), link.Kind);
                        break;
                    case "heal":
                    {
                        var targets = offensive ? new List<DuelUnit> { att } : allies;
                        long amt = (long)e.PowerPct * EffAtk(rules, att) * cfg.DamageScale / 20000;
                        var pas = Passive(rules, att);
                        if (pas != null && pas.Type == "heal_bonus") amt = amt * (100 + pas.Pct) / 100;
                        foreach (var t in targets) Heal(st, t, (int)amt);
                        break;
                    }
                    case "shield":
                    {
                        var targets = offensive ? new List<DuelUnit> { att } : allies;
                        foreach (var t in targets)
                        {
                            long basis = e.Scale == "maxhp" ? att.MaxHp : (long)(e.Scale == "def" ? att.Def : EffAtk(rules, att)) * cfg.DamageScale / 100;
                            int amt = (int)(e.PowerPct * basis / 100);
                            t.Shield += amt;
                            Log(st, "shield", t.Owner, t.Uid, 0, amt);
                        }
                        break;
                    }
                    case "status":
                        foreach (var t in (offensive && e.Status != "taunt" ? enemies : (allies.Count > 0 ? allies : new List<DuelUnit> { att })).ToList())
                            if (t.Hp > 0) AddStatus(st, t, e.Status!, e.Duration <= 0 ? 1 : e.Duration, e.Chance);
                        break;
                    case "cleanse":
                        foreach (var t in allies) Cleanse(st, t);
                        break;
                    case "energy":
                        me.Energy += Math.Max(1, e.PowerPct / 100);
                        Log(st, "energy", att.Owner, 0, 0, Math.Max(1, e.PowerPct / 100));
                        break;
                }
            }
        }

        private static List<DuelUnit> TacticTargets(DuelState st, ChainLink link, string? target, int owner)
        {
            var me = st.Players[owner]; var en = st.Players[1 - owner];
            var list = new List<DuelUnit>();
            void AddU(int uid) { var u = Unit(st, uid); if (u != null) list.Add(u); }
            switch (target)
            {
                case "t0": if (link.Targets.Count > 0) AddU(link.Targets[0]); break;
                case "own_all": list.AddRange(me.Units.OrderBy(u => u.Zone)); break;
                case "enemy_all": list.AddRange(en.Units.OrderBy(u => u.Zone)); break;
                case "enemy_front": { var f = en.Units.Where(u => u.Zone < 3).OrderBy(u => u.Zone).ToList(); list.AddRange(f.Count > 0 ? f : en.Units.OrderBy(u => u.Zone)); break; }
                case "attacker": AddU(link.Attacker); break;
                case "attack_target": case "summoned": AddU(link.Target); break;
            }
            return list;
        }

        private static void ResolveTactic(DuelRules rules, DuelState st, int i, ChainLink link, TacticDef d, TacticEffect e)
        {
            int p = link.Player;
            var pl = st.Players[p];
            var prev = i > 0 ? st.Chain[i - 1] : null;
            switch (e.Op)
            {
                case "heal":
                    foreach (var u in TacticTargets(st, link, e.Target == "own_element" ? "own_all" : e.Target, p))
                        if (e.Target != "own_element" || u.Element == e.Element) Heal(st, u, (int)((long)u.MaxHp * e.Pct / 100));
                    break;
                case "buff":
                    foreach (var u in TacticTargets(st, link, e.Target, p))
                    {
                        u.Buffs.Add(new DuelBuff { Stat = e.Stat ?? "atk", Pct = e.Pct, UntilTurn = st.Turn + Math.Max(1, e.Turns) - 1 });
                        Log(st, "buff", u.Owner, u.Uid, 0, e.Pct, e.Stat ?? "atk");
                    }
                    break;
                case "shield":
                    foreach (var u in TacticTargets(st, link, e.Target, p))
                    {
                        int amt = (int)((long)u.MaxHp * e.Pct / 100);
                        u.Shield += amt;
                        Log(st, "shield", u.Owner, u.Uid, 0, amt);
                    }
                    break;
                case "cleanse":
                    foreach (var u in TacticTargets(st, link, e.Target, p)) Cleanse(st, u);
                    break;
                case "draw": Draw(st, p, e.N, false); break;
                case "energy": pl.Energy += e.N; Log(st, "energy", p, 0, 0, e.N); break;
                case "swap":
                {
                    var a = Unit(st, link.Targets[0]); var b = Unit(st, link.Targets[1]);
                    if (a != null && b != null && a.Owner == p && b.Owner == p) { (a.Zone, b.Zone) = (b.Zone, a.Zone); Log(st, "swap", p, a.Uid, b.Uid); }
                    break;
                }
                case "search_pet":
                {
                    int idx = pl.Deck.FindIndex(c => st.Card(c).Kind == "pet");
                    if (idx >= 0) { int c = pl.Deck[idx]; pl.Deck.RemoveAt(idx); pl.Hand.Add(c); Log(st, "search", p, 0, 0, 1); }
                    break;
                }
                case "damage":
                    foreach (var u in TacticTargets(st, link, e.Target, p))
                    {
                        if (u.Hp <= 0) continue;
                        string? el = e.Element == "target" ? Unit(st, link.Target)?.Element : e.Element;
                        int mult = el == null ? 100 : ElementPct(rules, el, u.Element);
                        ApplyDamage(rules, st, u, Math.Max(1, (int)((long)u.MaxHp * e.Pct / 100 * mult / 100)), d.Id);
                    }
                    break;
                case "status":
                    foreach (var u in TacticTargets(st, link, e.Target, p)) if (u.Hp > 0) AddStatus(st, u, e.Status!, Math.Max(1, e.Turns), e.Chance);
                    break;
                case "special_summon":
                {
                    int c = link.Targets.Count > 0 ? link.Targets[0] : 0;
                    int zone = FreeZone(rules, pl);
                    if (pl.Hand.Contains(c) && zone >= 0) { pl.Hand.Remove(c); Place(rules, st, p, st.Card(c), 0, zone, 100, "special"); }
                    break;
                }
                case "revive":
                {
                    int c = link.Targets.Count > 0 ? link.Targets[0] : 0;
                    int zone = FreeZone(rules, pl);
                    if (pl.Grave.Contains(c) && zone >= 0) { pl.Grave.Remove(c); Place(rules, st, p, st.Card(c), 0, zone, e.Pct, "revive"); }
                    break;
                }
                case "negate":
                    if (prev != null && !prev.Negated && prev.Player != p &&
                        (e.What == "spell" ? prev.Kind == "spell" : e.What == "trap" ? prev.Kind == "trap" : prev.Kind == "attack" || prev.Kind == "skill" || prev.Kind == "ultimate"))
                    { prev.Negated = true; Log(st, "negate", p, i - 1, 0, 0, e.What ?? ""); }
                    break;
                case "reflect":
                {
                    var att = prev == null ? null : Unit(st, prev.Attacker); var tgt = prev == null ? null : Unit(st, prev.Target);
                    if (att != null && tgt != null) ApplyDamage(rules, st, att, Math.Max(1, BasicDamage(rules, st, att, tgt, 100) / 2), "reflect");
                    break;
                }
                case "destroy_st":
                {
                    var en = st.Players[1 - p];
                    int z = link.Targets.Count > 0 ? link.Targets[0] : -1;
                    if (z >= 0 && z < en.Zones.Count && en.Zones[z] != null) { en.Grave.Add(en.Zones[z]!.CardId); en.Zones[z] = null; Log(st, "destroy_st", p, z); }
                    break;
                }
                case "redirect":
                    if (prev != null)
                    {
                        var cur = Unit(st, prev.Target);
                        var cand = pl.Units.Where(u => cur == null || u.Uid != cur.Uid).OrderByDescending(u => u.Hp).ThenBy(u => u.Uid).FirstOrDefault();
                        if (cand != null) { prev.Target = cand.Uid; Log(st, "redirect", p, cand.Uid); }
                    }
                    break;
            }
        }

        private static int FreeZone(DuelRules rules, DuelPlayerState pl)
        {
            for (int z = 0; z < rules.Config.PetZones; z++) if (!pl.Units.Any(u => u.Zone == z)) return z;
            return -1;
        }

        // ------------------------------------------------------------------ damage, statuses, deaths

        public static int ElementPct(DuelRules rules, string attacker, string defender)
        {
            bool aStrong = rules.Strong.TryGetValue(attacker, out var s) && s.Contains(defender);
            bool dStrong = rules.Strong.TryGetValue(defender, out var s2) && s2.Contains(attacker);
            return aStrong ? rules.Config.StrongPct : dStrong ? rules.Config.WeakPct : 100;
        }

        public static int EffAtk(DuelRules rules, DuelUnit u)
        {
            long atk = (long)u.Atk * (100 + u.Buffs.Where(b => b.Stat == "atk").Sum(b => b.Pct)) / 100;
            if (Has(u, "weaken")) atk = atk * rules.Config.WeakenPct / 100;
            var pas = Passive(rules, u);
            if (pas != null && pas.Type == "low_hp_stat_bonus" && pas.Stat == "atk" && (long)u.Hp * 100 <= (long)u.MaxHp * 50) atk = atk * (100 + pas.Pct) / 100;
            return (int)Math.Max(1, atk);
        }

        /// <summary>Damage = power × ATK × ATK / (ATK + DEF) × scale × element (× crit). Scale-free: upgrades raise ATK and HP
        /// together, so the number of hits to destroy a Pet stays similar at every progression level.</summary>
        public static int BasicDamage(DuelRules rules, DuelState st, DuelUnit att, DuelUnit tgt, int powerPct)
        {
            var cfg = rules.Config;
            long atk = EffAtk(rules, att);
            long dmg = powerPct * atk * atk * cfg.DamageScale / ((atk + Math.Max(0, tgt.Def)) * 10000);
            dmg = dmg * ElementPct(rules, att.Element, tgt.Element) / 100;
            if (att.CritBp > 0 && R(st, 10000) < att.CritBp) dmg = dmg * cfg.CritPct / 100;
            return (int)Math.Max(1, dmg);
        }

        /// <summary>Applies damage to a Pet's own HP (never to LP). Returns HP actually lost.</summary>
        public static int ApplyDamage(DuelRules rules, DuelState st, DuelUnit u, int amount, string source)
        {
            if (u.Hp <= 0) return 0;
            long dmg = amount;
            if (Has(u, "guard")) dmg = dmg * rules.Config.GuardPct / 100;
            var pas = Passive(rules, u);
            if (pas != null && pas.Type == "damage_reduction") dmg = dmg * (100 - pas.Pct) / 100;
            dmg = Math.Max(1, dmg);
            int absorbed = (int)Math.Min(u.Shield, dmg);
            u.Shield -= absorbed;
            int rest = (int)dmg - absorbed;
            int before = u.Hp;
            u.Hp -= rest;
            if (Has(u, "endure") && u.Hp < 1) u.Hp = 1;
            Log(st, "damage", u.Owner, u.Uid, absorbed, (int)dmg, source);
            return before - Math.Max(0, u.Hp);
        }

        private static void Heal(DuelState st, DuelUnit u, int amount)
        {
            if (u.Hp <= 0 || amount <= 0) return;
            int before = u.Hp;
            u.Hp = Math.Min(u.MaxHp, u.Hp + amount);
            if (u.Hp > before) Log(st, "heal", u.Owner, u.Uid, 0, u.Hp - before);
        }

        private static void Cleanse(DuelState st, DuelUnit u)
        {
            int n = u.Statuses.RemoveAll(s => s.Type == "stun" || s.Type == "weaken" || s.Type == "burn" || s.Type == "poison");
            if (n > 0) Log(st, "cleanse", u.Owner, u.Uid, 0, n);
        }

        public static bool Has(DuelUnit u, string status) => u.Statuses.Any(s => s.Type == status && s.Remaining > 0);

        private static void AddStatus(DuelState st, DuelUnit u, string status, int turns, int chance)
        {
            if (chance < 100 && R(st, 100) >= chance) return;
            // a control status applied during its owner's own turn must still cover the owner's NEXT turn
            if (u.Owner == st.Active && (status == "stun" || status == "weaken" || status == "taunt")) turns++;
            var cur = u.Statuses.FirstOrDefault(s => s.Type == status);
            if (cur != null) cur.Remaining = Math.Max(cur.Remaining, turns);
            else u.Statuses.Add(new DuelStatus { Type = status, Remaining = turns });
            Log(st, "status", u.Owner, u.Uid, 0, turns, status);
        }

        private static DuelPassive? Passive(DuelRules rules, DuelUnit u) =>
            u.Passive != null && rules.Passives.TryGetValue(u.Passive, out var p) ? p : null;

        /// <summary>Destroys every Pet at 0 HP exactly once: graveyard + owner loses DeathPenalty LP. Returns owners hit.</summary>
        private static List<int> ProcessDeaths(DuelRules rules, DuelState st)
        {
            var owners = new List<int>();
            for (int p = 0; p < 2; p++)
            {
                var pl = st.Players[p];
                foreach (var u in pl.Units.Where(x => x.Hp <= 0).OrderBy(x => x.Zone).ToList())
                {
                    pl.Units.Remove(u);
                    pl.Grave.Add(u.CardId);
                    pl.Lp -= rules.Config.DeathPenalty;
                    owners.Add(p);
                    Log(st, "destroyed", p, u.Uid, u.CardId, rules.Config.DeathPenalty);
                }
            }
            bool dead0 = st.Players[0].Lp <= 0, dead1 = st.Players[1].Lp <= 0;
            if (dead0 && dead1) Finish(st, -1, "both_lp_zero");
            else if (dead0) Finish(st, 1, "lp_zero");
            else if (dead1) Finish(st, 0, "lp_zero");
            return owners;
        }
    }
}
