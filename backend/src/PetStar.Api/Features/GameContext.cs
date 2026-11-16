using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PetStar.Api.Data;
using PetStar.Api.Infrastructure;
using PetStar.Contracts;
using PetStar.Rules;

namespace PetStar.Api.Features;

/// <summary>Holds the in-memory design catalog and helpers shared by gameplay features.</summary>
public sealed class GameData(GameCatalog catalog, EconomyConfig economy, PveConfig pve, ValuationConfig valuation, MarketConfig market, AchievementCatalog achievements)
{
    public GameCatalog Catalog { get; } = catalog;
    public EconomyConfig Economy { get; } = economy;
    public PveConfig Pve { get; } = pve;
    public ValuationConfig Valuation { get; } = valuation;
    public MarketConfig Market { get; } = market;
    public AchievementCatalog Achievements { get; } = achievements;

    private readonly ConcurrentDictionary<(string, int), PackTypeDef> _packCache = new();

    /// <summary>Pack config comes from the DB row (versioned, admin-editable through approval), not the seed file.</summary>
    public async Task<(PackTypeDef def, int version)> Pack(PetStarDb db, string id)
    {
        var row = await db.PackTypes.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id) ?? throw ApiException.NotFound("pack_not_found");
        var def = _packCache.GetOrAdd((id, row.Version), _ => JsonSerializer.Deserialize<PackTypeDef>(row.DataJson, IdempotencyService.Json)!);
        return (def, row.Version);
    }

    /// <summary>Card DTO with server-computed stats from the stored progression (stars, enhancement, breakthrough,
    /// awakening, skill level and — when given — equipped engravings).</summary>
    public CardDto ToDto(CardInstance c, int slot = 0, IReadOnlyList<CardEngraving>? engravings = null)
    {
        var f = Catalog.Form(c.FormId);
        var progress = CardProgressLoader.Progress(c, engravings);
        var s = Progression.Compute(Catalog, c.FormId, c.Rarity, c.Quality, progress);
        int cap = Progression.LevelCap(c.Breakthrough);
        return new CardDto
        {
            Id = c.Id, FormId = c.FormId, Name = f.Name, FamilyId = f.FamilyId, Stage = f.Stage, Element = f.Element, Role = f.Role,
            Rarity = c.Rarity, Quality = c.Quality, Finish = c.Finish, Level = c.Level, Exp = c.Exp,
            ExpToNext = c.Level >= cap ? 0 : StatCalculator.ExpToNextUncapped(c.Level),
            Hp = s.Hp, Atk = s.Atk, Def = s.Def, Spd = s.Spd, Crit = s.Crit, Power = s.Power,
            SkillId = f.Skill, UltimateId = f.Ultimate, PassiveId = f.Passive, Serial = c.Serial, SkillLevel = c.SkillLevel,
            IsFavorite = c.IsFavorite, IsLocked = c.IsLocked, IsBusy = c.BusyState != null, Slot = slot,
            Stars = c.Stars, EnhanceLevel = c.EnhanceLevel, Breakthrough = c.Breakthrough, LevelCap = cap, Awakened = c.Awakened,
            DivineSet = f.DivineSet,
            Engravings = engravings?.Select(CardProgressLoader.ToDto).ToList() ?? new List<EngravingDto>(),
        };
    }

    /// <summary>DTOs for many cards with their engravings loaded in one query.</summary>
    public async Task<List<CardDto>> ToDtos(PetStarDb db, IReadOnlyList<CardInstance> cards)
    {
        var engr = await CardProgressLoader.Engravings(db, cards.Select(c => c.Id));
        return cards.Select(c => ToDto(c, 0, engr.GetValueOrDefault(c.Id))).ToList();
    }

    public async Task<CardDto> ToDtoFull(PetStarDb db, CardInstance c, int slot = 0) =>
        ToDto(c, slot, (await CardProgressLoader.Engravings(db, new[] { c.Id })).GetValueOrDefault(c.Id));

    public static async Task DiscoverCodex(PetStarDb db, long membershipId, string formId, long cardId) =>
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO codex_entries (membership_id, form_id, first_card_id, discovered_at) VALUES ({0}, {1}, {2}, now()) ON CONFLICT DO NOTHING",
            membershipId, formId, cardId);
}
