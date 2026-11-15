namespace PetStar.Api.Data;

// ============================================================================
// Guilds
// ============================================================================

public class Guild
{
    public long Id { get; set; }
    public int ServerId { get; set; }
    public string Name { get; set; } = "";
    public string NameNormalized { get; set; } = "";
    public string Description { get; set; } = "";
    public string JoinPolicy { get; set; } = "open";   // open | approval
    public int MaxMembers { get; set; } = 30;
    public long LeaderMembershipId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class GuildMember
{
    public long GuildId { get; set; }
    public long MembershipId { get; set; }
    public string Role { get; set; } = "member";        // leader | officer | member
    public DateTimeOffset JoinedAt { get; set; }
}

public class GuildJoinRequest
{
    public long Id { get; set; }
    public long GuildId { get; set; }
    public long MembershipId { get; set; }
    public string Status { get; set; } = "pending";     // pending | accepted | rejected
    public DateTimeOffset CreatedAt { get; set; }
}

public class GuildAnnouncement
{
    public long Id { get; set; }
    public long GuildId { get; set; }
    public long AuthorMembershipId { get; set; }
    public string Text { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

// ============================================================================
// Chat
// ============================================================================

public class ChatMessage
{
    public long Id { get; set; }
    public int ServerId { get; set; }
    public string Channel { get; set; } = "";           // world | guild:{id} | dm:{lowMid}:{highMid}
    public long AuthorMembershipId { get; set; }
    public string Text { get; set; } = "";
    public string Status { get; set; } = "visible";     // visible | hidden
    public DateTimeOffset CreatedAt { get; set; }
}

// ============================================================================
// Live PvP
// ============================================================================

public class PvpChallenge
{
    public long Id { get; set; }
    public int ServerId { get; set; }
    public long ChallengerMembershipId { get; set; }
    public long OpponentMembershipId { get; set; }
    public string Status { get; set; } = "pending";     // pending | accepted | declined | expired | cancelled
    public Guid? BattleId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

public class PvpQueueEntry
{
    public long MembershipId { get; set; }
    public int ServerId { get; set; }
    public int Rating { get; set; }
    public int Power { get; set; }
    public DateTimeOffset EnqueuedAt { get; set; }
}

/// <summary>Sealed plan of one side for one round of a live battle (hidden from the opponent until resolved).</summary>
public class PvpPendingAction
{
    public Guid BattleId { get; set; }
    public int Round { get; set; }
    public int Side { get; set; }
    public string ActionsJson { get; set; } = "[]";
    public DateTimeOffset SubmittedAt { get; set; }
}

// ============================================================================
// Giftcodes, events, login rewards
// ============================================================================

public class Giftcode
{
    public long Id { get; set; }
    public string Code { get; set; } = "";              // uppercase A-Z0-9-
    public int? ServerId { get; set; }                  // null = all servers
    public string RewardsJson { get; set; } = "{}";     // {currencies:{}, items:{}, packs:{}, cards:[{formId,rarity,quality,finish}]}
    public int MaxUses { get; set; }                    // 0 = unlimited
    public int Uses { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public bool Active { get; set; } = true;
    public long CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class GiftcodeRedemption
{
    public long GiftcodeId { get; set; }
    public long AccountId { get; set; }
    public int ServerId { get; set; }
    public long MembershipId { get; set; }
    public DateTimeOffset At { get; set; }
}

public class GameEvent
{
    public long Id { get; set; }
    public int? ServerId { get; set; }
    public string Key { get; set; } = "";               // localization key suffix
    public string Type { get; set; } = "claim";         // claim (one reward per member)
    public string RewardsJson { get; set; } = "{}";
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public bool Active { get; set; } = true;
    public long CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class EventClaim
{
    public long EventId { get; set; }
    public long MembershipId { get; set; }
    public DateTimeOffset At { get; set; }
}

public class LoginRewardClaim
{
    public long MembershipId { get; set; }
    public DateOnly Day { get; set; }
    public int StreakDay { get; set; }
    public DateTimeOffset At { get; set; }
}
