using System;
using System.Collections.Generic;
using System.Linq;

namespace PetStar.Rules
{
    /// <summary>Static game-design data (pets, skills, elements, rarities, packs). Loaded from database/seed JSON
    /// by the backend; the server version is authoritative.</summary>
    public sealed class GameCatalog
    {
        public ElementChart Elements { get; set; } = new ElementChart();
        public List<RarityDef> Rarities { get; set; } = new List<RarityDef>();
        public List<QualityDef> Qualities { get; set; } = new List<QualityDef>();
        public List<string> Finishes { get; set; } = new List<string>();
        public List<PetFamilyDef> Families { get; set; } = new List<PetFamilyDef>();
        public Dictionary<string, SkillDef> Skills { get; set; } = new Dictionary<string, SkillDef>();
        public Dictionary<string, PassiveDef> Passives { get; set; } = new Dictionary<string, PassiveDef>();
        public List<PackTypeDef> PackTypes { get; set; } = new List<PackTypeDef>();
        public EvolutionRuleSet EvolutionRules { get; set; } = new EvolutionRuleSet();
        public DeckRuleSet DeckRules { get; set; } = new DeckRuleSet();

        private Dictionary<string, PetFormDef>? _forms;

        public IReadOnlyDictionary<string, PetFormDef> Forms
        {
            get
            {
                if (_forms == null)
                {
                    // build fully, then publish: concurrent readers never see a half-filled dictionary
                    var forms = new Dictionary<string, PetFormDef>();
                    foreach (var fam in Families)
                        foreach (var f in fam.Forms)
                        {
                            f.FamilyId = fam.Id;
                            f.Element = fam.Element;
                            f.Role = fam.Role;
                            f.FamilyStages = fam.Stages;
                            f.DivineSet = fam.Divine;
                            forms[f.Id] = f;
                        }
                    System.Threading.Interlocked.CompareExchange(ref _forms, forms, null);
                }
                return _forms;
            }
        }

        public PetFormDef Form(string id) =>
            Forms.TryGetValue(id, out var f) ? f : throw new KeyNotFoundException("Unknown pet form " + id);

        public RarityDef Rarity(string id) => Rarities.First(r => r.Id == id);
        public QualityDef Quality(string id) => Qualities.First(q => q.Id == id);
        public PackTypeDef? PackType(string id) => PackTypes.FirstOrDefault(p => p.Id == id);

        /// <summary>Next form in the family line, or null if final / standalone.</summary>
        public PetFormDef? NextForm(PetFormDef form)
        {
            var fam = Families.First(x => x.Id == form.FamilyId);
            return fam.Forms.FirstOrDefault(f => f.Stage == form.Stage + 1);
        }

        /// <summary>Validates referential integrity; returns a list of problems (empty when valid).</summary>
        public List<string> Validate()
        {
            var problems = new List<string>();
            var ids = new HashSet<string>();
            foreach (var fam in Families)
            {
                if (fam.Forms.Count != fam.Stages) problems.Add($"{fam.Id}: stages={fam.Stages} but {fam.Forms.Count} forms");
                if (!Elements.Elements.Any(e => e.Id == fam.Element)) problems.Add($"{fam.Id}: unknown element {fam.Element}");
                for (int i = 0; i < fam.Forms.Count; i++)
                {
                    var f = fam.Forms[i];
                    if (!ids.Add(f.Id)) problems.Add($"duplicate form id {f.Id}");
                    if (f.Stage != i + 1) problems.Add($"{f.Id}: stage {f.Stage} out of order");
                    foreach (var s in new[] { f.Skill, f.Ultimate })
                        if (s != null && !Skills.ContainsKey(s)) problems.Add($"{f.Id}: unknown skill {s}");
                    if (f.Passive != null && !Passives.ContainsKey(f.Passive)) problems.Add($"{f.Id}: unknown passive {f.Passive}");
                }
            }
            var rarityIds = new HashSet<string>(Rarities.Select(r => r.Id));
            foreach (var p in PackTypes)
            {
                var tables = new List<Dictionary<string, int>> { p.SlotTable, p.QualityTable, p.FinishTable };
                if (p.FinalSlotTable.Count > 0) tables.Add(p.FinalSlotTable);
                foreach (var t in tables)
                {
                    if (t.Values.Sum(v => (long)v) != WeightedTable.Total) problems.Add($"{p.Id}: table does not sum to {WeightedTable.Total}");
                    if (t.Values.Any(v => v < 0)) problems.Add($"{p.Id}: negative weight");
                }
                foreach (var k in p.SlotTable.Keys.Concat(p.FinalSlotTable.Keys))
                    if (!rarityIds.Contains(k)) problems.Add($"{p.Id}: unknown rarity {k}");
                if (p.CardsPerPack != 5) problems.Add($"{p.Id}: cardsPerPack must be 5");
                bool divineWeight = p.SlotTable.TryGetValue(RarityDef.Divine, out var dw) && dw > 0;
                if (divineWeight != (p.DivinePool.Count > 0)) problems.Add($"{p.Id}: divine weight and divine pool must come together");
                foreach (var id in p.DivinePool)
                    if (!Forms.TryGetValue(id, out var df) || df.DivineSet == null) problems.Add($"{p.Id}: {id} is not a divine pet");
            }
            foreach (var fam in Families.Where(x => x.Divine != null))
                if (fam.Stages != 1) problems.Add($"{fam.Id}: divine pets are single-stage");
            return problems;
        }
    }

    public sealed class ElementDef
    {
        public string Id { get; set; } = "";
        public string Color { get; set; } = "";
        public List<string> StrongAgainst { get; set; } = new List<string>();
    }

    public sealed class ElementChart
    {
        public double StrongMultiplier { get; set; } = 1.5;
        public double WeakMultiplier { get; set; } = 0.75;
        public List<ElementDef> Elements { get; set; } = new List<ElementDef>();

        public double Multiplier(string attacker, string defender)
        {
            var a = Elements.FirstOrDefault(e => e.Id == attacker);
            var d = Elements.FirstOrDefault(e => e.Id == defender);
            if (a == null || d == null) return 1.0;
            bool aStrong = a.StrongAgainst.Contains(defender);
            bool dStrong = d.StrongAgainst.Contains(attacker);
            if (aStrong) return StrongMultiplier;      // includes mutual (light/dark)
            if (dStrong) return WeakMultiplier;
            return 1.0;
        }
    }

    public sealed class RarityDef
    {
        /// <summary>Rarity tier of the 15 Divine Pet Cards (Thần Pet). Only obtainable from their divine pool.</summary>
        public const string Divine = "DV";
        public string Id { get; set; } = "";
        public int Order { get; set; }
        public string Color { get; set; } = "";
        public double StatMultiplier { get; set; } = 1.0;
    }

    public sealed class QualityDef
    {
        public string Id { get; set; } = "";
        public int Order { get; set; }
        public double StatMultiplier { get; set; } = 1.0;
    }

    public sealed class BaseStats
    {
        public int Hp { get; set; }
        public int Atk { get; set; }
        public int Def { get; set; }
        public int Spd { get; set; }
        public double Crit { get; set; }
    }

    public sealed class PetFamilyDef
    {
        public string Id { get; set; } = "";
        public string Element { get; set; } = "";
        public string Role { get; set; } = "";
        public int Stages { get; set; }
        public string Identity { get; set; } = "";
        /// <summary>premium | limited | star for the Divine Pet Cards (Thần Pet), null for regular pets.</summary>
        public string? Divine { get; set; }
        public List<PetFormDef> Forms { get; set; } = new List<PetFormDef>();
    }

    public sealed class PetFormDef
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public int Stage { get; set; }
        public BaseStats Stats { get; set; } = new BaseStats();
        public string? Skill { get; set; }
        public string? Ultimate { get; set; }
        public string? Passive { get; set; }
        public string Art { get; set; } = "";
        public Dictionary<string, string> Lore { get; set; } = new Dictionary<string, string>();
        /// <summary>Localised names (vi/en) when they differ from Name (Divine Pets).</summary>
        public Dictionary<string, string> Names { get; set; } = new Dictionary<string, string>();

        // denormalised from family by GameCatalog.Forms
        public string FamilyId { get; set; } = "";
        public string Element { get; set; } = "";
        public string Role { get; set; } = "";
        public int FamilyStages { get; set; }
        public string? DivineSet { get; set; }
    }

    public sealed class SkillEffect
    {
        public string Type { get; set; } = "";          // damage | heal | shield | status | cleanse | energy
        public double Power { get; set; }
        public string Scale { get; set; } = "atk";      // atk | def | maxhp
        public string? Status { get; set; }             // burn | poison | stun | taunt | weaken
        public double Chance { get; set; } = 1.0;
        public int Duration { get; set; }
        public string? TargetOverride { get; set; }
    }

    public sealed class SkillDef
    {
        public string Id { get; set; } = "";
        public string Kind { get; set; } = "skill";     // skill | ultimate
        public int Cost { get; set; }
        public int Priority { get; set; }
        public string Target { get; set; } = "enemy_single";
        public List<SkillEffect> Effects { get; set; } = new List<SkillEffect>();
    }

    public sealed class PassiveDef
    {
        public string Id { get; set; } = "";
        public string Type { get; set; } = "";
        public string? Stat { get; set; }
        public double Pct { get; set; }
        public double Threshold { get; set; }
        public int Amount { get; set; }
    }

    public sealed class PackPrice
    {
        public string Currency { get; set; } = Rules.Currency.MiniGreenBean;
        public int Amount { get; set; }
    }

    public static class Currency
    {
        public const string RainbowStar = "RAINBOW_STAR";
        public const string MiniGreenBean = "MINI_GREEN_BEAN";
        public static readonly string[] All = { RainbowStar, MiniGreenBean };
    }

    public sealed class PackPool
    {
        public List<int> Stages { get; set; } = new List<int> { 1 };
        public bool IncludeStandalone { get; set; } = true;
    }

    public sealed class PackTypeDef
    {
        public string Id { get; set; } = "";
        public string NameKey { get; set; } = "";
        public string ArtAssetId { get; set; } = "";
        public PackPrice Price { get; set; } = new PackPrice();
        public int CardsPerPack { get; set; } = 5;
        public bool Enabled { get; set; }
        public DateTimeOffset? AvailableFrom { get; set; }
        public DateTimeOffset? AvailableUntil { get; set; }
        public PackPool Pool { get; set; } = new PackPool();
        public Dictionary<string, int> SlotTable { get; set; } = new Dictionary<string, int>();
        public Dictionary<string, int> FinalSlotTable { get; set; } = new Dictionary<string, int>();
        public Dictionary<string, int> QualityTable { get; set; } = new Dictionary<string, int>();
        public Dictionary<string, int> FinishTable { get; set; } = new Dictionary<string, int>();
        /// <summary>Divine Pet form ids rolled when a slot lands on rarity DV (uniform within the group).</summary>
        public List<string> DivinePool { get; set; } = new List<string>();
        public PackRequirements Requirements { get; set; } = new PackRequirements();
        public Dictionary<string, string> Names { get; set; } = new Dictionary<string, string>();
        public Dictionary<string, string> Descriptions { get; set; } = new Dictionary<string, string>();
        /// <summary>Display order in the shop.</summary>
        public int Order { get; set; }
        /// <summary>Visual theme of the opening animation (standard | advanced | transcendent | epic | legendary | mythic | premium | limited | star).</summary>
        public string Theme { get; set; } = "standard";
    }

    /// <summary>Purchase conditions, all checked by the server at purchase time.</summary>
    public sealed class PackRequirements
    {
        public int MinLevel { get; set; }
        public int MinVip { get; set; }
        /// <summary>Only purchasable while an admin-scheduled limited event for this pack is running.</summary>
        public bool RequiresEvent { get; set; }
        /// <summary>Needs a valid, unexpired, unrevoked invitation issued by an authorised admin.</summary>
        public bool RequiresInvitation { get; set; }
    }

    public sealed class EvolutionStep
    {
        public int MinLevel { get; set; }
        public int EssenceAmount { get; set; }
        public int Beans { get; set; }
    }

    public sealed class EvolutionRuleSet
    {
        public EvolutionStep Stage1To2 { get; set; } = new EvolutionStep { MinLevel = 15, EssenceAmount = 5, Beans = 500 };
        public EvolutionStep Stage2To3 { get; set; } = new EvolutionStep { MinLevel = 30, EssenceAmount = 15, Beans = 2000 };

        public EvolutionStep ForStage(int fromStage) => fromStage == 1 ? Stage1To2 : Stage2To3;
    }

    public sealed class DeckRuleSet
    {
        public int Size { get; set; } = 5;
        public int Frontline { get; set; } = 3;
        /// <summary>Max cards of the same evolution family in one deck (assumption, configurable).</summary>
        public int MaxSameFamily { get; set; } = 2;
    }
}
