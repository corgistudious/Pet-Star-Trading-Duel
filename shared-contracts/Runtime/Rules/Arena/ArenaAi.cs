using System.Collections.Generic;
using System.Linq;

namespace PetStar.Rules.Arena
{
    /// <summary>Rule-abiding opponent for PvE and timeouts. Deterministic (ties broken by ids), never cheats:
    /// it only reads its own hand and public information.</summary>
    public static class ArenaAi
    {
        public static ArenaAction Decide(ArenaRules rules, ArenaState st, int p)
        {
            if (st.Phase == "setup") return SetupChoice(rules, st, p);
            if (st.Players[p].MustPromote) return new ArenaAction { Type = "promote", Uid = PromoteChoice(rules, st, p) };
            return Main(rules, st, p) ?? new ArenaAction { Type = "end" };
        }

        private static int PetScore(ArenaRules rules, ArenaState st, ArenaPet pet)
        {
            var d = rules.Def(pet.DefId);
            int ready = d.Attacks.Count(a => ArenaEngine.CostPaid(pet.Energy, a.Cost));
            return (d.Hp - pet.Damage) + ready * 60 + d.Attacks.Select(a => a.Damage).DefaultIfEmpty(0).Max();
        }

        public static ArenaAction SetupChoice(ArenaRules rules, ArenaState st, int p)
        {
            var pl = st.Players[p];
            var basics = pl.Hand.Where(c => rules.Def(st.Card(c).DefId).IsBasic)
                .OrderByDescending(c => { var d = rules.Def(st.Card(c).DefId); return d.Hp + d.Attacks.Select(a => a.Damage).DefaultIfEmpty(0).Max(); }).ThenBy(c => c).ToList();
            return new ArenaAction { Type = "setup", Card = basics[0], Bench = basics.Skip(1).Take(rules.Config.BenchSize).ToList() };
        }

        public static int PromoteChoice(ArenaRules rules, ArenaState st, int p) =>
            st.Players[p].Bench.OrderByDescending(b => PetScore(rules, st, b)).ThenBy(b => b.Uid).First().Uid;

        private static ArenaAction? Main(ArenaRules rules, ArenaState st, int p)
        {
            var pl = st.Players[p]; var opp = st.Players[1 - p]; var cfg = rules.Config;
            var act = pl.Active;
            ArenaCardDef D(int card) => rules.Def(st.Card(card).DefId);
            // 1. evolve (active first)
            if (!(cfg.NoEvolveFirstTurn && pl.TurnsTaken == 1))
                foreach (var pet in pl.InPlay().OrderBy(x => x == act ? 0 : 1).ThenBy(x => x.Uid))
                {
                    if (pet.PlayedTurn == st.Turn || pet.EvolvedTurn == st.Turn) continue;
                    var evo = pl.Hand.Where(c => D(c).Kind == "pet" && D(c).EvolvesFrom == pet.DefId).OrderBy(c => c).FirstOrDefault();
                    if (evo != 0) return new ArenaAction { Type = "evolve", Card = evo, Uid = pet.Uid };
                }
            // 2. bench basics
            if (pl.Bench.Count < cfg.BenchSize)
            {
                var basic = pl.Hand.Where(c => D(c).IsBasic).OrderByDescending(c => D(c).Hp).ThenBy(c => c).FirstOrDefault();
                if (basic != 0) return new ArenaAction { Type = "bench", Card = basic };
            }
            // 3. supporters / items / tools
            foreach (var c in pl.Hand.OrderBy(c => c).ToList())
            {
                var d = D(c);
                if (d.Kind == "pet") continue;
                var a = TrainerChoice(rules, st, p, c, d);
                if (a == null) continue;
                if (d.Kind == "supporter" && pl.SupportersUsed >= cfg.SupportersPerTurn) continue;
                if (d.Kind == "supporter" && !cfg.SupporterFirstTurn && pl.TurnsTaken == 1) continue;
                if (d.Kind != "tool" && !ArenaEngine.TrainerPlayable(rules, st, p, d, a.Targets)) continue;
                return a;
            }
            // 4. activated abilities
            foreach (var pet in pl.InPlay().OrderBy(x => x.Uid))
            {
                var ab = rules.Def(pet.DefId).Ability;
                if (ab == null || ab.Trigger != "activated" || pet.AbilityTurn == st.Turn || ArenaEngine.AbilityLocked(st, pet)) continue;
                if (ab.Effects.Any(e => e.Cond == "is_active") && act != pet) continue;
                if (ab.Effects.All(e => e.Op == "heal") && (act == null || act.Damage == 0)) continue;
                return new ArenaAction { Type = "ability", Uid = pet.Uid };
            }
            // 5. attach energy where it brings an attack closer
            if (!pl.EnergyAttached && pl.ZoneCurrent != null && act != null)
            {
                var target = NeedsEnergy(rules, act, pl.ZoneCurrent) ? act
                    : pl.Bench.Where(b => NeedsEnergy(rules, b, pl.ZoneCurrent)).OrderByDescending(b => rules.Def(b.DefId).Stage).ThenBy(b => b.Uid).FirstOrDefault() ?? act;
                return new ArenaAction { Type = "attach", Uid = target.Uid };
            }
            // 6. retreat a stuck Active for a Benched Pet that can attack now
            if (act != null && pl.Retreats < cfg.RetreatsPerTurn && !ArenaEngine.AttackBanned(rules, st, p))
            {
                bool stuck = BestAttack(rules, st, p) < 0;
                int cost = ArenaEngine.RetreatCost(rules, st, p);
                var ready = pl.Bench.Where(b => rules.Def(b.DefId).Attacks.Any(at => ArenaEngine.CostPaid(b.Energy, at.Cost))).OrderByDescending(b => PetScore(rules, st, b)).ThenBy(b => b.Uid).FirstOrDefault();
                if (stuck && ready != null && cost < 50 && act.Energy.Count >= cost && act.Special != "asleep" && act.Special != "paralyzed")
                    return new ArenaAction { Type = "retreat", Uid = ready.Uid };
            }
            // 7. attack
            int best = BestAttack(rules, st, p);
            if (best >= 0)
            {
                var targets = new List<int>();
                var atk = rules.Def(act!.DefId).Attacks[best];
                if (atk.Effects.Any(e => e.Target == "opp_bench_chosen") && opp.Bench.Count > 0)
                    targets.Add(opp.Bench.OrderBy(b => rules.Def(b.DefId).Hp - b.Damage).ThenBy(b => b.Uid).First().Uid);
                if (atk.Effects.Any(e => e.Target == "own_bench_chosen") && pl.Bench.Count > 0)
                    targets.Add(pl.Bench.OrderByDescending(b => rules.Def(b.DefId).Stage).ThenBy(b => b.Uid).First().Uid);
                return new ArenaAction { Type = "attack", Index = best, Targets = targets };
            }
            return null;
        }

        private static bool NeedsEnergy(ArenaRules rules, ArenaPet pet, string type)
        {
            var atks = rules.Def(pet.DefId).Attacks;
            if (atks.Count == 0) return false;
            var top = atks[atks.Count - 1];
            if (ArenaEngine.CostPaid(pet.Energy, top.Cost)) return false;
            var with = pet.Energy.Concat(new[] { type }).ToList();
            // useful if it pays for something or brings the top attack closer
            return atks.Any(a => !ArenaEngine.CostPaid(pet.Energy, a.Cost) && ArenaEngine.CostPaid(with, a.Cost)) || with.Count <= top.Cost.Count;
        }

        /// <summary>Index of the best usable attack (KO first, then highest damage), or -1.</summary>
        public static int BestAttack(ArenaRules rules, ArenaState st, int p)
        {
            var pl = st.Players[p]; var opp = st.Players[1 - p];
            if (pl.Active == null || opp.Active == null) return -1;
            var atks = rules.Def(pl.Active.DefId).Attacks;
            int best = -1, bestVal = -1;
            for (int i = 0; i < atks.Count; i++)
            {
                if (ArenaEngine.AttackProblem(rules, st, p, i) != null) continue;
                int dmg = ArenaEngine.AttackDamage(rules, st, p, pl.Active, atks[i], opp.Active, 0);
                bool ko = dmg >= ArenaEngine.MaxHp(rules, st, opp.Active) - opp.Active.Damage;
                int val = (ko ? 1000 : 0) + dmg * 3 + atks[i].Effects.Count * 5;
                if (val > bestVal) { bestVal = val; best = i; }
            }
            return best;
        }

        private static ArenaAction? TrainerChoice(ArenaRules rules, ArenaState st, int p, int c, ArenaCardDef d)
        {
            var pl = st.Players[p]; var opp = st.Players[1 - p]; var act = pl.Active;
            ArenaAction A(string type, params int[] targets) => new ArenaAction { Type = type, Card = c, Targets = targets.ToList() };
            var hurt = pl.InPlay().Where(x => x.Damage >= 20).OrderByDescending(x => x.Damage).ThenBy(x => x.Uid).FirstOrDefault();
            switch (d.Id)
            {
                case "at_herb_salve": return hurt != null ? A("item", hurt.Uid) : null;
                case "at_star_lantern": return pl.Bench.Count < rules.Config.BenchSize && pl.Deck.Any(x => rules.Def(st.Card(x).DefId).IsBasic) ? A("item") : null;
                case "at_lucky_coin": return pl.Hand.Count <= 5 && pl.Deck.Count > 0 ? A("item") : null;
                case "at_wind_boots":
                    return act != null && pl.Retreats == 0 && BestAttack(rules, st, p) < 0 && ArenaEngine.RetreatCost(rules, st, p) > act.Energy.Count && pl.Bench.Any(b => rules.Def(b.DefId).Attacks.Any(a => ArenaEngine.CostPaid(b.Energy, a.Cost))) ? A("item") : null;
                case "su_scholar": return pl.Hand.Count <= 6 && pl.Deck.Count > 0 ? A("supporter") : null;
                case "su_medic": return hurt != null && hurt.Damage >= 40 ? A("supporter", hurt.Uid) : null;
                case "su_captain":
                    if (opp.Bench.Count == 0 || act == null) return null;
                    var weak = opp.Bench.OrderBy(b => ArenaEngine.MaxHp(rules, st, b) - b.Damage).ThenBy(b => b.Uid).First();
                    return ArenaEngine.MaxHp(rules, st, weak) - weak.Damage <= rules.Def(act.DefId).Attacks.Select(a => a.Damage).DefaultIfEmpty(0).Max() && BestAttack(rules, st, p) >= 0 ? A("supporter", weak.Uid) : null;
                case "su_strategist": return BestAttack(rules, st, p) >= 0 ? A("supporter") : null;
                case "su_guide":
                    if (act == null || BestAttack(rules, st, p) >= 0 || ArenaEngine.AttackBanned(rules, st, p)) return null;
                    var r = pl.Bench.Where(b => rules.Def(b.DefId).Attacks.Any(a => ArenaEngine.CostPaid(b.Energy, a.Cost))).OrderBy(b => b.Uid).FirstOrDefault();
                    return r != null ? A("supporter", r.Uid) : null;
                case "su_smith":
                    var donor = pl.Bench.Where(b => b.Energy.Count > 0).OrderByDescending(b => b.Energy.Count).ThenBy(b => b.Uid).FirstOrDefault();
                    return donor != null && act != null && BestAttack(rules, st, p) < 0 ? A("supporter", donor.Uid) : null;
                case "su_tracker": return opp.Active != null && opp.Active.Energy.Count >= 2 ? A("supporter") : null;
                case "su_warden": return opp.Active != null && rules.Def(opp.Active.DefId).Ability != null ? A("supporter") : null;
            }
            if (d.Kind == "tool" && act != null && act.Tool == 0) return new ArenaAction { Type = "tool", Card = c, Uid = act.Uid };
            if (d.Kind == "tool") { var holder = pl.Bench.Where(b => b.Tool == 0).OrderBy(b => b.Uid).FirstOrDefault(); if (holder != null) return new ArenaAction { Type = "tool", Card = c, Uid = holder.Uid }; }
            return null;
        }

        /// <summary>Every action the rules would accept right now (used by random-play simulations and clients).
        /// Each action here is guaranteed to apply without a rule error.</summary>
        public static List<ArenaAction> LegalActions(ArenaRules rules, ArenaState st, int p)
        {
            var list = new List<ArenaAction>();
            if (!ArenaEngine.MustAct(st, p)) return list;
            var pl = st.Players[p]; var opp = st.Players[1 - p]; var cfg = rules.Config;
            if (st.Phase == "setup") { list.Add(SetupChoice(rules, st, p)); return list; }
            if (pl.MustPromote) { list.AddRange(pl.Bench.Select(b => new ArenaAction { Type = "promote", Uid = b.Uid })); return list; }
            ArenaCardDef D(int card) => rules.Def(st.Card(card).DefId);
            foreach (var c in pl.Hand.Distinct())
            {
                var d = D(c);
                if (d.IsBasic && pl.Bench.Count < cfg.BenchSize) list.Add(new ArenaAction { Type = "bench", Card = c });
                if (d.Kind == "pet" && d.Stage > 0 && !(cfg.NoEvolveFirstTurn && pl.TurnsTaken == 1))
                    foreach (var pet in pl.InPlay().Where(x => x.DefId == d.EvolvesFrom && x.PlayedTurn != st.Turn && x.EvolvedTurn != st.Turn))
                        list.Add(new ArenaAction { Type = "evolve", Card = c, Uid = pet.Uid });
                if (d.Kind == "tool") foreach (var pet in pl.InPlay().Where(x => x.Tool == 0)) list.Add(new ArenaAction { Type = "tool", Card = c, Uid = pet.Uid });
                if (d.Kind == "item" || (d.Kind == "supporter" && pl.SupportersUsed < cfg.SupportersPerTurn && (cfg.SupporterFirstTurn || pl.TurnsTaken > 1)))
                {
                    var choices = TargetChoices(rules, st, p, d);
                    foreach (var t in choices)
                        if (ArenaEngine.TrainerPlayable(rules, st, p, d, t)) list.Add(new ArenaAction { Type = d.Kind, Card = c, Targets = t });
                }
            }
            if (!pl.EnergyAttached && pl.ZoneCurrent != null) foreach (var pet in pl.InPlay()) list.Add(new ArenaAction { Type = "attach", Uid = pet.Uid });
            foreach (var pet in pl.InPlay())
            {
                var ab = rules.Def(pet.DefId).Ability;
                if (ab == null || ab.Trigger != "activated" || pet.AbilityTurn == st.Turn || ArenaEngine.AbilityLocked(st, pet)) continue;
                if (ab.Effects.Any(e => e.Cond == "is_active") && pl.Active != pet) continue;
                if (ab.Effects.All(e => e.Op == "heal") && (pl.Active == null || pl.Active.Damage == 0)) continue;
                list.Add(new ArenaAction { Type = "ability", Uid = pet.Uid });
            }
            if (pl.Active != null && pl.Retreats < cfg.RetreatsPerTurn && pl.Active.Special != "asleep" && pl.Active.Special != "paralyzed")
            {
                int cost = ArenaEngine.RetreatCost(rules, st, p);
                if (cost < 50 && pl.Active.Energy.Count >= cost) foreach (var b in pl.Bench) list.Add(new ArenaAction { Type = "retreat", Uid = b.Uid });
            }
            if (pl.Active != null)
            {
                var atks = rules.Def(pl.Active.DefId).Attacks;
                for (int i = 0; i < atks.Count; i++)
                {
                    if (ArenaEngine.AttackProblem(rules, st, p, i) != null) continue;
                    var t = new List<int>();
                    if (atks[i].Effects.Any(e => e.Target == "opp_bench_chosen") && opp.Bench.Count > 0) t.Add(opp.Bench[0].Uid);
                    if (atks[i].Effects.Any(e => e.Target == "own_bench_chosen") && pl.Bench.Count > 0) t.Add(pl.Bench[0].Uid);
                    list.Add(new ArenaAction { Type = "attack", Index = i, Targets = t });
                }
            }
            list.Add(new ArenaAction { Type = "end" });
            return list;
        }

        private static List<List<int>> TargetChoices(ArenaRules rules, ArenaState st, int p, ArenaCardDef d)
        {
            var pl = st.Players[p]; var opp = st.Players[1 - p];
            var needs = d.Effects.Where(e => e.Target.EndsWith("_chosen")).Select(e => e.Target).Distinct().ToList();
            if (needs.Count == 0) return new List<List<int>> { new List<int>() };
            var n = needs[0];
            IEnumerable<ArenaPet> pool = n == "own_chosen" ? pl.InPlay() : n == "own_bench_chosen" ? pl.Bench : opp.Bench;
            if (d.Effects.Any(e => e.Op == "energy_transfer")) pool = pool.Where(b => b.Energy.Count > 0);
            if (d.Effects.Any(e => e.Op == "heal") && !d.Effects.Any(e => e.Op == "cure")) pool = pool.Where(b => b.Damage > 0);
            return pool.Select(x => new List<int> { x.Uid }).ToList();
        }
    }
}
