using System.Text.Json;
using System.Text.Json.Nodes;
using PetStar.Rules;

namespace PetStar.Api.Infrastructure;

public sealed class EconomyConfig
{
    public Dictionary<string, long> StarterCurrencies { get; init; } = new();
    public Dictionary<string, int> StarterPacks { get; init; } = new();
    public List<IapProduct> IapProducts { get; init; } = new();
    /// <summary>VIP thresholds (points for level 0..10). Points = Rainbow Stars spent on system purchases.</summary>
    public List<long> VipThresholds { get; init; } = new() { 0 };
}

public sealed record IapProduct(string ProductId, string Currency, long Amount, long Bonus, string AssetId);

public sealed class PveStage
{
    public string Id { get; set; } = "";
    public int Chapter { get; set; }
    public int Order { get; set; }
    public int Difficulty { get; set; }
    public List<PveEnemy> Enemies { get; set; } = new();
    public PveRewards Rewards { get; set; } = new();
    public PveRewards FirstClearRewards { get; set; } = new();
    public string? RequiresStage { get; set; }
}

public sealed class PveEnemy
{
    public string FormId { get; set; } = "";
    public int Level { get; set; }
    public string Rarity { get; set; } = "N";
    public string Quality { get; set; } = "B";
    public int Position { get; set; }
}

public sealed class PveRewards
{
    public Dictionary<string, long> Currencies { get; set; } = new();
    public Dictionary<string, long> Items { get; set; } = new();
    public Dictionary<string, int> Packs { get; set; } = new();
    public int CardExp { get; set; }
}

public sealed class PveConfig
{
    public int DailyRewardedClearsPerStage { get; set; } = 10;
    public List<PveStage> Stages { get; set; } = new();
}

/// <summary>Loads design data from database/seed/*.json (copied to the output folder as seed/).</summary>
public static class CatalogLoader
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public static string SeedDir(string contentRoot)
    {
        foreach (var candidate in new[]
                 {
                     Path.Combine(AppContext.BaseDirectory, "seed"),
                     Path.Combine(contentRoot, "seed"),
                     Path.GetFullPath(Path.Combine(contentRoot, "../../../database/seed")),
                 })
            if (File.Exists(Path.Combine(candidate, "pets.json"))) return candidate;
        throw new FileNotFoundException("Seed data folder not found");
    }

    public static GameCatalog LoadCatalog(string dir)
    {
        JsonNode Read(string f) => JsonNode.Parse(File.ReadAllText(Path.Combine(dir, f)))!;
        var cat = new GameCatalog();

        var el = Read("elements.json");
        cat.Elements = new ElementChart
        {
            StrongMultiplier = el["strongMultiplier"]!.GetValue<double>(),
            WeakMultiplier = el["weakMultiplier"]!.GetValue<double>(),
            Elements = el["elements"].Deserialize<List<ElementDef>>(Json)!,
        };

        var rar = Read("rarities.json");
        cat.Rarities = rar["rarities"].Deserialize<List<RarityDef>>(Json)!;
        cat.Qualities = rar["qualities"].Deserialize<List<QualityDef>>(Json)!;
        cat.Finishes = rar["finishes"]!.AsArray().OrderBy(f => f!["order"]!.GetValue<int>()).Select(f => f!["id"]!.GetValue<string>()).ToList();

        var skills = Read("skills.json");
        cat.Skills = skills["skills"].Deserialize<List<SkillDef>>(Json)!.ToDictionary(s => s.Id);
        cat.Passives = skills["passives"].Deserialize<List<PassiveDef>>(Json)!.ToDictionary(s => s.Id);

        var pets = Read("pets.json");
        cat.Families = pets["families"].Deserialize<List<PetFamilyDef>>(Json)!;
        var evo = pets["evolutionRules"]!;
        cat.EvolutionRules = new EvolutionRuleSet
        {
            Stage1To2 = evo["1to2"].Deserialize<EvolutionStep>(Json)!,
            Stage2To3 = evo["2to3"].Deserialize<EvolutionStep>(Json)!,
        };

        cat.PackTypes = Read("packs.json")["packTypes"].Deserialize<List<PackTypeDef>>(Json)!;

        var problems = cat.Validate();
        if (problems.Count > 0) throw new InvalidDataException("Catalog invalid: " + string.Join("; ", problems));
        return cat;
    }

    public static EconomyConfig LoadEconomy(string dir)
    {
        var e = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "economy.json")))!;
        var starter = e["starterGrant"]!.AsObject();
        return new EconomyConfig
        {
            StarterCurrencies = Currency.All.ToDictionary(c => c, c => starter[c]?.GetValue<long>() ?? 0),
            StarterPacks = starter["packs"].Deserialize<Dictionary<string, int>>(Json)!,
            IapProducts = e["iapProducts"].Deserialize<List<IapProduct>>(Json)!,
            VipThresholds = e["vip"]?["thresholds"].Deserialize<List<long>>(Json) ?? new List<long> { 0 },
        };
    }

    public static ValuationConfig LoadValuation(string dir) =>
        new() { Raw = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "valuation.json")))!.AsObject() };

    public static MarketConfig LoadMarket(string dir) =>
        new() { Raw = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "market.json")))!.AsObject() };

    public static AchievementCatalog LoadAchievements(string dir) =>
        new() { Items = JsonSerializer.Deserialize<List<AchievementDef>>(JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "achievements.json")))!["achievements"]!.ToJsonString(), Json)! };

    /// <summary>Duel rules (database/seed/duel_cards.json): configuration, Spell/Trap definitions, starter collection.</summary>
    public static DuelData LoadDuel(string dir)
    {
        var j = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "duel_cards.json")))!;
        return new DuelData
        {
            Config = j["config"].Deserialize<PetStar.Rules.Duel.DuelConfig>(Json)!,
            Tactics = j["cards"].Deserialize<List<PetStar.Rules.Duel.TacticDef>>(Json)!,
            Starter = j["starter"]!["cards"].Deserialize<Dictionary<string, int>>(Json)!,
        };
    }

    /// <summary>Pet Battle Arena card definitions + config (database/seed/arena_cards.json, built by tools/build_arena_cards.py).</summary>
    public static PetStar.Rules.Arena.ArenaRules LoadArena(string dir) =>
        JsonSerializer.Deserialize<PetStar.Rules.Arena.ArenaRules>(File.ReadAllText(Path.Combine(dir, "arena_cards.json")), Json)!;

    /// <summary>Battle 3v3 data (database/seed/team_battle.json, built by tools/build_team_battle.py).</summary>
    public static PetStar.Rules.Team.TeamRules LoadTeam(string dir) =>
        JsonSerializer.Deserialize<PetStar.Rules.Team.TeamRules>(File.ReadAllText(Path.Combine(dir, "team_battle.json")), Json)!;

    public static PveConfig LoadPve(string dir) =>
        JsonSerializer.Deserialize<PveConfig>(File.ReadAllText(Path.Combine(dir, "pve_stages.json")), Json)!;
}

public sealed class DuelData
{
    public PetStar.Rules.Duel.DuelConfig Config { get; init; } = new();
    public List<PetStar.Rules.Duel.TacticDef> Tactics { get; init; } = new();
    public Dictionary<string, int> Starter { get; init; } = new();
}

public sealed class MarketConfig
{
    public required JsonObject Raw { get; init; }
    public int FeeBps => Raw["feeBps"]!.GetValue<int>();
    public int AuctionFeeBps => Raw["auctionFeeBps"]!.GetValue<int>();
    public long L(string section, string key) => Raw[section]![key]!.GetValue<long>();
    public double D(string section, string key) => Raw[section]![key]!.GetValue<double>();
}

public sealed class AchievementDef
{
    public string Id { get; set; } = "";
    public string Trigger { get; set; } = "";
    public int Threshold { get; set; } = 1;
    public string? Element { get; set; }
    public Dictionary<string, long> Currencies { get; set; } = new();
    public Dictionary<string, int> Packs { get; set; } = new();
    public bool Hidden { get; set; }
}

public sealed class AchievementCatalog
{
    public List<AchievementDef> Items { get; init; } = new();
}
