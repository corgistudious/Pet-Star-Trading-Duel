using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PetStar.Api.Data;
using PetStar.Api.Features;

namespace PetStar.Api.Infrastructure;

/// <summary>Who may see what in the Social Hub, and the shared author "badge" (frame, guild, trainer) used by feed, chat,
/// market, leaderboards and profiles so cosmetics stay in sync everywhere.</summary>
public static class SocialAccess
{
    public static readonly string[] Visibilities = { "public", "friends", "guild", "private" };

    public static async Task<bool> AreFriends(PetStarDb db, long a, long b) =>
        a != b && await db.Friendships.AnyAsync(f => f.Status == "accepted" &&
            ((f.RequesterMembershipId == a && f.AddresseeMembershipId == b) || (f.AddresseeMembershipId == a && f.RequesterMembershipId == b)));

    public static async Task<List<long>> FriendIds(PetStarDb db, long mid) =>
        await db.Friendships.AsNoTracking().Where(f => f.Status == "accepted" && f.RequesterMembershipId == mid).Select(f => f.AddresseeMembershipId)
            .Union(db.Friendships.AsNoTracking().Where(f => f.Status == "accepted" && f.AddresseeMembershipId == mid).Select(f => f.RequesterMembershipId)).ToListAsync();

    public static async Task<long?> GuildOf(PetStarDb db, long mid) =>
        await db.GuildMembers.AsNoTracking().Where(m => m.MembershipId == mid).Select(m => (long?)m.GuildId).FirstOrDefaultAsync();

    /// <summary>Single source of truth for post visibility. Authors always see their own (also pending/hidden) posts.</summary>
    public static async Task<bool> CanView(PetStarDb db, FeedPost p, long viewer)
    {
        if (p.Status == "deleted") return false;
        if (p.AuthorMembershipId == viewer) return true;
        if (p.Status != "visible") return false;
        if (await MarketEndpoints.IsBlocked(db, viewer, p.AuthorMembershipId)) return false;
        if (p.GuildId != null && !await db.GuildMembers.AnyAsync(m => m.GuildId == p.GuildId && m.MembershipId == viewer)) return false;
        return p.Visibility switch
        {
            "public" => true,
            "guild" => p.GuildId != null,
            "friends" => await AreFriends(db, viewer, p.AuthorMembershipId),
            _ => false,
        };
    }

    public sealed record Badge(long Uid, string Nickname, int Level, string FrameId, string BubbleId, string? CharacterId, string? TitleId, string? EffectId,
        long? GuildId, string? GuildName, string? GuildTag);

    public static async Task<Dictionary<long, Badge>> Badges(PetStarDb db, IEnumerable<long> memberIds)
    {
        var ids = memberIds.Distinct().ToList();
        var members = await db.Memberships.AsNoTracking().Where(m => ids.Contains(m.Id)).ToListAsync();
        var trainers = await db.TrainerProfiles.AsNoTracking().Where(t => ids.Contains(t.MembershipId)).ToDictionaryAsync(t => t.MembershipId);
        var guilds = await db.GuildMembers.AsNoTracking().Where(g => ids.Contains(g.MembershipId))
            .Join(db.Guilds, g => g.GuildId, x => x.Id, (g, x) => new { g.MembershipId, x.Id, x.Name, x.Tag }).ToDictionaryAsync(x => x.MembershipId);
        return members.ToDictionary(m => m.Id, m =>
        {
            trainers.TryGetValue(m.Id, out var t);
            guilds.TryGetValue(m.Id, out var g);
            return new Badge(m.AccountId, m.Nickname, m.Level, t?.FrameId ?? "frame_default", t?.BubbleId ?? "bubble_default", t?.CharacterId, t?.TitleId, t?.EffectId,
                g?.Id, g?.Name, g?.Tag);
        });
    }

    /// <summary>Put flagged content into the review queue (idempotent per target).</summary>
    public static async Task Hold(PetStarDb db, int sid, string type, long id, long author, IEnumerable<string> flags)
    {
        if (await db.ModerationQueue.AnyAsync(q => q.TargetType == type && q.TargetId == id && q.Status == "pending")) return;
        db.ModerationQueue.Add(new ModerationQueueItem { ServerId = sid, TargetType = type, TargetId = id, AuthorMembershipId = author, Flags = string.Join(",", flags), CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
    }

    public static string Json(object o) => JsonSerializer.Serialize(o, IdempotencyService.Json);
}
