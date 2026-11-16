using System;
using System.Collections.Generic;
using System.Linq;

namespace PetStar.Rules
{
    /// <summary>Everything that makes one card instance stronger than its species baseline. Stored per card on the
    /// server; the client only displays it (the server recomputes stats for every battle).</summary>
    public sealed class CardProgress
    {
        public int Level { get; set; } = 1;
        public int Stars { get; set; } = 1;
        public int Enhance { get; set; }
        public int Breakthrough { get; set; }
        public bool Awakened { get; set; }
        public int SkillLevel { get; set; } = 1;
        public List<EngravingInfo> Engravings { get; set; } = new List<EngravingInfo>();
    }

    public sealed class EngravingInfo
    {
        public string Type { get; set; } = "";   // atk | def | hp | spd | crit
        public int Level { get; set; } = 1;      // 1..5
    }

    public sealed class UpgradeCost
    {
        public long Beans { get; set; }
        public Dictionary<string, int> Items { get; set; } = new Dictionary<string, int>();
        public int MinLevel { get; set; }
        public int MinStars { get; set; }
        public int MinBreakthrough { get; set; }
    }

    /// <summary>
    /// Card progression rules (balance baseline, configurable here and covered by the balance tests).
    /// Every bonus is bounded: 10 stars, +20 enhancement, 4 breakthroughs (level cap 50 → 70), one awakening,
    /// skill level 10, 3 engravings with per-stat caps. Bonuses multiply the species stats; nothing is unbounded.
    /// </summary>
    public static class Progression
    {
        public const int MaxStars = 10;
        public const int MaxEnhance = 20;
        public const int MaxBreakthrough = 4;
        public const int BaseLevelCap = StatCalculator.MaxLevel;   // 50
        public const int LevelsPerBreakthrough = 5;                 // → 70 after 4 breakthroughs
        public const int MaxSkillLevel = 10;
        public const int MaxEngravingSlots = 3;
        public const int MaxEngravingLevel = 5;
        public const int MaxSameEngravingType = 2;
        public const int DivineStars = 10;

        public const double StarBonusPerStar = 0.04;                // 10★ = +36 %
        public const double AwakenStatBonus = 0.06;
        public const double AwakenSkillBonus = 0.05;

        // material item ids (inventory_items)
        public const string PetShard = "pet_shard";
        public const string EnhanceStone = "enhance_stone";
        public const string BreakthroughCore = "breakthrough_core";
        public const string AwakeningCrystal = "awakening_crystal";
        public const string EngravingDust = "engraving_dust";
        public const string TrainingScroll = TrainingRules.ScrollItemId;
        public static readonly string[] Materials = { PetShard, EnhanceStone, BreakthroughCore, AwakeningCrystal, EngravingDust, TrainingScroll };

        public static readonly string[] EngravingTypes = { "atk", "def", "hp", "spd", "crit" };

        public static int LevelCap(int breakthrough) => BaseLevelCap + LevelsPerBreakthrough * Clamp(breakthrough, 0, MaxBreakthrough);

        // ------------------------------------------------------------------ stars (1 → 10)

        private static readonly long[] StarBeans = { 0, 0, 100, 200, 350, 500, 800, 1200, 1800, 2600, 4000 };
        private static readonly int[] StarShards = { 0, 0, 5, 10, 15, 25, 40, 60, 90, 130, 200 };

        /// <summary>Cost to go from <paramref name="stars"/> to stars + 1, or null at 10★.</summary>
        public static UpgradeCost? StarCost(int stars)
        {
            int to = stars + 1;
            if (stars < 1 || to > MaxStars) return null;
            return new UpgradeCost { Beans = StarBeans[to], Items = { [PetShard] = StarShards[to] } };
        }

        public static double StarMultiplier(int stars) => 1 + StarBonusPerStar * (Clamp(stars, 1, MaxStars) - 1);

        // ------------------------------------------------------------------ enhancement (+0 → +20, always succeeds)

        private static readonly long[] EnhBeans = { 0, 100, 150, 200, 250, 300, 450, 500, 550, 600, 700, 900, 1000, 1100, 1200, 1400, 1800, 2000, 2200, 2500, 3000 };
        private static readonly int[] EnhStones = { 0, 1, 1, 2, 2, 3, 4, 4, 5, 5, 6, 8, 8, 10, 10, 12, 15, 15, 18, 18, 20 };

        /// <summary>Tier of an enhancement level: 1 basic (+1..5), 2 advanced (+6..10), 3 superior (+11..15), 4 ultimate (+16..20).</summary>
        public static int EnhanceTier(int level) => level <= 0 ? 0 : (Math.Min(level, MaxEnhance) - 1) / 5 + 1;

        public static UpgradeCost? EnhanceCost(int enhance)
        {
            int to = enhance + 1;
            if (enhance < 0 || to > MaxEnhance) return null;
            // higher tiers need a sturdier card: +6 needs level 10, +11 level 25, +16 level 40
            int minLevel = EnhanceTier(to) switch { 2 => 10, 3 => 25, 4 => 40, _ => 1 };
            return new UpgradeCost { Beans = EnhBeans[to], Items = { [EnhanceStone] = EnhStones[to] }, MinLevel = minLevel };
        }

        /// <summary>Cumulative stat bonus: +1 % per level (+1..5), +1.5 % (+6..10), +2 % (+11..15), +2.5 % (+16..20) → +35 % at +20.</summary>
        public static double EnhanceBonus(int enhance)
        {
            double b = 0;
            for (int l = 1; l <= Clamp(enhance, 0, MaxEnhance); l++) b += 0.005 * (EnhanceTier(l) + 1);
            return b;
        }

        /// <summary>Total spent to reach +level (used for the 50 % reset refund).</summary>
        public static UpgradeCost EnhanceSpent(int enhance)
        {
            var c = new UpgradeCost { Items = { [EnhanceStone] = 0 } };
            for (int l = 1; l <= Clamp(enhance, 0, MaxEnhance); l++) { c.Beans += EnhBeans[l]; c.Items[EnhanceStone] += EnhStones[l]; }
            return c;
        }

        // ------------------------------------------------------------------ breakthrough (level cap 50 → 70)

        private static readonly int[] BtMinStars = { 0, 3, 5, 7, 9 };
        private static readonly int[] BtCores = { 0, 5, 10, 20, 40 };
        private static readonly long[] BtBeans = { 0, 2000, 5000, 10000, 20000 };

        public static UpgradeCost? BreakthroughCost(int breakthrough)
        {
            int to = breakthrough + 1;
            if (breakthrough < 0 || to > MaxBreakthrough) return null;
            return new UpgradeCost
            {
                Beans = BtBeans[to], Items = { [BreakthroughCore] = BtCores[to] },
                MinLevel = LevelCap(breakthrough),   // must have reached the current cap
                MinStars = BtMinStars[to],
            };
        }

        // ------------------------------------------------------------------ awakening (once)

        public static UpgradeCost AwakenCost() => new UpgradeCost
        {
            Beans = 30000, Items = { [AwakeningCrystal] = 10 }, MinLevel = 60, MinStars = MaxStars, MinBreakthrough = 2,
        };

        // ------------------------------------------------------------------ skill level (1 → 10)

        /// <summary>Levels 2..5 keep the existing training costs; 6..10 need breakthroughs.</summary>
        public static UpgradeCost? SkillCost(int skillLevel)
        {
            if (skillLevel < 1 || skillLevel >= MaxSkillLevel) return null;
            var t = TrainingRules.Next(skillLevel);
            if (t != null) return new UpgradeCost { Beans = t.Beans, Items = { [TrainingScroll] = t.Scrolls }, MinLevel = t.MinCardLevel };
            int[] scrolls = { 12, 16, 20, 25, 30 };
            long[] beans = { 3000, 4000, 6000, 8000, 12000 };
            int i = skillLevel - 5;   // 5→6 .. 9→10
            return new UpgradeCost { Beans = beans[i], Items = { [TrainingScroll] = scrolls[i] }, MinLevel = 45, MinBreakthrough = i + 1 > 4 ? 4 : i + 1 };
        }

        public static UpgradeCost SkillSpent(int skillLevel)
        {
            var c = new UpgradeCost { Items = { [TrainingScroll] = 0 } };
            for (int l = 1; l < Clamp(skillLevel, 1, MaxSkillLevel); l++)
            {
                var s = SkillCost(l)!;
                c.Beans += s.Beans; c.Items[TrainingScroll] += s.Items[TrainingScroll];
            }
            return c;
        }

        /// <summary>Skill/ultimate power multiplier: +6 % per level up to 5 (unchanged), +3 % per level 6..10, +5 % awakened.
        /// Max 1.39 (1.44 awakened). Basic attacks are never scaled.</summary>
        public static double SkillPowerMultiplier(int skillLevel, bool awakened, double lowBonus = 0.06, double highBonus = 0.03)
        {
            int l = Clamp(skillLevel, 1, MaxSkillLevel);
            double m = 1 + lowBonus * (Math.Min(l, 5) - 1) + highBonus * Math.Max(0, l - 5);
            return m + (awakened ? AwakenSkillBonus : 0);
        }

        // ------------------------------------------------------------------ engravings

        /// <summary>Bonus of one engraving at a level. atk/def/hp: +1 %..+5 %; spd: +0.6 %..+3 %; crit: +0.5..+2.5 points.</summary>
        public static double EngravingValue(string type, int level)
        {
            int l = Clamp(level, 1, MaxEngravingLevel);
            switch (type)
            {
                case "atk": case "def": case "hp": return 0.01 * l;
                case "spd": return 0.006 * l;
                case "crit": return 0.005 * l;
                default: return 0;
            }
        }

        /// <summary>Stacking caps per stat (whatever the combination of engravings).</summary>
        public static double EngravingCap(string type) => type == "spd" ? 0.06 : type == "crit" ? 0.05 : 0.12;

        public static double EngravingTotal(IEnumerable<EngravingInfo> list, string type) =>
            Math.Min(EngravingCap(type), list.Where(e => e.Type == type).Sum(e => EngravingValue(e.Type, e.Level)));

        /// <summary>Slot n (1..3) unlocks at card level 10 / 30 / breakthrough 1.</summary>
        public static bool SlotUnlocked(int slot, int level, int breakthrough) =>
            slot == 1 ? level >= 10 : slot == 2 ? level >= 30 : slot == 3 && breakthrough >= 1;

        /// <returns>null when the set is legal, otherwise an error code.</returns>
        public static string? ValidateEngravings(IList<EngravingInfo> list)
        {
            if (list.Count > MaxEngravingSlots) return "engraving_slots_full";
            if (list.Any(e => !EngravingTypes.Contains(e.Type))) return "engraving_unknown_type";
            if (list.Any(e => e.Level < 1 || e.Level > MaxEngravingLevel)) return "engraving_invalid_level";
            if (list.GroupBy(e => e.Type).Any(g => g.Count() > MaxSameEngravingType)) return "engraving_type_limit";
            return null;
        }

        public static UpgradeCost EngravingCraftCost() => new UpgradeCost { Beans = 500, Items = { [EngravingDust] = 10 } };

        public static UpgradeCost? EngravingUpgradeCost(int level)
        {
            if (level < 1 || level >= MaxEngravingLevel) return null;
            int[] dust = { 0, 10, 20, 35, 60 };
            long[] beans = { 0, 800, 1500, 3000, 5000 };
            return new UpgradeCost { Beans = beans[level], Items = { [EngravingDust] = dust[level] } };
        }

        // ------------------------------------------------------------------ stats

        /// <summary>Stat multiplier from stars, enhancement and awakening (everything except level/rarity/quality and engravings).</summary>
        public static double GrowthMultiplier(CardProgress p) =>
            StarMultiplier(p.Stars) * (1 + EnhanceBonus(p.Enhance)) * (1 + (p.Awakened ? AwakenStatBonus : 0));

        public static CardStats Compute(GameCatalog cat, string formId, string rarity, string quality, CardProgress p)
        {
            var f = cat.Form(formId);
            int level = Clamp(p.Level, 1, LevelCap(p.Breakthrough));
            double baseM = (1 + StatCalculator.PerLevel * (level - 1)) * cat.Rarity(rarity).StatMultiplier * cat.Quality(quality).StatMultiplier;
            double g = GrowthMultiplier(p);
            var e = p.Engravings ?? new List<EngravingInfo>();
            return new CardStats
            {
                Hp = (int)Math.Round(f.Stats.Hp * baseM * g * (1 + EngravingTotal(e, "hp"))),
                Atk = (int)Math.Round(f.Stats.Atk * baseM * g * (1 + EngravingTotal(e, "atk"))),
                Def = (int)Math.Round(f.Stats.Def * baseM * g * (1 + EngravingTotal(e, "def"))),
                // speed stays driven by species identity: only level (slowly) and spd engravings (≤ +6 %)
                Spd = (int)Math.Round(f.Stats.Spd * (1 + 0.01 * (level - 1)) * (1 + EngravingTotal(e, "spd"))),
                Crit = Math.Min(0.5, f.Stats.Crit + EngravingTotal(e, "crit")),
            };
        }

        /// <summary>Upper bound of the progression multiplier on HP/ATK/DEF (balance guard used by tests).</summary>
        public static double MaxGrowthMultiplier =>
            StarMultiplier(MaxStars) * (1 + EnhanceBonus(MaxEnhance)) * (1 + AwakenStatBonus) * (1 + EngravingCap("atk"));

        private static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;
    }
}
