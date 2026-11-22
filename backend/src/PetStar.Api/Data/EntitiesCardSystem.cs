namespace PetStar.Api.Data;

// Card system expansion (9 packs, Divine Pets, card progression). All tables are additive; existing rows keep working.

/// <summary>VIP of one membership (per server). Points = Rainbow Stars spent on system purchases (never market trades).</summary>
public class MembershipVip
{
    public long MembershipId { get; set; }
    public int ServerId { get; set; }
    public long Points { get; set; }
    public int Level { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Immutable history of every pack configuration (price, odds, pools, requirements). Openings store the version.</summary>
public class PackTypeVersion
{
    public long Id { get; set; }
    public string PackTypeId { get; set; } = "";
    public int Version { get; set; }
    public bool Enabled { get; set; }
    public string DataJson { get; set; } = "{}";
    public long? ChangedByAccountId { get; set; }
    public string Reason { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Every pack purchase (separate purchase history per pack type; event/invitation used).</summary>
public class PackPurchase
{
    public Guid Id { get; set; }
    public long MembershipId { get; set; }
    public int ServerId { get; set; }
    public string PackTypeId { get; set; } = "";
    public int PackVersion { get; set; }
    public int Quantity { get; set; }
    public string Currency { get; set; } = "";
    public long Amount { get; set; }
    public long? EventId { get; set; }
    public Guid? InvitationId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Admin-scheduled sale window for a Limited pack. Ending it stops sales; owned cards are never taken back.</summary>
public class LimitedEvent
{
    public long Id { get; set; }
    public string PackTypeId { get; set; } = "";
    /// <summary>null = all servers.</summary>
    public int? ServerId { get; set; }
    public string NameVi { get; set; } = "";
    public string NameEn { get; set; } = "";
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    /// <summary>Total packs that may be sold during the event (null = unlimited).</summary>
    public int? TotalLimit { get; set; }
    public int? PerPlayerLimit { get; set; }
    public int Sold { get; set; }
    public bool Cancelled { get; set; }
    public long CreatedByAccountId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Invitation that unlocks the Star pack for one membership. Checked and consumed by the server at purchase.</summary>
public class StarInvitation
{
    public Guid Id { get; set; }
    public int ServerId { get; set; }
    public long MembershipId { get; set; }
    public long IssuedByAccountId { get; set; }
    public DateTimeOffset IssuedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public int MaxPacks { get; set; }
    public int UsedPacks { get; set; }
    public string Status { get; set; } = "active";   // active | revoked (expired / used up are derived)
    public string? Note { get; set; }
    public long? RevokedByAccountId { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevokeReason { get; set; }
}

/// <summary>An engraving (khắc ấn) owned by a membership; optionally equipped on one card slot. Travels with the card on trade.</summary>
public class CardEngraving
{
    public long Id { get; set; }
    public long MembershipId { get; set; }
    public int ServerId { get; set; }
    public string Type { get; set; } = "";
    public int Level { get; set; } = 1;
    public long? CardId { get; set; }
    public int? Slot { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>A 40-card Tactical Duel deck (Pet card instances + Spell/Trap cards). Entries are validated again at duel start.</summary>
public class DuelDeck
{
    public long Id { get; set; }
    public long MembershipId { get; set; }
    public int ServerId { get; set; }
    public string Name { get; set; } = "";
    public bool IsDefault { get; set; }
    /// <summary>[{ "kind": "pet", "cardId": 123 } | { "kind": "tactic", "tacticId": "sp_x", "count": 2 }]</summary>
    public string EntriesJson { get; set; } = "[]";
    public DateTimeOffset UpdatedAt { get; set; }
}
