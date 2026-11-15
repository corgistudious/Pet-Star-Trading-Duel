using System;
using System.Collections.Generic;
using PetStar.Rules.Battle;

// REST API contracts shared by backend and Unity client. JSON uses camelCase.
namespace PetStar.Contracts
{
    // ---------------- Auth ----------------
    public class RegisterRequest { public string Email { get; set; } = ""; public string Password { get; set; } = ""; public string DisplayName { get; set; } = ""; public string Language { get; set; } = "en"; }
    public class LoginRequest { public string Email { get; set; } = ""; public string Password { get; set; } = ""; public string? Platform { get; set; } public string? DeviceName { get; set; } }
    public class RefreshRequest { public string RefreshToken { get; set; } = ""; }
    public class TokenRequest { public string Token { get; set; } = ""; }
    public class EmailRequest { public string Email { get; set; } = ""; }
    public class ResetPasswordRequest { public string Token { get; set; } = ""; public string NewPassword { get; set; } = ""; }

    public class AuthResponse
    {
        public long Uid { get; set; }
        public string AccessToken { get; set; } = "";
        public DateTimeOffset AccessTokenExpiresAt { get; set; }
        public string RefreshToken { get; set; } = "";
        public bool EmailVerified { get; set; }
        public int? ServerId { get; set; }
    }

    public class SessionDto { public Guid Id { get; set; } public string? Platform { get; set; } public string? DeviceName { get; set; } public DateTimeOffset CreatedAt { get; set; } public DateTimeOffset LastUsedAt { get; set; } public bool Current { get; set; } }

    public class ProfileDto
    {
        public long Uid { get; set; }
        public string Email { get; set; } = "";
        public bool EmailVerified { get; set; }
        public string DisplayName { get; set; } = "";
        public string Language { get; set; } = "en";
        public string AvatarId { get; set; } = "";
        public string AvatarFrameId { get; set; } = "";
        public string? TitleId { get; set; }
        public List<string> Roles { get; set; } = new List<string>();
    }

    public class UpdateProfileRequest { public string? DisplayName { get; set; } public string? Language { get; set; } public string? AvatarId { get; set; } public string? AvatarFrameId { get; set; } public string? TitleId { get; set; } }

    // ---------------- Servers ----------------
    public class ServerDto
    {
        public int Id { get; set; }
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public string Status { get; set; } = "";
        public bool RegistrationOpen { get; set; }
        public bool HasProgress { get; set; }
        public int? PlayerLevel { get; set; }
        public string? Nickname { get; set; }
    }

    public class EnterServerRequest { public string? Nickname { get; set; } }

    public class PlayerDto
    {
        public long Uid { get; set; }
        public int ServerId { get; set; }
        public string ServerCode { get; set; } = "";
        public string Nickname { get; set; } = "";
        public int Level { get; set; }
        public int Exp { get; set; }
        /// <summary>Server-authoritative balances keyed by currency id (RAINBOW_STAR, MINI_GREEN_BEAN).</summary>
        public Dictionary<string, long> Currencies { get; set; } = new Dictionary<string, long>();
        public Dictionary<string, int> Packs { get; set; } = new Dictionary<string, int>();
        public Dictionary<string, long> Items { get; set; } = new Dictionary<string, long>();
        public int CardCount { get; set; }
        public int CodexDiscovered { get; set; }
        public int CodexTotal { get; set; }
    }

    public class LedgerEntryDto { public long Id { get; set; } public string Currency { get; set; } = ""; public long Delta { get; set; } public long BalanceAfter { get; set; } public string Reason { get; set; } = ""; public DateTimeOffset At { get; set; } }

    // ---------------- Packs ----------------
    public class PackShopItemDto
    {
        public string Id { get; set; } = "";
        public string NameKey { get; set; } = "";
        public string ArtAssetId { get; set; } = "";
        public string PriceCurrency { get; set; } = "";
        public int PriceAmount { get; set; }
        public int Owned { get; set; }
        public DateTimeOffset? AvailableUntil { get; set; }
    }

    /// <summary>Public odds disclosure (percentages).</summary>
    public class PackOddsDto
    {
        public string PackTypeId { get; set; } = "";
        public int Version { get; set; }
        public Dictionary<string, double> Slots1To4 { get; set; } = new Dictionary<string, double>();
        public Dictionary<string, double> Slot5 { get; set; } = new Dictionary<string, double>();
        public Dictionary<string, double> Quality { get; set; } = new Dictionary<string, double>();
        public Dictionary<string, double> Finish { get; set; } = new Dictionary<string, double>();
        public List<string> PossiblePets { get; set; } = new List<string>();
    }

    public class PurchasePackRequest { public string PackTypeId { get; set; } = ""; public int Quantity { get; set; } = 1; }
    public class PurchasePackResponse { public string PackTypeId { get; set; } = ""; public int Owned { get; set; } public Dictionary<string, long> Currencies { get; set; } = new Dictionary<string, long>(); }
    public class OpenPackRequest { public string PackTypeId { get; set; } = ""; }

    public class PackOpeningDto
    {
        public Guid OpeningId { get; set; }
        public string PackTypeId { get; set; } = "";
        public DateTimeOffset CreatedAt { get; set; }
        public bool Acknowledged { get; set; }
        public List<CardDto> Cards { get; set; } = new List<CardDto>();
    }

    // ---------------- Cards ----------------
    public class CardDto
    {
        public long Id { get; set; }
        /// <summary>12-char collectible serial (display only; never an ownership credential).</summary>
        public string Serial { get; set; } = "";
        public int SkillLevel { get; set; } = 1;
        public string FormId { get; set; } = "";
        public string Name { get; set; } = "";
        public string FamilyId { get; set; } = "";
        public int Stage { get; set; }
        public string Element { get; set; } = "";
        public string Role { get; set; } = "";
        public string Rarity { get; set; } = "";
        public string Quality { get; set; } = "";
        public string Finish { get; set; } = "";
        public int Level { get; set; }
        public int Exp { get; set; }
        public int ExpToNext { get; set; }
        public int Hp { get; set; }
        public int Atk { get; set; }
        public int Def { get; set; }
        public int Spd { get; set; }
        public double Crit { get; set; }
        public int Power { get; set; }
        public string? SkillId { get; set; }
        public string? UltimateId { get; set; }
        public string? PassiveId { get; set; }
        public bool IsFavorite { get; set; }
        public bool IsLocked { get; set; }
        public bool IsBusy { get; set; }
        public int Slot { get; set; }
    }

    public class CardPageDto { public List<CardDto> Items { get; set; } = new List<CardDto>(); public int Total { get; set; } public int Page { get; set; } public int PageSize { get; set; } public Dictionary<string, int> CopiesByForm { get; set; } = new Dictionary<string, int>(); }
    public class FlagRequest { public bool Value { get; set; } }

    public class EvolutionPreviewDto
    {
        public CardDto Current { get; set; } = new CardDto();
        public CardDto? Next { get; set; }
        public string Check { get; set; } = "";
        public int RequiredLevel { get; set; }
        public string EssenceItemId { get; set; } = "";
        public int EssenceRequired { get; set; }
        public long EssenceOwned { get; set; }
        public int BeansRequired { get; set; }
        public long BeansOwned { get; set; }
    }

    public class CodexEntryDto { public string FormId { get; set; } = ""; public string FamilyId { get; set; } = ""; public int Stage { get; set; } public bool Discovered { get; set; } public DateTimeOffset? DiscoveredAt { get; set; } public int Owned { get; set; } }
    public class CodexDto { public int Discovered { get; set; } public int Total { get; set; } public double Completion { get; set; } public List<CodexEntryDto> Entries { get; set; } = new List<CodexEntryDto>(); }

    // ---------------- Decks ----------------
    public class DeckSlotDto { public int Position { get; set; } public long CardId { get; set; } }
    public class DeckDto { public long Id { get; set; } public string Name { get; set; } = ""; public bool IsDefault { get; set; } public List<DeckSlotDto> Slots { get; set; } = new List<DeckSlotDto>(); public int TotalPower { get; set; } }
    public class SaveDeckRequest { public string Name { get; set; } = ""; public List<DeckSlotDto> Slots { get; set; } = new List<DeckSlotDto>(); public bool IsDefault { get; set; } }

    // ---------------- Battles ----------------
    public class PveStageDto { public string Id { get; set; } = ""; public int Chapter { get; set; } public int Order { get; set; } public int Difficulty { get; set; } public bool Unlocked { get; set; } public int ClearCount { get; set; } public int RewardedClearsLeftToday { get; set; } public List<string> EnemyForms { get; set; } = new List<string>(); }
    public class StartPveRequest { public string StageId { get; set; } = ""; public long? DeckId { get; set; } }
    public class SubmitRoundRequest { public int Round { get; set; } public List<PlannedAction> Actions { get; set; } = new List<PlannedAction>(); }

    public class BattleUnitDto
    {
        public int Side { get; set; } public int Index { get; set; } public int Position { get; set; } public long CardId { get; set; }
        public string FormId { get; set; } = ""; public string Element { get; set; } = ""; public string Rarity { get; set; } = ""; public int Level { get; set; }
        public int MaxHp { get; set; } public int Hp { get; set; } public int Shield { get; set; } public int Atk { get; set; } public int Def { get; set; } public int Spd { get; set; }
        public string? SkillId { get; set; } public string? UltimateId { get; set; } public string? PassiveId { get; set; }
        public List<string> Statuses { get; set; } = new List<string>();
    }

    public class BattleDto
    {
        public Guid Id { get; set; }
        public string Mode { get; set; } = "";
        public string? StageId { get; set; }
        public int Round { get; set; }
        public int MyEnergy { get; set; }
        public int OpponentEnergy { get; set; }
        public int MySide { get; set; }
        public string Status { get; set; } = "";
        public int? WinnerSide { get; set; }
        public string? EndReason { get; set; }
        public DateTimeOffset? RoundDeadline { get; set; }
        public List<BattleUnitDto> Units { get; set; } = new List<BattleUnitDto>();
        public List<BattleEvent> LastEvents { get; set; } = new List<BattleEvent>();
        public BattleRewardsDto? Rewards { get; set; }
    }

    public class BattleRewardsDto { public Dictionary<string, long> Currencies { get; set; } = new Dictionary<string, long>(); public Dictionary<string, long> Items { get; set; } = new Dictionary<string, long>(); public Dictionary<string, int> Packs { get; set; } = new Dictionary<string, int>(); public int CardExp { get; set; } public bool FirstClear { get; set; } public bool DailyCapReached { get; set; } }

    // ---------------- IAP ----------------
    public class IapVerifyRequest { public string Platform { get; set; } = ""; public string ProductId { get; set; } = ""; public string PurchaseToken { get; set; } = ""; }
    public class IapVerifyResponse { public string Status { get; set; } = ""; public string ProductId { get; set; } = ""; public long Granted { get; set; } public Dictionary<string, long> Currencies { get; set; } = new Dictionary<string, long>(); }

    // ---------------- Admin ----------------
    public class AdminServerRequest { public int Id { get; set; } public string Code { get; set; } = ""; public string Name { get; set; } = "Global"; public string Status { get; set; } = "coming_soon"; public bool RegistrationOpen { get; set; } }
    public class AdminServerPatch { public string? Status { get; set; } public bool? RegistrationOpen { get; set; } public string? Name { get; set; } }
    public class AdminServerDto : ServerDto { public int Members { get; set; } public int ActiveLast24h { get; set; } public int Online { get; set; } }
}
