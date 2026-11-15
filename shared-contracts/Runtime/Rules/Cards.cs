using System;
using System.Collections.Generic;
using System.Linq;

namespace PetStar.Rules
{
    public sealed class CardStats
    {
        public int Hp, Atk, Def, Spd;
        public double Crit;
        public int Power => (int)Math.Round(Hp * 0.25 + Atk * 2.0 + Def * 1.5 + Spd * 1.0);
    }

    public static class StatCalculator
    {
        public const int MaxLevel = 50;
        public const double PerLevel = 0.04;

        public static CardStats Compute(GameCatalog cat, string formId, int level, string rarity, string quality)
        {
            var f = cat.Form(formId);
            level = Math.Max(1, Math.Min(MaxLevel, level));
            double m = (1 + PerLevel * (level - 1)) * cat.Rarity(rarity).StatMultiplier * cat.Quality(quality).StatMultiplier;
            return new CardStats
            {
                Hp = (int)Math.Round(f.Stats.Hp * m),
                Atk = (int)Math.Round(f.Stats.Atk * m),
                Def = (int)Math.Round(f.Stats.Def * m),
                // speed scales slowly with level so turn order is driven by species identity
                Spd = (int)Math.Round(f.Stats.Spd * (1 + 0.01 * (level - 1))),
                Crit = f.Stats.Crit,
            };
        }

        /// <summary>EXP needed to go from level to level+1.</summary>
        public static int ExpToNext(int level) => level >= MaxLevel ? int.MaxValue : 50 + 25 * level;

        /// <summary>Applies EXP, returns (newLevel, newExp).</summary>
        public static (int level, int exp) AddExp(int level, int exp, int gained)
        {
            exp += Math.Max(0, gained);
            while (level < MaxLevel && exp >= ExpToNext(level)) { exp -= ExpToNext(level); level++; }
            if (level >= MaxLevel) exp = 0;
            return (level, exp);
        }
    }

    public sealed class RolledCard
    {
        public int Slot;
        public string FormId = "";
        public string Rarity = "";
        public string Quality = "";
        public string Finish = "";
    }

    public static class PackRoller
    {
        public static List<PetFormDef> Pool(GameCatalog cat, PackTypeDef pack) =>
            cat.Forms.Values
                .Where(f => f.FamilyStages == 1 ? pack.Pool.IncludeStandalone : pack.Pool.Stages.Contains(f.Stage))
                .OrderBy(f => f.Id, StringComparer.Ordinal)
                .ToList();

        /// <summary>Rolls exactly CardsPerPack cards. The final slot uses its own rarity table.</summary>
        public static List<RolledCard> Roll(GameCatalog cat, PackTypeDef pack, IRandomSource rng)
        {
            var pool = Pool(cat, pack);
            if (pool.Count == 0) throw new InvalidOperationException("Pack pool is empty: " + pack.Id);
            var rarityOrder = cat.Rarities.OrderBy(r => r.Order).Select(r => r.Id).ToList();
            var qualityOrder = cat.Qualities.OrderBy(q => q.Order).Select(q => q.Id).ToList();
            var result = new List<RolledCard>(pack.CardsPerPack);
            for (int slot = 1; slot <= pack.CardsPerPack; slot++)
            {
                var table = slot == pack.CardsPerPack ? pack.FinalSlotTable : pack.SlotTable;
                result.Add(new RolledCard
                {
                    Slot = slot,
                    Rarity = WeightedTable.Roll(table, rng, rarityOrder),
                    FormId = pool[rng.Next(pool.Count)].Id,
                    Quality = WeightedTable.Roll(pack.QualityTable, rng, qualityOrder),
                    Finish = WeightedTable.Roll(pack.FinishTable, rng, cat.Finishes),
                });
            }
            return result;
        }
    }

    public enum EvolutionCheck { Ok, FinalForm, LevelTooLow, NotEnoughEssence, NotEnoughBeans, CardLocked }

    public static class EvolutionRules
    {
        public static string EssenceItemId(string element) => "essence_" + element;

        public static EvolutionCheck Check(GameCatalog cat, string formId, int level, long essenceOwned, long beans, bool locked)
        {
            var form = cat.Form(formId);
            if (cat.NextForm(form) == null) return EvolutionCheck.FinalForm;
            if (locked) return EvolutionCheck.CardLocked;
            var step = cat.EvolutionRules.ForStage(form.Stage);
            if (level < step.MinLevel) return EvolutionCheck.LevelTooLow;
            if (essenceOwned < step.EssenceAmount) return EvolutionCheck.NotEnoughEssence;
            if (beans < step.Beans) return EvolutionCheck.NotEnoughBeans;
            return EvolutionCheck.Ok;
        }
    }

    public sealed class DeckCardInfo
    {
        public long CardId;
        public string FormId = "";
        public int Position; // 0..2 frontline, 3..4 backline
    }

    public static class DeckRules
    {
        /// <returns>null when valid, otherwise an error code.</returns>
        public static string? Validate(GameCatalog cat, IList<DeckCardInfo> cards)
        {
            var r = cat.DeckRules;
            if (cards.Count != r.Size) return "deck_wrong_size";
            if (cards.Select(c => c.CardId).Distinct().Count() != cards.Count) return "deck_duplicate_card";
            if (cards.Select(c => c.Position).Distinct().Count() != cards.Count || cards.Any(c => c.Position < 0 || c.Position >= r.Size))
                return "deck_invalid_positions";
            var fams = cards.GroupBy(c => cat.Form(c.FormId).FamilyId);
            if (fams.Any(g => g.Count() > r.MaxSameFamily)) return "deck_too_many_same_family";
            return null;
        }
    }
}
