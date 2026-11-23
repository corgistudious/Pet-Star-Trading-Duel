using System.Collections.Generic;
using System.Linq;

namespace PetStar.Rules.Team
{
    /// <summary>Rule-abiding 3v3 opponent (PvE, timeouts). Deterministic: ties broken by slot/uid; reads only public state.</summary>
    public static class TeamAi
    {
        public static TeamAction Decide(TeamRules rules, TeamState st, int p)
        {
            var orders = new List<TeamOrder>();
            var allies = TeamEngine.Team(st, p).Where(u => u.Alive).OrderBy(u => u.Slot).ToList();
            var enemies = TeamEngine.Team(st, 1 - p).Where(u => u.Alive).OrderBy(u => u.Slot).ToList();
            if (enemies.Count == 0) return new TeamAction { Orders = orders };
            bool allyHurt = allies.Any(a => a.Hp * 100 < a.MaxHp * 55);
            var focus = enemies.OrderBy(e => e.Hp * 1000 / e.MaxHp).ThenBy(e => e.Slot).First();
            foreach (var u in allies)
            {
                string slot = "basic";
                foreach (var cand in new[] { "ultimate", "skill" })
                {
                    var sk = TeamEngine.SkillFor(rules, u, cand);
                    if (sk == null) continue;
                    if (TeamEngine.OrderProblem(rules, st, p, new TeamOrder { Unit = u.Uid, Slot = cand }) != null) continue;
                    bool healing = sk.Effects.Any(e => e.Op == "heal") && !sk.Target.StartsWith("enemy");
                    bool supportOnly = !sk.Target.StartsWith("enemy") && !healing;
                    if (healing && !allyHurt) continue;
                    if (supportOnly && st.Round > 1 && cand == "skill" && u.En < 70) continue;   // keep EN for offense later in the fight
                    slot = cand; break;
                }
                var chosen = TeamEngine.SkillFor(rules, u, slot)!;
                orders.Add(new TeamOrder { Unit = u.Uid, Slot = slot, Target = chosen.Target == "enemy_single" ? focus.Uid : 0 });
            }
            return new TeamAction { Orders = orders };
        }
    }
}
