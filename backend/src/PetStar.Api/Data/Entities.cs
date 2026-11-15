namespace PetStar.Api.Data;

// ---------------------------------------------------------------------------
// Global (cross-server) identity
// ---------------------------------------------------------------------------

public class Account
{
    /// <summary>Global player UID. Generated from sequence account_uid_seq starting at 100000. Never changes.</summary>
    public long Id { get; set; }
    public string Email { get; set; } = "";
    public string EmailNormalized { get; set; } = "";
    public bool EmailVerified { get; set; }
    public string PasswordHash { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Language { get; set; } = "en";
    public string AvatarId { get; set; } = "avatar_default";
    public string AvatarFrameId { get; set; } = "frame_default";
    public string? TitleId { get; set; }
    public string Status { get; set; } = AccountStatus.Active;
    public string? BanReason { get; set; }
    public DateTimeOffset? BannedUntil { get; set; }
    public int FailedLoginCount { get; set; }
    public DateTimeOffset? LockoutUntil { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public List<AccountRole> Roles { get; set; } = new();
}

public static class AccountStatus
{
    public const string Active = "active";
    public const string Banned = "banned";
}

public class AccountRole
{
    public long AccountId { get; set; }
    public string Role { get; set; } = "";
    public DateTimeOffset GrantedAt { get; set; }
    public long? GrantedBy { get; set; }
}

public class Session
{
    public Guid Id { get; set; }
    public long AccountId { get; set; }
    public string RefreshTokenHash { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset LastUsedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevokeReason { get; set; }
    public string? Platform { get; set; }
    public string? DeviceName { get; set; }
    public string? Ip { get; set; }
}

public class EmailToken
{
    public Guid Id { get; set; }
    public long AccountId { get; set; }
    public string Purpose { get; set; } = "";
    public string TokenHash { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
}

// ---------------------------------------------------------------------------
// Multi-server
// ---------------------------------------------------------------------------

public class GameServer
{
    public int Id { get; set; }
    public string Code { get; set; } = "";      // PS-1
    public string Name { get; set; } = "";      // Global
    public string Status { get; set; } = ServerStatus.ComingSoon;
    public bool RegistrationOpen { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? OpenedAt { get; set; }
    public int? MergedIntoServerId { get; set; }
}

public static class ServerStatus
{
    public const string ComingSoon = "coming_soon";
    public const string Online = "online";
    public const string Maintenance = "maintenance";
    public const string Merged = "merged";
    public static readonly string[] All = { ComingSoon, Online, Maintenance, Merged };
}

/// <summary>A player's independent progress profile on one server (level, coins, cards...).</summary>
public class ServerMembership
{
    public long Id { get; set; }
    public long AccountId { get; set; }
    public int ServerId { get; set; }
    public string Nickname { get; set; } = "";
    public int Level { get; set; } = 1;
    public int Exp { get; set; }
    public long? FavoriteCardId { get; set; }
    public string Status { get; set; } = "active";   // active | merged
    public int? OriginServerId { get; set; }          // preserved through server merges
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
}

// ---------------------------------------------------------------------------
// Pet catalog mirror (authoritative copy of design data, for FKs / admin)
// ---------------------------------------------------------------------------

public class PetFamilyRow
{
    public string Id { get; set; } = "";
    public string Element { get; set; } = "";
    public string Role { get; set; } = "";
    public int Stages { get; set; }
}

public class PetFormRow
{
    public string Id { get; set; } = "";
    public string FamilyId { get; set; } = "";
    public int Stage { get; set; }
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public string DataJson { get; set; } = "{}";
}

public class PetSkillRow
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "";
    public string DataJson { get; set; } = "{}";
}

// ---------------------------------------------------------------------------
// Cards
// ---------------------------------------------------------------------------

public class CardInstance
{
    /// <summary>Globally unique across all servers (sequence), so server merges never collide.</summary>
    public long Id { get; set; }
    public int ServerId { get; set; }
    public long OwnerMembershipId { get; set; }
    /// <summary>12-char collectible serial (A-Z0-9), unique forever, never changes. Not an ownership credential.</summary>
    public string Serial { get; set; } = "";
    public string FormId { get; set; } = "";
    public string Rarity { get; set; } = "";
    public string Quality { get; set; } = "";
    public string Finish { get; set; } = "";
    public int Level { get; set; } = 1;
    public int Exp { get; set; }
    public int SkillLevel { get; set; } = 1;
    public bool IsFavorite { get; set; }
    public bool IsLocked { get; set; }
    /// <summary>Business lock (e.g. "trade:{id}"). Cards with a busy state cannot be traded/evolved/consumed.</summary>
    public string? BusyState { get; set; }
    public string Origin { get; set; } = "";
    public string? OriginRef { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class CardOwnershipHistory
{
    public long Id { get; set; }
    public long CardId { get; set; }
    public int ServerId { get; set; }
    public long? FromMembershipId { get; set; }
    public long? ToMembershipId { get; set; }
    public string Reason { get; set; } = "";
    public string? RefId { get; set; }
    public DateTimeOffset At { get; set; }
}

public class CardEvolutionHistory
{
    public long Id { get; set; }
    public long CardId { get; set; }
    public long MembershipId { get; set; }
    public string FromFormId { get; set; } = "";
    public string ToFormId { get; set; } = "";
    public string CostJson { get; set; } = "{}";
    public DateTimeOffset At { get; set; }
}

public class CodexEntry
{
    public long MembershipId { get; set; }
    public string FormId { get; set; } = "";
    public long FirstCardId { get; set; }
    public DateTimeOffset DiscoveredAt { get; set; }
}

public class ShowcaseSlot
{
    public long MembershipId { get; set; }
    public int Slot { get; set; }
    public long CardId { get; set; }
}

public class Deck
{
    public long Id { get; set; }
    public long MembershipId { get; set; }
    public int ServerId { get; set; }
    public string Name { get; set; } = "";
    public bool IsDefault { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<DeckSlot> Slots { get; set; } = new();
}

public class DeckSlot
{
    public long DeckId { get; set; }
    public int Position { get; set; }
    public long CardId { get; set; }
}

// ---------------------------------------------------------------------------
// Packs
// ---------------------------------------------------------------------------

public class PackTypeRow
{
    public string Id { get; set; } = "";
    public int Version { get; set; }
    public bool Enabled { get; set; }
    public string DataJson { get; set; } = "{}";
    public DateTimeOffset UpdatedAt { get; set; }
}

public class PackInventory
{
    public long MembershipId { get; set; }
    public string PackTypeId { get; set; } = "";
    public int Quantity { get; set; }
}

public class PackOpening
{
    /// <summary>Unique opening transaction id.</summary>
    public Guid Id { get; set; }
    public long MembershipId { get; set; }
    public int ServerId { get; set; }
    public string PackTypeId { get; set; } = "";
    public int PackConfigVersion { get; set; }
    public string IdempotencyKey { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? AcknowledgedAt { get; set; }
    public List<PackOpeningResult> Results { get; set; } = new();
}

public class PackOpeningResult
{
    public Guid OpeningId { get; set; }
    public int Slot { get; set; }
    public long CardId { get; set; }
    public string FormId { get; set; } = "";
    public string Rarity { get; set; } = "";
    public string Quality { get; set; } = "";
    public string Finish { get; set; } = "";
}

// ---------------------------------------------------------------------------
// Economy
// ---------------------------------------------------------------------------

public class Wallet
{
    public long MembershipId { get; set; }
    public string Currency { get; set; } = PetStar.Rules.Currency.MiniGreenBean;
    public long Balance { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Real-money purchase of premium currency. Granted at most once per store purchase token.</summary>
public class IapPurchase
{
    public Guid Id { get; set; }
    public long AccountId { get; set; }
    public long MembershipId { get; set; }
    public int ServerId { get; set; }
    public string Platform { get; set; } = "";          // google_play | microsoft_store
    public string ProductId { get; set; } = "";
    public string PurchaseTokenHash { get; set; } = ""; // SHA-256 of store token (unique)
    public string? OrderId { get; set; }
    public string Status { get; set; } = "";            // verified | granted | rejected | refunded
    public string? RejectReason { get; set; }
    public string VerificationJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? GrantedAt { get; set; }
}

public class LedgerEntry
{
    public long Id { get; set; }
    public long MembershipId { get; set; }
    public int ServerId { get; set; }
    public string Currency { get; set; } = "";
    public long Delta { get; set; }
    public long BalanceAfter { get; set; }
    public string Reason { get; set; } = "";
    public string RefId { get; set; } = "";
    public long? ActorAccountId { get; set; }
    public DateTimeOffset At { get; set; }
}

public class InventoryItem
{
    public long MembershipId { get; set; }
    public string ItemId { get; set; } = "";
    public long Quantity { get; set; }
}

public class ItemLedgerEntry
{
    public long Id { get; set; }
    public long MembershipId { get; set; }
    public int ServerId { get; set; }
    public string ItemId { get; set; } = "";   // item id or "pack:{packTypeId}"
    public long Delta { get; set; }
    public long BalanceAfter { get; set; }
    public string Reason { get; set; } = "";
    public string RefId { get; set; } = "";
    public DateTimeOffset At { get; set; }
}

// ---------------------------------------------------------------------------
// Battles
// ---------------------------------------------------------------------------

public class Battle
{
    public Guid Id { get; set; }
    public int ServerId { get; set; }
    public string Mode { get; set; } = "pve";
    public string? StageId { get; set; }
    public long PlayerAMembershipId { get; set; }
    public long? PlayerBMembershipId { get; set; }
    public string Status { get; set; } = "active";
    public string StateJson { get; set; } = "{}";
    /// <summary>Secret RNG seed (CSPRNG). Never sent to clients while the battle is active.</summary>
    public long Seed { get; set; }
    public int Round { get; set; }
    public int? WinnerSide { get; set; }
    public string? EndReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? RoundDeadline { get; set; }
    /// <summary>Live PvP: consecutive rounds each side failed to submit (3 → forfeit for disconnection).</summary>
    public int MissedA { get; set; }
    public int MissedB { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
}

public class BattleRound
{
    public Guid BattleId { get; set; }
    public int Round { get; set; }
    public string ActionsAJson { get; set; } = "[]";
    public string ActionsBJson { get; set; } = "[]";
    public string EventsJson { get; set; } = "[]";
    public DateTimeOffset ResolvedAt { get; set; }
}

public class BattleResult
{
    public Guid BattleId { get; set; }
    public long? WinnerMembershipId { get; set; }
    public string RewardsJson { get; set; } = "{}";
    public DateTimeOffset At { get; set; }
}

public class PveProgress
{
    public long MembershipId { get; set; }
    public string StageId { get; set; } = "";
    public int ClearCount { get; set; }
    public DateTimeOffset? FirstClearedAt { get; set; }
    public int BestRounds { get; set; }
    public DateOnly DailyDate { get; set; }
    public int DailyRewardedClears { get; set; }
}

// ---------------------------------------------------------------------------
// Infrastructure
// ---------------------------------------------------------------------------

public class IdempotencyRecord
{
    public long AccountId { get; set; }
    public string Key { get; set; } = "";
    public string Endpoint { get; set; } = "";
    public string RequestHash { get; set; } = "";
    public int StatusCode { get; set; }
    public string? ResponseJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class AuditLog
{
    public long Id { get; set; }
    public long? ActorAccountId { get; set; }
    public string Action { get; set; } = "";
    public string? TargetType { get; set; }
    public string? TargetId { get; set; }
    public int? ServerId { get; set; }
    public string DataJson { get; set; } = "{}";
    public string? Ip { get; set; }
    public DateTimeOffset At { get; set; }
}
