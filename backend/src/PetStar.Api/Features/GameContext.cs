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

    public CardDto ToDto(CardInstance c, int slot = 0)
    {
        var f = Catalog.Form(c.FormId);
        var s = StatCalculator.Compute(Catalog, c.FormId, c.Level, c.Rarity, c.Quality);
        return new CardDto
        {
            Id = c.Id, FormId = c.FormId, Name = f.Name, FamilyId = f.FamilyId, Stage = f.Stage, Element = f.Element, Role = f.Role,
            Rarity = c.Rarity, Quality = c.Quality, Finish = c.Finish, Level = c.Level, Exp = c.Exp,
            ExpToNext = c.Level >= StatCalculator.MaxLevel ? 0 : StatCalculator.ExpToNext(c.Level),
            Hp = s.Hp, Atk = s.Atk, Def = s.Def, Spd = s.Spd, Crit = s.Crit, Power = s.Power,
            SkillId = f.Skill, UltimateId = f.Ultimate, PassiveId = f.Passive, Serial = c.Serial, SkillLevel = c.SkillLevel,
            IsFavorite = c.IsFavorite, IsLocked = c.IsLocked, IsBusy = c.BusyState != null, Slot = slot,
        };
    }

    public static async Task DiscoverCodex(PetStarDb db, long membershipId, string formId, long cardId) =>
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO codex_entries (membership_id, form_id, first_card_id, discovered_at) VALUES ({0}, {1}, {2}, now()) ON CONFLICT DO NOTHING",
            membershipId, formId, cardId);
}
