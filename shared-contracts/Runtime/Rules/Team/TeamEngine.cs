using System;
using System.Collections.Generic;
using System.Linq;

namespace PetStar.Rules.Team
{
    /// <summary>
    /// Battle 3v3 rules engine (original). Round flow:
    /// PLANNING (both players lock one order per living Pet; plans stay hidden) → RESOLVE (orders sorted by skill
    /// priority, then effective SPD, then a seeded tie-break; each Pet acts once) → ROUND END (burn, poison, regen,
    /// status and cooldown countdown, EN regeneration) → next round, until one team has no Pet standing.
    ///
    /// Status rules: stun / freeze skip the Pet's next action, silence allows only the basic attack; hard control lasts
    /// HardCcTurns and is followed by CcImmunityTurns of immunity (no permanent lock). Poison stacks (max
    /// PoisonMaxStacks), every other status refreshes to the longer duration. Shields keep the larger value (capped).
    /// </summary>
    public static class TeamEngine
    {
        private static readonly HashSet<string> HardCc = new HashSet<string> { "stun", "freeze", "silence" };
        private static readonly HashSet<string> Debuffs = new HashSet<string> { "burn", "poison", "stun", "freeze", "silence", "weaken" };
        private static readonly HashSet<string> Dots = new HashSet<string> { "burn", "poison" };

        public static TeamState Create(TeamRules rules, TeamSetupPlayer a, TeamSetupPlayer b, ulong seed)
        {
            var cfg = rules.Config;
            var st = new TeamState { Rng = seed, DefsVersion = rules.Version };
            foreach (var (sp, side) in new[] { (a, 0), (b, 1) })
            {
                st.Players.Add(new TeamPlayerState { Name = sp.Name });
                if (sp.Units.Count != cfg.TeamSize) throw new TeamRuleException("team_size");
                for (int i = 0; i < sp.Units.Count; i++)
                {
                    var s = sp.Units[i];
                    var kit = rules.Kits.TryGetValue(s.FormId, out var k) ? k : throw new TeamRuleException("unknown_pet");
                    var u = new TeamUnit
                    {
                        Uid = st.Units.Count + 1, Side = side, Slot = i, FormId = s.FormId, CardInstanceId = s.CardInstanceId,
                        Element = kit.Element, Role = kit.Role, MaxHp = s.Hp, Hp = s.Hp, Atk = s.Atk, Def = s.Def, Mag = s.Mag, Res = s.Res,
                        Spd = s.Spd, CritBp = s.CritBp, Basic = kit.Basic, Skill = kit.Skill, Ultimate = kit.Ultimate, Passive = kit.Passive,
                        En = Math.Min(cfg.MaxEn, cfg.StartEn + (kit.Passive?.Op == "start_en" ? kit.Passive.Amount : 0)),
                    };
                    st.Units.Add(u);
                }
            }
            Log(st, "start", 0, 0, 0, "");
            return st;
        }

        public static IEnumerable<TeamUnit> Team(TeamState st, int side) => st.Units.Where(u => u.Side == side);
        public static bool MustAct(TeamState st, int p) => !st.Finished && !st.Players[p].Locked;
        public static bool Has(TeamUnit u, string status) => u.Statuses.Any(s => s.Type == status && s.Remaining > 0);

        public static TeamSkill? SkillFor(TeamRules rules, TeamUnit u, string slot)
        {
            var id = slot switch { "basic" => u.Basic, "skill" => u.Skill, "ultimate" => u.Ultimate, _ => null };
            return id != null && rules.Skills.TryGetValue(id, out var s) ? s : null;
        }

        /// <summary>Why this order cannot be given now (null = allowed). Used by the server and the clients.</summary>
        public static string? OrderProblem(TeamRules rules, TeamState st, int p, TeamOrder o)
        {
            var u = st.Units.FirstOrDefault(x => x.Uid == o.Unit);
            if (u == null || u.Side != p) return "invalid_unit";
            if (!u.Alive) return "unit_down";
            if (o.Slot == "guard") return null;
            var sk = SkillFor(rules, u, o.Slot);
            if (sk == null) return "no_such_skill";
            if (sk.Kind != "basic" && Has(u, "silence")) return "silenced";
            if (u.En < sk.Cost) return "not_enough_en";
            if (u.Cooldowns.TryGetValue(sk.Id, out var cd) && cd > 0) return "on_cooldown";
            if (o.Target != 0)
            {
                var t = st.Units.FirstOrDefault(x => x.Uid == o.Target);
                if (t == null || !t.Alive) return "invalid_target";
                bool enemyTarget = sk.Target.StartsWith("enemy");
                if (enemyTarget && t.Side == p) return "invalid_target";
                if (!enemyTarget && t.Side != p) return "invalid_target";
            }
            return null;
        }

        public static void Apply(TeamRules rules, TeamState st, int p, TeamAction a)
        {
            if (st.Finished) throw new TeamRuleException("match_finished");
            if (p != 0 && p != 1) throw new TeamRuleException("invalid_player");
            if (a.Type == "surrender") { Finish(st, 1 - p, "surrender"); st.Version++; return; }
            if (a.Type != "plan") throw new TeamRuleException("invalid_action");
            if (st.Players[p].Locked) throw new TeamRuleException("plan_locked");
            var orders = new List<TeamOrder>();
            foreach (var o in a.Orders)
            {
                if (orders.Any(x => x.Unit == o.Unit)) throw new TeamRuleException("duplicate_order");
                var problem = OrderProblem(rules, st, p, o);
                if (problem != null) throw new TeamRuleException(problem);
                orders.Add(new TeamOrder { Unit = o.Unit, Slot = o.Slot, Target = o.Target });
            }
            st.Players[p].Timeouts = 0;
            Lock(rules, st, p, orders);
            st.Version++;
        }

        private static void Lock(TeamRules rules, TeamState st, int p, List<TeamOrder> orders)
        {
            var pl = st.Players[p];
            // a living Pet without an order uses its basic attack on an automatic target
            foreach (var u in Team(st, p).Where(x => x.Alive && orders.All(o => o.Unit != x.Uid)).OrderBy(x => x.Slot))
                orders.Add(new TeamOrder { Unit = u.Uid, Slot = "basic" });
            pl.Plan = orders; pl.Locked = true;
            Log(st, "locked", 0, 0, p, "");
            if (st.Players.All(x => x.Locked)) Resolve(rules, st);
        }

        /// <summary>Players who did not lock in time get an automatic plan; repeated timeouts forfeit.</summary>
        public static void Timeout(TeamRules rules, TeamState st)
        {
            if (st.Finished) return;
            var late = Enumerable.Range(0, 2).Where(p => !st.Players[p].Locked).ToList();
            int round = st.Round;
            foreach (int p in late)
            {
                var pl = st.Players[p];
                pl.Timeouts++;
                Log(st, "timeout", 0, 0, p, pl.Timeouts.ToString());
                if (pl.Timeouts >= rules.Config.TimeoutsToForfeit) { Finish(st, 1 - p, "timeout"); break; }
            }
            foreach (int p in late)
            {
                if (st.Finished || st.Round != round || st.Players[p].Locked) break;
                Lock(rules, st, p, TeamAi.Decide(rules, st, p).Orders);
            }
            st.Version++;
        }

        // ------------------------------------------------------------ resolution

        public static int EffSpd(TeamRules rules, TeamUnit u) => Has(u, "haste") ? u.Spd * (100 + rules.Config.HastePct) / 100 : u.Spd;

        private static void Resolve(TeamRules rules, TeamState st)
        {
            var orders = st.Players.SelectMany(pl => pl.Plan!).ToList();
            var tie = new Dictionary<int, int>();
            foreach (var o in orders.OrderBy(o => o.Unit)) tie[o.Unit] = R(st, 1_000_000);
            var queue = orders.Select(o => (o, u: st.Units.First(x => x.Uid == o.Unit), sk: o.Slot == "guard" ? null : SkillFor(rules, st.Units.First(x => x.Uid == o.Unit), o.Slot)))
                .OrderByDescending(x => x.o.Slot == "guard" ? 9 : x.sk!.Priority).ThenByDescending(x => EffSpd(rules, x.u)).ThenBy(x => tie[x.o.Unit]).ToList();
            Log(st, "round", 0, 0, st.Round, "");
            foreach (var (o, u, sk0) in queue)
            {
                if (st.Finished) break;
                if (!u.Alive) continue;
                if (Has(u, "stun") || Has(u, "freeze")) { Log(st, "skip", u.Uid, 0, 0, Has(u, "stun") ? "stun" : "freeze"); u.Acted = true; continue; }
                if (o.Slot == "guard") { u.Statuses.RemoveAll(s => s.Type == "guard"); u.Statuses.Add(new TeamStatus { Type = "guard", Remaining = 1 }); Log(st, "guard", u.Uid, 0, 0, ""); u.Acted = true; continue; }
                var sk = sk0!;
                if (sk.Kind != "basic" && Has(u, "silence")) { Log(st, "silenced", u.Uid, 0, 0, sk.Id); sk = rules.Skills[u.Basic]; }
                if (u.En < sk.Cost || (u.Cooldowns.TryGetValue(sk.Id, out var cd) && cd > 0)) { Log(st, "fizzle", u.Uid, 0, 0, sk.Id); sk = rules.Skills[u.Basic]; }
                u.En -= sk.Cost;
                if (sk.Kind == "basic") u.En = Math.Min(rules.Config.MaxEn, u.En + rules.Config.BasicEnGain);
                if (sk.Cooldown > 0) u.Cooldowns[sk.Id] = sk.Cooldown + 1;
                Act(rules, st, u, sk, o.Target);
                u.Acted = true;
                CheckEnd(st);
            }
            if (!st.Finished) RoundEnd(rules, st);
        }

        private static List<TeamUnit> Targets(TeamState st, TeamUnit u, TeamSkill sk, int chosen)
        {
            var enemies = Team(st, 1 - u.Side).Where(x => x.Alive).OrderBy(x => x.Slot).ToList();
            var allies = Team(st, u.Side).Where(x => x.Alive).OrderBy(x => x.Slot).ToList();
            switch (sk.Target)
            {
                case "enemy_single":
                    if (enemies.Count == 0) return new List<TeamUnit>();
                    var taunt = enemies.FirstOrDefault(x => Has(x, "taunt"));
                    if (taunt != null) return new List<TeamUnit> { taunt };
                    var pick = enemies.FirstOrDefault(x => x.Uid == chosen);
                    return new List<TeamUnit> { pick ?? enemies.OrderBy(x => x.Hp).ThenBy(x => x.Slot).First() };
                case "enemy_all": return enemies;
                case "ally_all": return allies;
                case "ally_lowest": return allies.Count == 0 ? allies : new List<TeamUnit> { allies.OrderBy(x => x.Hp * 1000 / x.MaxHp).ThenBy(x => x.Slot).First() };
                default: return new List<TeamUnit> { u };   // self
            }
        }

        private static void Act(TeamRules rules, TeamState st, TeamUnit u, TeamSkill sk, int chosen)
        {
            var cfg = rules.Config;
            var targets = Targets(st, u, sk, chosen);
            bool offensive = sk.Target.StartsWith("enemy");
            Log(st, "act", u.Uid, targets.FirstOrDefault()?.Uid ?? 0, u.En, sk.Id);
            foreach (var e in sk.Effects)
            {
                switch (e.Op)
                {
                    case "damage":
                        foreach (var t in targets.Where(x => x.Alive)) Hit(rules, st, u, t, sk, e.Power);
                        break;
                    case "heal":
                        foreach (var t in (offensive ? new List<TeamUnit> { u } : targets).Where(x => x.Alive))
                        {
                            int bonus = u.Passive?.Op == "heal_bonus" ? u.Passive.Pct : 0;
                            int amount = e.Power * u.Mag / 100 * (100 + bonus) / 100;
                            Heal(st, t, amount, u.Uid);
                        }
                        break;
                    case "shield":
                        foreach (var t in (offensive ? new List<TeamUnit> { u } : targets).Where(x => x.Alive))
                        {
                            int basis = e.Scale == "res" ? u.Res : u.Def;
                            int amount = Math.Min(t.MaxHp * cfg.ShieldCapPct / 100, e.Power * basis / 100 * cfg.DamageScalePct / 100);
                            if (amount > t.Shield) { t.Shield = amount; Log(st, "shield", u.Uid, t.Uid, amount, ""); }
                        }
                        break;
                    case "cleanse":
                        foreach (var t in (offensive ? new List<TeamUnit> { u } : targets).Where(x => x.Alive))
                        {
                            int n = t.Statuses.RemoveAll(s => Debuffs.Contains(s.Type));
                            if (n > 0) Log(st, "cleanse", u.Uid, t.Uid, n, "");
                        }
                        break;
                    case "energy":
                        foreach (var t in Team(st, u.Side).Where(x => x.Alive && x.Uid != u.Uid).OrderBy(x => x.Slot))
                        {
                            t.En = Math.Min(cfg.MaxEn, t.En + e.Amount);
                            Log(st, "energy", u.Uid, t.Uid, e.Amount, "");
                        }
                        break;
                    case "status":
                        var on = e.On == "self" ? new List<TeamUnit> { u } : e.On == "ally" ? (offensive ? new List<TeamUnit> { u } : targets) : (offensive ? targets : new List<TeamUnit>());
                        foreach (var t in on.Where(x => x.Alive))
                            if (R(st, 100) < e.Chance) AddStatus(rules, st, u, t, e.Status!, e.Turns);
                        break;
                }
            }
        }

        private static void Hit(TeamRules rules, TeamState st, TeamUnit u, TeamUnit t, TeamSkill sk, int power)
        {
            var cfg = rules.Config;
            int a = sk.Dmg == "mag" ? u.Mag : u.Atk;
            int d = sk.Dmg == "mag" ? t.Res : t.Def;
            if (u.Passive?.Op == "rage" && u.Hp * 100 < u.MaxHp * u.Passive.ThresholdPct) a = a * (100 + u.Passive.Pct) / 100;
            long dmg = (long)power * a * a / Math.Max(1, a + d) / 100 * cfg.DamageScalePct / 100;
            string tag = "";
            if (rules.Strong.TryGetValue(u.Element, out var s1) && s1.Contains(t.Element)) { dmg = dmg * cfg.StrongPct / 100; tag = "strong"; }
            else if (rules.Strong.TryGetValue(t.Element, out var s2) && s2.Contains(u.Element)) { dmg = dmg * cfg.WeakPct / 100; tag = "weak"; }
            if (R(st, 10000) < u.CritBp) { dmg = dmg * cfg.CritDamagePct / 100; tag += tag.Length > 0 ? "+crit" : "crit"; }
            if (Has(u, "weaken")) dmg = dmg * (100 - cfg.WeakenPct) / 100;
            if (t.Passive?.Op == "reduce") dmg = dmg * (100 - t.Passive.Pct) / 100;
            if (Has(t, "guard")) dmg = dmg * (100 - cfg.GuardReductionPct) / 100;
            int dealt = (int)Math.Max(1, dmg);
            int absorbed = Math.Min(t.Shield, dealt);
            t.Shield -= absorbed;
            int hpLoss = Math.Min(t.Hp, dealt - absorbed);
            t.Hp -= hpLoss;
            Log(st, "damage", u.Uid, t.Uid, dealt, tag + (absorbed > 0 ? $"|shield{absorbed}" : ""));
            if (t.Statuses.RemoveAll(x => x.Type == "freeze" && u.Element == "fire") > 0) Log(st, "thaw", u.Uid, t.Uid, 0, "");
            if (!t.Alive) Log(st, "down", u.Uid, t.Uid, 0, "");
            if (u.Passive?.Op == "drain" && hpLoss > 0 && u.Alive) Heal(st, u, hpLoss * u.Passive.Pct / 100, u.Uid);
            if (Has(t, "counter") && u.Alive && hpLoss + absorbed > 0)
            {
                int back = Math.Max(1, dealt * cfg.CounterPct / 100);
                int abs2 = Math.Min(u.Shield, back); u.Shield -= abs2;
                u.Hp -= Math.Min(u.Hp, back - abs2);
                Log(st, "counter", t.Uid, u.Uid, back, "");
                if (!u.Alive) Log(st, "down", t.Uid, u.Uid, 0, "");
            }
        }

        private static void Heal(TeamState st, TeamUnit t, int amount, int by)
        {
            int h = Math.Min(t.MaxHp - t.Hp, Math.Max(0, amount));
            if (h <= 0) return;
            t.Hp += h;
            Log(st, "heal", by, t.Uid, h, "");
        }

        private static void AddStatus(TeamRules rules, TeamState st, TeamUnit by, TeamUnit t, string type, int turns)
        {
            var cfg = rules.Config;
            if (HardCc.Contains(type))
            {
                if (t.CcImmune > 0 || t.Statuses.Any(s => HardCc.Contains(s.Type))) { Log(st, "resist", by.Uid, t.Uid, 0, type); return; }
                turns = cfg.HardCcTurns;
            }
            int remaining = Dots.Contains(type) ? turns : turns + (t.Acted ? 1 : 0);
            var cur = t.Statuses.FirstOrDefault(s => s.Type == type);
            if (cur == null) t.Statuses.Add(new TeamStatus { Type = type, Remaining = remaining });
            else
            {
                cur.Remaining = Math.Max(cur.Remaining, remaining);
                if (type == "poison") cur.Stacks = Math.Min(cfg.PoisonMaxStacks, cur.Stacks + 1);
            }
            Log(st, "status", by.Uid, t.Uid, remaining, type);
        }

        private static void RoundEnd(TeamRules rules, TeamState st)
        {
            var cfg = rules.Config;
            foreach (var u in st.Units.Where(x => x.Alive).OrderBy(x => x.Uid))
            {
                foreach (var s in u.Statuses.Where(s => Dots.Contains(s.Type)).ToList())
                {
                    int dmg = Math.Max(1, u.MaxHp * (s.Type == "burn" ? cfg.BurnPct : cfg.PoisonPct * s.Stacks) / 100);
                    u.Hp -= Math.Min(u.Hp, dmg);
                    Log(st, s.Type, 0, u.Uid, dmg, "");
                    if (!u.Alive) { Log(st, "down", 0, u.Uid, 0, s.Type); break; }
                }
                if (!u.Alive) continue;
                if (Has(u, "regen")) Heal(st, u, u.MaxHp * cfg.RegenPct / 100, u.Uid);
                if (u.Passive?.Op == "regen") Heal(st, u, u.MaxHp * u.Passive.Pct / 100, u.Uid);
            }
            foreach (var u in st.Units.OrderBy(x => x.Uid))
            {
                if (u.CcImmune > 0) u.CcImmune--;
                foreach (var s in u.Statuses) s.Remaining--;
                foreach (var gone in u.Statuses.Where(s => s.Remaining <= 0).ToList())
                {
                    if (HardCc.Contains(gone.Type)) u.CcImmune = cfg.CcImmunityTurns;
                    u.Statuses.Remove(gone);
                }
                foreach (var k in u.Cooldowns.Keys.ToList()) u.Cooldowns[k] = Math.Max(0, u.Cooldowns[k] - 1);
                if (u.Alive) u.En = Math.Min(cfg.MaxEn, u.En + cfg.RoundEnGain);
                u.Acted = false;
            }
            CheckEnd(st);
            if (st.Finished) return;
            if (st.Round >= cfg.MaxRounds)
            {
                long Score(int side) => Team(st, side).Sum(x => (long)x.Hp * 10000 / x.MaxHp);
                long a = Score(0), b = Score(1);
                Finish(st, a > b ? 0 : b > a ? 1 : (int?)null, "round_limit");
                return;
            }
            st.Round++;
            foreach (var pl in st.Players) { pl.Plan = null; pl.Locked = false; }
            Log(st, "plan", 0, 0, st.Round, "");
        }

        private static void CheckEnd(TeamState st)
        {
            if (st.Finished) return;
            bool aDead = Team(st, 0).All(x => !x.Alive), bDead = Team(st, 1).All(x => !x.Alive);
            if (aDead && bDead) Finish(st, null, "both_down");
            else if (aDead) Finish(st, 1, "knockout");
            else if (bDead) Finish(st, 0, "knockout");
        }

        private static void Finish(TeamState st, int? winner, string reason)
        {
            if (st.Finished) return;
            st.Phase = "finished"; st.Winner = winner; st.EndReason = reason;
            foreach (var pl in st.Players) pl.Locked = true;
            Log(st, "finish", 0, 0, winner ?? -1, reason);
        }

        internal static int R(TeamState st, int n)
        {
            var r = new SeededRandom(st.Rng);
            int v = r.Next(n);
            st.Rng = r.State;
            return v;
        }

        private static void Log(TeamState st, string type, int a, int b, int n, string s) =>
            st.Log.Add(new TeamEvent { Type = type, Round = st.Round, A = a, B = b, N = n, S = s });
    }
}
