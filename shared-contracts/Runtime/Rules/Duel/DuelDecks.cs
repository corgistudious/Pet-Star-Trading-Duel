using System;
using System.Collections.Generic;
using System.Linq;

namespace PetStar.Rules.Duel
{
    /// <summary>One entry of a 40-card duel deck: a Pet card instance the player owns, or a Spell/Trap (tactic) card.</summary>
    public sealed class DeckEntryInfo
    {
        public string Kind { get; set; } = "pet";        // pet | tactic
        public long CardInstanceId { get; set; }
        public string FormId { get; set; } = "";
        public bool Divine { get; set; }
        public string? TacticId { get; set; }
        public int Count { get; set; } = 1;              // tactics only (pets are single instances)
    }

    public static class DuelDeckRules
    {
        /// <returns>null when valid, otherwise an error code. Ownership/locks are checked by the server before calling.</returns>
        public static string? Validate(DuelRules rules, IList<DeckEntryInfo> entries)
        {
            var cfg = rules.Config;
            if (entries.Any(e => e.Count < 1)) return "deck_invalid_count";
            int pets = entries.Where(e => e.Kind == "pet").Sum(e => e.Count);
            int total = entries.Sum(e => e.Count);
            if (total != cfg.DeckSize) return "deck_must_have_40";
            if (pets < cfg.PetMin || pets > cfg.PetMax) return "deck_pet_count";
            if (entries.Where(e => e.Kind == "pet").GroupBy(e => e.CardInstanceId).Any(g => g.Sum(x => x.Count) > 1)) return "deck_duplicate_instance";
            foreach (var g in entries.Where(e => e.Kind == "pet").GroupBy(e => e.FormId))
            {
                bool divine = g.First().Divine;
                if (g.Sum(x => x.Count) > (divine ? cfg.DivineMaxCopies : cfg.MaxCopies)) return divine ? "deck_divine_copies" : "deck_too_many_copies";
            }
            foreach (var g in entries.Where(e => e.Kind == "tactic").GroupBy(e => e.TacticId))
            {
                if (g.Key == null || !rules.Tactics.ContainsKey(g.Key)) return "deck_unknown_card";
                if (g.Sum(x => x.Count) > cfg.MaxCopies) return "deck_too_many_copies";
            }
            return null;
        }
    }

    /// <summary>Builds duel rules and Pet stats from the real catalog and card progression (server side and tests).</summary>
    public static class DuelBuilder
    {
        public static DuelRules Rules(GameCatalog cat, DuelConfig cfg, IEnumerable<TacticDef> tactics)
        {
            var r = new DuelRules { Config = cfg };
            foreach (var t in tactics) r.Tactics[t.Id] = t;
            foreach (var s in cat.Skills.Values)
                r.Skills[s.Id] = new DuelSkill
                {
                    Id = s.Id, Cost = s.Cost, Target = s.Target,
                    Effects = s.Effects.Select(e => new DuelSkillEffect
                    {
                        Type = e.Type, PowerPct = (int)Math.Round(e.Power * 100), Scale = e.Scale, Status = e.Status,
                        Chance = (int)Math.Round(e.Chance * 100), Duration = e.Duration,
                    }).ToList(),
                };
            foreach (var p in cat.Passives.Values)
                r.Passives[p.Id] = new DuelPassive { Type = p.Type, Stat = p.Stat, Pct = (int)Math.Round(p.Pct * 100), Amount = p.Amount };
            foreach (var e in cat.Elements.Elements) r.Strong[e.Id] = e.StrongAgainst.ToList();
            return r;
        }

        /// <summary>Stats for the card's current form and every later form of its family line (in-duel evolution).
        /// Ranked caps the out-of-duel growth (stars × enhancement × awakening × engravings) at RankedGrowthCap.</summary>
        public static List<PetFormStats> PetForms(GameCatalog cat, DuelConfig cfg, string formId, string rarity, string quality, CardProgress progress, bool ranked)
        {
            var start = cat.Form(formId);
            var fam = cat.Families.First(f => f.Id == start.FamilyId);
            var list = new List<PetFormStats>();
            foreach (var f in fam.Forms.Where(f => f.Stage >= start.Stage).OrderBy(f => f.Stage))
            {
                var s = Progression.Compute(cat, f.Id, rarity, quality, progress);
                if (ranked)
                {
                    var plain = Progression.Compute(cat, f.Id, rarity, quality, new CardProgress { Level = progress.Level, Breakthrough = progress.Breakthrough, SkillLevel = progress.SkillLevel });
                    double cap = cfg.RankedGrowthCap / 100.0;
                    if (s.Atk > plain.Atk * cap) s.Atk = (int)Math.Round(plain.Atk * cap);
                    if (s.Hp > plain.Hp * cap) s.Hp = (int)Math.Round(plain.Hp * cap);
                    if (s.Def > plain.Def * cap) s.Def = (int)Math.Round(plain.Def * cap);
                }
                list.Add(new PetFormStats
                {
                    FormId = f.Id, Stage = f.FamilyStages == 1 ? 1 : f.Stage, Divine = f.DivineSet != null, Element = f.Element, Role = f.Role,
                    Hp = s.Hp, Atk = s.Atk, Def = s.Def, Spd = s.Spd, CritBp = (int)Math.Round(s.Crit * 10000),
                    Skill = f.Skill, Ultimate = f.Ultimate, Passive = f.Passive,
                });
            }
            return list;
        }
    }
}
