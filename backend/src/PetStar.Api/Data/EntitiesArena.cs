namespace PetStar.Api.Data;

// Battle 3v3 match persistence (tables keep the arena_ prefix from the first match server). Ownership stays in card_instances / items; these tables only hold decks, matches
// and their history. Table names use the arena_ prefix so nothing collides with the existing battles/decks tables.

/// <summary>A saved Battle 3v3 team: exactly three of the player's own Pet cards (several teams per player).</summary>
public class TeamLineup
{
    public long Id { get; set; }
    public long MembershipId { get; set; }
    public int ServerId { get; set; }
    public string Name { get; set; } = "";
    public bool IsDefault { get; set; }
    public long Card1Id { get; set; }
    public long Card2Id { get; set; }
    public long Card3Id { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long[] CardIds => new[] { Card1Id, Card2Id, Card3Id };
}

/// <summary>The player's chosen Trainer. Character, appearance and outfit are separate ids so outfits, hairstyles and
/// accessories can be added later without schema changes. Cosmetics never change battle stats.</summary>
public class TrainerProfile
{
    public long MembershipId { get; set; }
    public int ServerId { get; set; }
    public string CharacterId { get; set; } = "";           // trainer_boy | trainer_girl (database/seed/trainers.json)
    public string AppearanceId { get; set; } = "default";
    public string OutfitId { get; set; } = "";
    public long? CompanionCardId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Outfits a player owns (the default outfit is granted on character creation; future shop/event outfits too).</summary>
public class TrainerOutfitOwned
{
    public long MembershipId { get; set; }
    public string OutfitId { get; set; } = "";
    public string Source { get; set; } = "default";
    public DateTimeOffset AcquiredAt { get; set; }
}

/// <summary>Card definitions exactly as a match used them, so replays stay correct after balance changes.</summary>
public class ArenaCardVersion
{
    public string Version { get; set; } = "";
    public string DefinitionsJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
}

public class ArenaMatch
{
    public Guid Id { get; set; }
    public int ServerId { get; set; }
    public string Mode { get; set; } = "";                  // pve | casual | ranked | friend | private
    public string Status { get; set; } = "active";          // waiting | invited | active | finished | abandoned | declined | expired
    public string DefsVersion { get; set; } = "";
    public long Seed { get; set; }                          // secure random; with the action log it reproduces the match
    public string? StageId { get; set; }
    public string? RoomCode { get; set; }
    public int? SeasonId { get; set; }
    public string StateJson { get; set; } = "{}";
    public int Version { get; set; }
    public int EventSeq { get; set; }
    public DateTimeOffset? Deadline { get; set; }
    public int? WinnerSide { get; set; }
    public string? EndReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class ArenaMatchPlayer
{
    public Guid MatchId { get; set; }
    public int Side { get; set; }
    public long? MembershipId { get; set; }                 // null = server AI
    public string Name { get; set; } = "";
    public bool IsAi { get; set; }
    public string DeckJson { get; set; } = "[]";            // the exact deck (definition ids + card instances) and energy types used
    public int? RatingBefore { get; set; }
    public int? RatingAfter { get; set; }
}

/// <summary>Every accepted action (players, AI and timeouts) in order. ActionId makes client retries idempotent.</summary>
public class ArenaEventRow
{
    public long Id { get; set; }
    public Guid MatchId { get; set; }
    public int Seq { get; set; }
    public int Side { get; set; }                           // -1 = timer
    public string? ActionId { get; set; }
    public string Kind { get; set; } = "action";            // action | timeout
    public string ActionJson { get; set; } = "{}";
    public int VersionAfter { get; set; }
    public DateTimeOffset At { get; set; }
}

public class ArenaSnapshot
{
    public long Id { get; set; }
    public Guid MatchId { get; set; }
    public int Seq { get; set; }
    public string StateJson { get; set; } = "{}";
    public DateTimeOffset At { get; set; }
}

public class ArenaRating
{
    public int SeasonId { get; set; }
    public long MembershipId { get; set; }
    public int ServerId { get; set; }
    public int Rating { get; set; } = 1000;
    public int Wins { get; set; }
    public int Losses { get; set; }
    public int Draws { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>One row per (match, player) that received a reward: the unique key makes double rewards impossible.</summary>
public class ArenaReward
{
    public long Id { get; set; }
    public Guid MatchId { get; set; }
    public long MembershipId { get; set; }
    public string RewardsJson { get; set; } = "{}";
    public DateTimeOffset At { get; set; }
}

public class ArenaQueueEntry
{
    public long MembershipId { get; set; }
    public int ServerId { get; set; }
    public string Mode { get; set; } = "casual";            // casual | ranked
    public long LineupId { get; set; }
    public int Rating { get; set; }
    public DateTimeOffset EnqueuedAt { get; set; }
}
