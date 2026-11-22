using System.Collections.Generic;
using System.Linq;

namespace PetStar.Rules.Duel
{
    /// <summary>
    /// Rule-abiding duel AI (PvE opponents, ranked ghost decks, timeouts in tests). It only sees what its player may see
    /// and only proposes legal actions; the engine still validates everything. Deterministic: ties break by id/zone,
    /// so the TypeScript port makes the same decisions.
    /// </summary>
    public static class DuelAi
    {
        public static DuelAction Decide(DuelRules rules, DuelState st, int p)
        {
            return st.Priority == p ? React(rules, st, p) : st.Phase == "battle" ? Battle(rules, st, p) : Main(rules, st, p);
        }

        private static DuelAction Act(string type) => new DuelAction { Type = type };

        private static int Power(DuelUnit u) => u.Hp / 4 + u.Atk * 2 + u.Def * 3 / 2;
        private static int Power(PetFormStats f) => f.Hp / 4 + f.Atk * 2 + f.Def * 3 / 2;

        // ------------------------------------------------------------------ reactions

        private static DuelAction React(DuelRules rules, DuelState st, int p)
        {
            var pl = st.Players[p];
            for (int z = 0; z < pl.Zones.Count; z++)
                if (DuelEngine.TrapReady(rules, st, p, pl.Zones[z])) return new DuelAction { Type = "activate", Zone = z };
            var last = st.Chain.Count > 0 ? st.Chain[st.Chain.Count - 1] : null;
            foreach (var c in pl.Hand.OrderBy(c => c))
            {
                var card = st.Card(c);
                if (card.Kind != "tactic") continue;
                var d = rules.Tactics[card.TacticId!];
                if (d.Type != "spell" || d.Speed != "quick" || pl.Energy < d.Cost) continue;
                var neg = d.Effects.FirstOrDefault(e => e.Op == "negate");
                if (neg != null)
                {
                    if (last == null || last.Player == p) continue;
                    bool attack = last.Kind == "attack" || last.Kind == "skill" || last.Kind == "ultimate";
                    if (neg.What == "attack" && attack)
                    {
                        var t = DuelEngine.Unit(st, last.Target);
                        if (t != null && t.Owner == p && (long)t.Hp * 100 < (long)t.MaxHp * 60) return Spell(c);
                    }
                    if (neg.What == "spell" && last.Kind == "spell" && rules.Tactics[last.TacticId!].Cost >= 2) return Spell(c);
                    continue;
                }
                if (last != null && last.Player != p && (last.Kind == "attack" || last.Kind == "skill" || last.Kind == "ultimate") && d.Targeting == "own_pet")
                {
                    var t = DuelEngine.Unit(st, last.Target);
                    if (t != null && t.Owner == p && (long)t.Hp * 100 < (long)t.MaxHp * 50) return Spell(c, t.Uid);
                }
            }
            return Act("pass");
        }

        private static DuelAction Spell(int card, params int[] targets) => new DuelAction { Type = "spell", Card = card, Targets = targets.ToList() };

        // ------------------------------------------------------------------ main phase

        private static DuelAction Main(DuelRules rules, DuelState st, int p)
        {
            var pl = st.Players[p];
            var en = st.Players[1 - p];
            var cfg = rules.Config;

            // 1) evolve the strongest eligible Pet
            foreach (var u in pl.Units.OrderByDescending(Power).ThenBy(u => u.Uid))
            {
                var card = st.Card(u.CardId);
                if (u.SummonedTurn == st.Turn || u.EvolvedTurn == st.Turn || u.FormIndex + 1 >= card.Forms.Count) continue;
                int cost = cfg.EvolveCost[System.Math.Min(cfg.EvolveCost.Count - 1, System.Math.Max(1, u.Stage))];
                if (pl.Energy >= cost) return new DuelAction { Type = "evolve", Unit = u.Uid };
            }

            // 2) normal summon: best Pet whose tributes are worth it
            if (!pl.NormalSummonUsed)
            {
                foreach (var c in pl.Hand.Where(c => st.Card(c).Kind == "pet").OrderByDescending(c => Power(st.Card(c).Forms[0])).ThenBy(c => c))
                {
                    var f = st.Card(c).Forms[0];
                    int need = DuelEngine.TributesFor(rules, f);
                    var tributes = pl.Units.OrderBy(Power).ThenBy(u => u.Uid).Take(need).ToList();
                    if (tributes.Count < need) continue;
                    if (need > 0 && Power(f) * 10 < tributes.Sum(Power) * 12) continue;
                    int zone = PickZone(rules, pl, f.Role, tributes.Select(t => t.Uid).ToList());
                    if (zone < 0) continue;
                    return new DuelAction { Type = "summon", Card = c, Zone = zone, Tributes = tributes.Select(t => t.Uid).ToList() };
                }
            }

            // 3) spells
            foreach (var c in pl.Hand.OrderBy(c => c))
            {
                var card = st.Card(c);
                if (card.Kind != "tactic") continue;
                var d = rules.Tactics[card.TacticId!];
                if (d.Type != "spell" || pl.Energy < d.Cost) continue;
                var a = SpellChoice(rules, st, p, c, d);
                if (a != null && DuelEngine.TargetsValid(rules, st, p, d, a.Targets) && LimitsOk(pl, d)) return a;
            }

            // 4) set traps
            for (int z = 0; z < pl.Zones.Count; z++)
            {
                if (pl.Zones[z] != null) continue;
                int trap = pl.Hand.Where(c => st.Card(c).Kind == "tactic" && rules.Tactics[st.Card(c).TacticId!].Type == "trap").DefaultIfEmpty(0).Min();
                if (trap > 0) return new DuelAction { Type = "set", Card = trap, Zone = z };
                break;
            }

            // 5) battle if possible and useful
            bool firstTurn = p == st.First && pl.TurnsTaken == 1;
            if (!firstTurn && en.Units.Count > 0 && pl.Units.Any(u => DuelEngine.CanAct(st, u))) return Act("battle");
            return Act("end");
        }

        private static bool LimitsOk(DuelPlayerState pl, TacticDef d) =>
            !(d.OncePerTurn && pl.UsedThisTurn.Contains(d.Id)) && !(d.OncePerDuel && pl.UsedThisDuel.Contains(d.Id));

        private static int PickZone(DuelRules rules, DuelPlayerState pl, string role, List<int> freed)
        {
            bool back = role == "mage" || role == "support";
            var order = back ? new[] { 3, 4, 0, 1, 2 } : new[] { 0, 1, 2, 3, 4 };
            foreach (var z in order)
                if (z < rules.Config.PetZones && !pl.Units.Any(u => u.Zone == z && !freed.Contains(u.Uid))) return z;
            return -1;
        }

        private static DuelAction? SpellChoice(DuelRules rules, DuelState st, int p, int c, TacticDef d)
        {
            var pl = st.Players[p]; var en = st.Players[1 - p];
            var hurt = pl.Units.Where(u => (long)u.Hp * 100 < (long)u.MaxHp * 50).OrderBy(u => (long)u.Hp * 1000 / u.MaxHp).ThenBy(u => u.Uid).FirstOrDefault();
            bool canBattle = !(p == st.First && pl.TurnsTaken == 1) && pl.Units.Any(u => DuelEngine.CanAct(st, u)) && en.Units.Count > 0;
            switch (d.Id)
            {
                case "sp_herbal_tonic": return hurt != null ? Spell(c, hurt.Uid) : null;
                case "sp_group_remedy": return pl.Units.Count(u => (long)u.Hp * 100 < (long)u.MaxHp * 70) >= 2 ? Spell(c) : null;
                case "sp_nature_blessing": return pl.Units.Count(u => (long)u.Hp * 100 < (long)u.MaxHp * 70) >= 2 ? Spell(c) : null;
                case "sp_power_surge":
                {
                    var best = pl.Units.Where(u => DuelEngine.CanAct(st, u)).OrderByDescending(u => u.Atk).ThenBy(u => u.Uid).FirstOrDefault();
                    return canBattle && best != null ? Spell(c, best.Uid) : null;
                }
                case "sp_battle_cry": return canBattle && pl.Units.Count(u => DuelEngine.CanAct(st, u)) >= 2 ? Spell(c) : null;
                case "sp_tactical_draw": return pl.Hand.Count <= 4 ? Spell(c) : null;
                case "sp_energy_well": return pl.Hand.Count(h => st.Card(h).Kind == "tactic") >= 2 ? Spell(c) : null;
                case "sp_scout": return !pl.Hand.Any(h => st.Card(h).Kind == "pet") ? Spell(c) : null;
                case "sp_fire_storm": return en.Units.Count >= 2 ? Spell(c) : null;
                case "sp_tidal_wave": return en.Units.Count(u => u.Zone < 3) >= 2 ? Spell(c) : null;
                case "sp_thunderbolt":
                {
                    var t = en.Units.OrderBy(u => u.Hp).ThenBy(u => u.Uid).FirstOrDefault();
                    return t != null ? Spell(c, t.Uid) : null;
                }
                case "sp_call_reinforcement":
                {
                    if (!pl.NormalSummonUsed) return null;
                    int h = pl.Hand.Where(x => x != c && st.Card(x).Kind == "pet" && st.Card(x).Forms[0].Stage <= 1 && !st.Card(x).Forms[0].Divine).DefaultIfEmpty(0).Min();
                    return h > 0 ? Spell(c, h) : null;
                }
                case "sp_second_chance":
                {
                    int g = pl.Grave.Where(x => st.Card(x).Kind == "pet" && st.Card(x).Forms[0].Stage <= 2 && !st.Card(x).Forms[0].Divine).DefaultIfEmpty(0).Max();
                    return g > 0 ? Spell(c, g) : null;
                }
                case "sp_dispel":
                {
                    for (int z = 0; z < en.Zones.Count; z++) if (en.Zones[z] != null) return Spell(c, z);
                    return null;
                }
                default: return null;   // quick spells are kept for reactions; formation shift is a human tool
            }
        }

        // ------------------------------------------------------------------ battle phase

        private static DuelAction Battle(DuelRules rules, DuelState st, int p)
        {
            var pl = st.Players[p];
            foreach (var u in pl.Units.Where(u => DuelEngine.CanAct(st, u)).OrderByDescending(u => u.Atk).ThenBy(u => u.Uid))
            {
                var legal = DuelEngine.LegalTargets(st, p);
                if (legal.Count == 0) break;
                var target = legal.OrderBy(t => t.Hp).ThenBy(t => t.Uid).First();
                foreach (var kind in new[] { "ultimate", "skill" })
                {
                    string? id = kind == "skill" ? u.Skill : u.Ultimate;
                    if (id == null || !rules.Skills.TryGetValue(id, out var sk) || pl.Energy < sk.Cost) continue;
                    bool offensive = sk.Target.StartsWith("enemy");
                    bool allyNeedsHelp = pl.Units.Any(x => (long)x.Hp * 100 < (long)x.MaxHp * 60);
                    if (!offensive && !allyNeedsHelp) continue;
                    return new DuelAction { Type = kind, Unit = u.Uid, Target = target.Uid };
                }
                return new DuelAction { Type = "attack", Unit = u.Uid, Target = target.Uid };
            }
            return Act("end");
        }
    }
}
