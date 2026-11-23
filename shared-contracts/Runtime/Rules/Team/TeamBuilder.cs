using System;

namespace PetStar.Rules.Team
{
    /// <summary>Server-side battle stats for an owned Pet card: HP/ATK/DEF/SPD/Crit from the card's progression (same
    /// formula as the collection), MAG/RES derived from ATK/DEF by role (team_battle.json → roles). Ranked can cap growth.</summary>
    public static class TeamBuilder
    {
        public static TeamSetupUnit Unit(GameCatalog cat, TeamRules rules, string formId, string rarity, string quality, CardProgress progress, long cardInstanceId, int? rankedGrowthCapPct = null)
        {
            var f = cat.Form(formId);
            var s = Progression.Compute(cat, formId, rarity, quality, progress);
            int hp = s.Hp, atk = s.Atk, def = s.Def, spd = s.Spd; double crit = s.Crit;
            var p = f.Passive != null && cat.Passives.TryGetValue(f.Passive, out var pd) ? pd : null;
            if (p != null && p.Type == "stat_bonus")
            {
                switch (p.Stat)
                {
                    case "atk": atk = (int)Math.Round(atk * (1 + p.Pct)); break;
                    case "def": def = (int)Math.Round(def * (1 + p.Pct)); break;
                    case "spd": spd = (int)Math.Round(spd * (1 + p.Pct)); break;
                    case "crit": crit = Math.Min(0.5, crit + p.Pct); break;
                }
            }
            if (rankedGrowthCapPct is int cap)
            {
                var b = Progression.Compute(cat, formId, "N", "C", new CardProgress { Level = 1 });
                hp = Math.Min(hp, b.Hp * cap / 100 * 3); atk = Math.Min(atk, b.Atk * cap / 100 * 3);
                def = Math.Min(def, b.Def * cap / 100 * 3); spd = Math.Min(spd, b.Spd * cap / 100 * 2);
            }
            var role = rules.Roles.TryGetValue(f.Role, out var r) ? r : new TeamRole();
            return new TeamSetupUnit
            {
                FormId = formId, CardInstanceId = cardInstanceId, Hp = hp, Atk = atk, Def = def,
                Mag = atk * role.MagPct / 100, Res = def * role.ResPct / 100, Spd = spd, CritBp = (int)Math.Round(crit * 10000),
            };
        }
    }
}
