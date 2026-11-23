namespace PetStar.Api.Data;

// Social Hub v2, Guild v2, leaderboards, trading, cosmetics, mini games.
// Existing tables are extended where an equivalent already exists (feed_posts, feed_comments, guilds, chat_messages,
// market_*, trainer_profiles/trainer_outfits); the classes below are the genuinely new structures.

public class CommentLike
{
    public long CommentId { get; set; }
    public long MembershipId { get; set; }
    public DateTimeOffset At { get; set; }
}

/// <summary>Content held for review (auto-flagged posts/comments/chat) and its decision. Admin UI connects later.</summary>
public class ModerationQueueItem
{
    public long Id { get; set; }
    public int ServerId { get; set; }
    public string TargetType { get; set; } = "";      // post | comment | chat | guild
    public long TargetId { get; set; }
    public long AuthorMembershipId { get; set; }
    public string Flags { get; set; } = "";           // comma separated auto-moderation flags or "report"
    public string Status { get; set; } = "pending";   // pending | approved | rejected
    public DateTimeOffset CreatedAt { get; set; }
    public long? ReviewedByAccountId { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? Note { get; set; }
}

public class SocialSettings
{
    public long MembershipId { get; set; }
    public int ServerId { get; set; }
    public string DefaultVisibility { get; set; } = "public";   // public | friends | guild | private
    public string ProfileVisibility { get; set; } = "public";   // public | friends | private
    public string DefaultFeedTab { get; set; } = "explore";
    public DateTimeOffset UpdatedAt { get; set; }
}

public class GuildInvite
{
    public long Id { get; set; }
    public long GuildId { get; set; }
    public long MembershipId { get; set; }
    public long InviterMembershipId { get; set; }
    public string Status { get; set; } = "pending";   // pending | accepted | declined | cancelled
    public DateTimeOffset CreatedAt { get; set; }
}

public class GuildLog
{
    public long Id { get; set; }
    public long GuildId { get; set; }
    public long? ActorMembershipId { get; set; }
    public string Action { get; set; } = "";
    public string DataJson { get; set; } = "{}";
    public DateTimeOffset At { get; set; }
}

public class GuildContribution
{
    public long GuildId { get; set; }
    public long MembershipId { get; set; }
    public string PeriodKey { get; set; } = "";       // ISO week, e.g. 2026-W48, plus "all"
    public long Points { get; set; }
}

public class GuildMissionProgress
{
    public long GuildId { get; set; }
    public string MissionId { get; set; } = "";
    public string PeriodKey { get; set; } = "";
    public int Progress { get; set; }
}

public class GuildMissionClaim
{
    public long GuildId { get; set; }
    public string MissionId { get; set; } = "";
    public string PeriodKey { get; set; } = "";
    public long MembershipId { get; set; }
    public DateTimeOffset At { get; set; }
}

public class GuildBoss
{
    public long Id { get; set; }
    public long GuildId { get; set; }
    public string PeriodKey { get; set; } = "";
    public string BossId { get; set; } = "";
    public long MaxHp { get; set; }
    public long Hp { get; set; }
    public string Status { get; set; } = "active";    // active | defeated | expired
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DefeatedAt { get; set; }
}

public class GuildBossAttack
{
    public long Id { get; set; }
    public long BossId { get; set; }
    public long MembershipId { get; set; }
    public long Damage { get; set; }
    public DateOnly Day { get; set; }
    public long Seed { get; set; }
    public int Rounds { get; set; }
    public DateTimeOffset At { get; set; }
}

public class GuildWarEntry
{
    public long Id { get; set; }
    public int ServerId { get; set; }
    public string PeriodKey { get; set; } = "";
    public long GuildId { get; set; }
    public long? OpponentGuildId { get; set; }
    public int Points { get; set; }
    public int Wins { get; set; }
    public int Battles { get; set; }
    public string Result { get; set; } = "pending";   // pending | win | loss | draw
    public DateTimeOffset RegisteredAt { get; set; }
    public DateTimeOffset? SettledAt { get; set; }
}

public class LeaderboardSeason
{
    public int Id { get; set; }
    public int ServerId { get; set; }
    public string Key { get; set; } = "";
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public string Status { get; set; } = "active";    // active | closed
}

public class LeaderboardSnapshot
{
    public long Id { get; set; }
    public int SeasonId { get; set; }
    public string Board { get; set; } = "";
    public int Rank { get; set; }
    public string SubjectType { get; set; } = "player"; // player | guild
    public long SubjectId { get; set; }
    public string Name { get; set; } = "";
    public long Score { get; set; }
}

public class RankingReward
{
    public long Id { get; set; }
    public int SeasonId { get; set; }
    public string Board { get; set; } = "";
    public long MembershipId { get; set; }
    public int Rank { get; set; }
    public string RewardJson { get; set; } = "{}";
    public DateTimeOffset? ClaimedAt { get; set; }
}

public class CosmeticOwned
{
    public long MembershipId { get; set; }
    public string ItemId { get; set; } = "";
    public string Source { get; set; } = "";          // default | shop | event | ranking | admin
    public DateTimeOffset AcquiredAt { get; set; }
}

public class ShopPurchase
{
    public long Id { get; set; }
    public long MembershipId { get; set; }
    public int ServerId { get; set; }
    public string ItemId { get; set; } = "";
    public string Currency { get; set; } = "";
    public long Price { get; set; }
    public int Quantity { get; set; } = 1;
    public string IdempotencyKey { get; set; } = "";
    public DateTimeOffset At { get; set; }
}

/// <summary>Card-for-card trade offer. Cards are checked and moved atomically on accept; no currency, no cash-out.</summary>
public class TradeOffer
{
    public long Id { get; set; }
    public int ServerId { get; set; }
    public long FromMembershipId { get; set; }
    public long ToMembershipId { get; set; }
    public string OfferCardIds { get; set; } = "[]";
    public string RequestCardIds { get; set; } = "[]";
    public string Note { get; set; } = "";
    public string Status { get; set; } = "pending";   // pending | accepted | declined | cancelled | expired | failed
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
}

public class MiniGameSession
{
    public Guid Id { get; set; }
    public long MembershipId { get; set; }
    public int ServerId { get; set; }
    public string Game { get; set; } = "";            // memory | puzzle | sprint | quiz | daily
    public long Seed { get; set; }
    public DateOnly Day { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public int? Score { get; set; }
    public bool Rewarded { get; set; }
    public string RewardJson { get; set; } = "{}";
    public string? Rejected { get; set; }
}

public class ChatReadMarker
{
    public long MembershipId { get; set; }
    public string Channel { get; set; } = "";
    public long LastReadId { get; set; }
}
