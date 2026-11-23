namespace PetStar.Api.Data;

// ============================================================================
// Card serials, passports, valuation
// ============================================================================

/// <summary>Permanent registry of every serial ever issued. A serial is never reused, even if a card is retired.</summary>
public class CardSerialRegistry
{
    public string Serial { get; set; } = "";
    public long? CardId { get; set; }
    public DateTimeOffset IssuedAt { get; set; }
    public DateTimeOffset? RetiredAt { get; set; }
}

/// <summary>Immutable issuance record of a card (written once, when the card instance is created).</summary>
public class CardPassport
{
    public long CardId { get; set; }
    public string Serial { get; set; } = "";
    public int IssuedServerId { get; set; }
    public string Origin { get; set; } = "";
    public string? OriginRef { get; set; }
    public string FormIdAtIssue { get; set; } = "";
    public string Rarity { get; set; } = "";
    public string Quality { get; set; } = "";
    public string Finish { get; set; } = "";
    public long InitialValueBeans { get; set; }
    public long InitialValueStars { get; set; }
    public DateTimeOffset IssuedAt { get; set; }
}

/// <summary>Leveling / training / skill-upgrade history of a card ("lịch sử nâng cấp").</summary>
public class CardProgressEvent
{
    public long Id { get; set; }
    public long CardId { get; set; }
    public string Type { get; set; } = "";       // level_up | skill_upgrade
    public string DataJson { get; set; } = "{}";
    public DateTimeOffset At { get; set; }
}

/// <summary>Daily reference value per (server, form, rarity, currency): history of estimated values.</summary>
public class CardValuationSnapshot
{
    public long Id { get; set; }
    public int ServerId { get; set; }
    public string FormId { get; set; } = "";
    public string Rarity { get; set; } = "";
    public string Currency { get; set; } = "";
    public DateOnly Day { get; set; }
    public long InitialReference { get; set; }
    public long EstimatedValue { get; set; }
    public int SampleSize { get; set; }
    public string Confidence { get; set; } = "low";   // low | medium | high
    public int Supply { get; set; }
    public DateTimeOffset ComputedAt { get; set; }
}

// ============================================================================
// Marketplace
// ============================================================================

public static class ListingStatus
{
    public const string Active = "active", Sold = "sold", Cancelled = "cancelled", Expired = "expired", Removed = "removed";
}

public class MarketListing
{
    public long Id { get; set; }
    public int ServerId { get; set; }
    public long SellerMembershipId { get; set; }
    public long CardId { get; set; }
    public string Currency { get; set; } = "";
    public long Price { get; set; }
    public int FeeBps { get; set; }
    public string Status { get; set; } = ListingStatus.Active;
    public int Views { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public long? BuyerMembershipId { get; set; }
}

public class MarketTransaction
{
    public long Id { get; set; }
    public int ServerId { get; set; }
    public string Kind { get; set; } = "listing";   // listing | auction
    public long RefId { get; set; }                 // listing id or auction id (unique per kind)
    public long CardId { get; set; }
    public string FormId { get; set; } = "";
    public string Rarity { get; set; } = "";
    public long SellerMembershipId { get; set; }
    public long BuyerMembershipId { get; set; }
    public string Currency { get; set; } = "";
    public long Price { get; set; }
    public long Fee { get; set; }
    public long SellerNet { get; set; }
    public bool Suspicious { get; set; }
    public string? SuspicionReasons { get; set; }
    public DateTimeOffset At { get; set; }
}

/// <summary>Price evidence used for valuation: completed trades only (never listing prices).</summary>
public class MarketPriceHistory
{
    public long Id { get; set; }
    public int ServerId { get; set; }
    public long TransactionId { get; set; }
    public string FormId { get; set; } = "";
    public string Rarity { get; set; } = "";
    public string Currency { get; set; } = "";
    public long Price { get; set; }
    /// <summary>Price divided by the card's quality x finish multipliers, so different variants are comparable.</summary>
    public long NormalizedPrice { get; set; }
    public bool Excluded { get; set; }
    public DateTimeOffset At { get; set; }
}

// ============================================================================
// Auctions & reserved balances
// ============================================================================

public static class AuctionStatus
{
    public const string Active = "active", Settled = "settled", Cancelled = "cancelled", NoBids = "expired_no_bids";
}

public class AuctionListing
{
    public long Id { get; set; }
    public int ServerId { get; set; }
    public long SellerMembershipId { get; set; }
    public long CardId { get; set; }
    public string Currency { get; set; } = "";
    public long StartPrice { get; set; }
    public long? BuyoutPrice { get; set; }
    public int MinIncrementBps { get; set; }
    public int FeeBps { get; set; }
    public string Status { get; set; } = AuctionStatus.Active;
    public long? HighestBidId { get; set; }
    public long? HighestAmount { get; set; }
    public long? HighestBidderMembershipId { get; set; }
    public int BidCount { get; set; }
    public int Extensions { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public DateTimeOffset? SettledAt { get; set; }
}

public class AuctionBid
{
    public long Id { get; set; }
    public long AuctionId { get; set; }
    public long BidderMembershipId { get; set; }
    public long Amount { get; set; }
    public string Status { get; set; } = "leading";  // leading | outbid | won | refunded
    public bool Flagged { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class AuctionSettlement
{
    public long AuctionId { get; set; }
    public long? WinningBidId { get; set; }
    public long? TransactionId { get; set; }
    public string Outcome { get; set; } = "";   // sold | no_bids
    public DateTimeOffset At { get; set; }
}

/// <summary>Funds held for a bid. Holding debits the wallet (ledger "auction_hold"); release credits it back.</summary>
public class ReservedBalance
{
    public long Id { get; set; }
    public long MembershipId { get; set; }
    public int ServerId { get; set; }
    public string Currency { get; set; } = "";
    public long Amount { get; set; }
    public string Reason { get; set; } = "";
    public string RefId { get; set; } = "";
    public string Status { get; set; } = "held";   // held | released | captured
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
}

/// <summary>System sink (fees, burns). Fees never go to a person's account.</summary>
public class EconomySystemLedger
{
    public long Id { get; set; }
    public int ServerId { get; set; }
    public string Currency { get; set; } = "";
    public long Amount { get; set; }
    public string Reason { get; set; } = "";
    public string RefId { get; set; } = "";
    public DateTimeOffset At { get; set; }
}

public class EconomyAuditEvent
{
    public long Id { get; set; }
    public int ServerId { get; set; }
    public string Type { get; set; } = "";       // wash_trade | linked_accounts | price_anomaly | admin_adjustment ...
    public string Severity { get; set; } = "info";
    public long? MembershipId { get; set; }
    public long? CardId { get; set; }
    public string? RefId { get; set; }
    public string DataJson { get; set; } = "{}";
    public string Status { get; set; } = "open";  // open | reviewed | dismissed
    public DateTimeOffset At { get; set; }
}

// ============================================================================
// Wishlist & alerts
// ============================================================================

public class WishlistEntry
{
    public long Id { get; set; }
    public long MembershipId { get; set; }
    public int ServerId { get; set; }
    public string? FormId { get; set; }
    public string? FamilyId { get; set; }
    public string? MinRarity { get; set; }
    public string? Quality { get; set; }
    public string? Finish { get; set; }
    public string? Currency { get; set; }
    public long? MaxPrice { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class PriceAlert
{
    public long Id { get; set; }
    public long WishlistId { get; set; }
    public long MembershipId { get; set; }
    public string Kind { get; set; } = "listing";
    public long RefId { get; set; }
    public DateTimeOffset At { get; set; }
}

// ============================================================================
// Social: follows, friends, blocks, mutes, reports, feed
// ============================================================================

public class UserFollow
{
    public long FollowerMembershipId { get; set; }
    public long FolloweeMembershipId { get; set; }
    public int ServerId { get; set; }
    public DateTimeOffset At { get; set; }
}

public class Friendship
{
    public long Id { get; set; }
    public int ServerId { get; set; }
    public long RequesterMembershipId { get; set; }
    public long AddresseeMembershipId { get; set; }
    public string Status { get; set; } = "pending";   // pending | accepted
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
}

public class UserBlock
{
    public long BlockerMembershipId { get; set; }
    public long BlockedMembershipId { get; set; }
    public DateTimeOffset At { get; set; }
}

public class UserMute
{
    public long MuterMembershipId { get; set; }
    public long MutedMembershipId { get; set; }
    public DateTimeOffset At { get; set; }
}

public class UserReport
{
    public long Id { get; set; }
    public int ServerId { get; set; }
    public long ReporterMembershipId { get; set; }
    public string TargetType { get; set; } = "";   // post | comment | user | listing
    public long TargetId { get; set; }
    public string Reason { get; set; } = "";
    public string? Details { get; set; }
    public string Status { get; set; } = "open";    // open | actioned | dismissed
    public long? HandledBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? HandledAt { get; set; }
}

public class FeedPost
{
    public long Id { get; set; }
    public int ServerId { get; set; }
    public long AuthorMembershipId { get; set; }
    /// <summary>Set for guild-only posts (guild feed).</summary>
    public long? GuildId { get; set; }
    public string Type { get; set; } = "text";     // text | card | pack_opening | evolution | duel | achievement | album
    public string Caption { get; set; } = "";
    public string? RefId { get; set; }
    public string DataJson { get; set; } = "{}";
    public string Status { get; set; } = "visible"; // visible | hidden | deleted
    public bool Featured { get; set; }
    public int LikeCount { get; set; }
    public int CommentCount { get; set; }
    /// <summary>public | friends | guild | private. Guild posts also carry GuildId.</summary>
    public string Visibility { get; set; } = "public";
    public long? SharedFromPostId { get; set; }
    public int ShareCount { get; set; }
    public bool Pinned { get; set; }
    public DateTimeOffset? EditedAt { get; set; }
    /// <summary>Pet Showcase kind (rare | new_pack | evolved | companion | team | element | theme); null for normal posts.</summary>
    public string? ShowcaseKind { get; set; }
    /// <summary>Highest rarity among attached cards, derived on the server (never from the client).</summary>
    public int MaxRarity { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Controlled snapshot of a real card at posting time (the live card is linked by id).</summary>
public class FeedPostCard
{
    public long PostId { get; set; }
    public long CardId { get; set; }
    public int Position { get; set; }
    public string Serial { get; set; } = "";
    public string SnapshotJson { get; set; } = "{}";
    public long OwnerAtPostMembershipId { get; set; }
}

public class FeedLike
{
    public long PostId { get; set; }
    public long MembershipId { get; set; }
    public DateTimeOffset At { get; set; }
}

public class FeedSave
{
    public long PostId { get; set; }
    public long MembershipId { get; set; }
    public DateTimeOffset At { get; set; }
}

public class FeedComment
{
    public long Id { get; set; }
    public long PostId { get; set; }
    public long AuthorMembershipId { get; set; }
    public string Text { get; set; } = "";
    public string Status { get; set; } = "visible";
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>One reply level: replies point at a top-level comment.</summary>
    public long? ParentId { get; set; }
    public int LikeCount { get; set; }
}

public class ModerationAction
{
    public long Id { get; set; }
    public int ServerId { get; set; }
    public long ModeratorAccountId { get; set; }
    public string Action { get; set; } = "";       // hide_post | hide_comment | restrict | unrestrict | dismiss_report | feature_post | fraud_lock
    public string TargetType { get; set; } = "";
    public long TargetId { get; set; }
    public string Reason { get; set; } = "";
    public long? ReportId { get; set; }
    public DateTimeOffset At { get; set; }
}

public class AccountRestriction
{
    public long Id { get; set; }
    public long AccountId { get; set; }
    public string Scope { get; set; } = "";       // social | market | all
    public string Reason { get; set; } = "";
    public DateTimeOffset Until { get; set; }
    public long? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

// ============================================================================
// Notifications, mailbox, achievements, collections, ranked
// ============================================================================

public class Notification
{
    public long Id { get; set; }
    public long MembershipId { get; set; }
    public string Type { get; set; } = "";
    public string DataJson { get; set; } = "{}";
    public string DedupeKey { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
}

public class MailboxMessage
{
    public long Id { get; set; }
    public long MembershipId { get; set; }
    public string TitleKey { get; set; } = "";
    public string Body { get; set; } = "";
    public string AttachmentsJson { get; set; } = "{}";  // {currencies:{}, items:{}, packs:{}}
    public string DedupeKey { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? ClaimedAt { get; set; }
}

public class AchievementProgress
{
    public long MembershipId { get; set; }
    public string AchievementId { get; set; } = "";
    public DateTimeOffset UnlockedAt { get; set; }
}

public class Collection
{
    public long Id { get; set; }
    public long MembershipId { get; set; }
    public int ServerId { get; set; }
    public string Name { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public List<CollectionItem> Items { get; set; } = new();
}

public class CollectionItem
{
    public long CollectionId { get; set; }
    public long CardId { get; set; }
    public int Position { get; set; }
}

public class RankedSeason
{
    public int Id { get; set; }
    public int ServerId { get; set; }
    public string Name { get; set; } = "";
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public string Status { get; set; } = "active";   // active | closed
}

public class RankedRecord
{
    public int SeasonId { get; set; }
    public long MembershipId { get; set; }
    public int Rating { get; set; } = 1000;
    public int Wins { get; set; }
    public int Losses { get; set; }
    public int Streak { get; set; }
    public int BestStreak { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
