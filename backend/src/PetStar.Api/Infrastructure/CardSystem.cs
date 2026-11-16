using Microsoft.EntityFrameworkCore;
using PetStar.Api.Data;
using PetStar.Api.Features;
using PetStar.Contracts;
using PetStar.Rules;

namespace PetStar.Api.Infrastructure;

/// <summary>VIP level per membership. Points come only from Rainbow Stars spent on system purchases (packs);
/// player-to-player trades never add points. No real money is involved in this phase.</summary>
public sealed class VipService(PetStarDb db, GameData data)
{
    public IReadOnlyList<long> Thresholds => data.Economy.VipThresholds;

    public int LevelFor(long points)
    {
        int lvl = 0;
        for (int i = 0; i < Thresholds.Count; i++) if (points >= Thresholds[i]) lvl = i;
        return Math.Min(lvl, 10);
    }

    public async Task<VipDto> Get(long membershipId)
    {
        var row = await db.Vip.AsNoTracking().FirstOrDefaultAsync(v => v.MembershipId == membershipId);
        long pts = row?.Points ?? 0;
        int lvl = LevelFor(pts);
        return new VipDto { Level = lvl, Points = pts, NextLevelPoints = lvl + 1 < Thresholds.Count ? Thresholds[lvl + 1] : null, Thresholds = Thresholds.ToList() };
    }

    /// <summary>Adds points atomically (caller's transaction). Returns the new level.</summary>
    public async Task<int> AddPoints(long membershipId, int serverId, long points)
    {
        if (points <= 0) return (await Get(membershipId)).Level;
        if (db.Database.CurrentTransaction == null) throw new InvalidOperationException("VIP changes require a transaction");
        var pts = await db.Database.SqlQueryRaw<long>(
            "INSERT INTO membership_vip (membership_id, server_id, points, level, updated_at) VALUES ({0}, {1}, {2}, 0, now()) " +
            "ON CONFLICT (membership_id) DO UPDATE SET points = membership_vip.points + EXCLUDED.points, updated_at = now() RETURNING points AS \"Value\"",
            membershipId, serverId, points).ToListAsync();
        int lvl = LevelFor(pts[0]);
        await db.Database.ExecuteSqlRawAsync("UPDATE membership_vip SET level = {0} WHERE membership_id = {1}", lvl, membershipId);
        return lvl;
    }
}

/// <summary>Loads the server-stored progression of cards (incl. equipped engravings) for stats, DTOs and battles.</summary>
public static class CardProgressLoader
{
    public static async Task<Dictionary<long, List<CardEngraving>>> Engravings(PetStarDb db, IEnumerable<long> cardIds)
    {
        var ids = cardIds.Distinct().ToList();
        if (ids.Count == 0) return new();
        var rows = await db.Engravings.AsNoTracking().Where(e => e.CardId != null && ids.Contains(e.CardId.Value)).ToListAsync();
        return rows.GroupBy(e => e.CardId!.Value).ToDictionary(g => g.Key, g => g.OrderBy(e => e.Slot).ToList());
    }

    public static CardProgress Progress(CardInstance c, IEnumerable<CardEngraving>? engravings) => new()
    {
        Level = c.Level, Stars = c.Stars, Enhance = c.EnhanceLevel, Breakthrough = c.Breakthrough, Awakened = c.Awakened,
        SkillLevel = c.SkillLevel,
        Engravings = (engravings ?? Enumerable.Empty<CardEngraving>()).Select(e => new EngravingInfo { Type = e.Type, Level = e.Level }).ToList(),
    };

    public static EngravingDto ToDto(CardEngraving e) => new()
    {
        Id = e.Id, Type = e.Type, Level = e.Level, CardId = e.CardId, Slot = e.Slot, Value = Progression.EngravingValue(e.Type, e.Level),
    };
}

/// <summary>Server-side purchase conditions of a pack (level, VIP, limited event window/limits, Star invitation).</summary>
public sealed class PackGate(PetStarDb db, VipService vip)
{
    public sealed record Result(string? Blocked, LimitedEvent? Event, StarInvitation? Invitation, int BoughtInEvent);

    public static bool OnSale(PackTypeDef p, DateTimeOffset now) =>
        p.Enabled && (p.AvailableFrom == null || p.AvailableFrom <= now) && (p.AvailableUntil == null || p.AvailableUntil > now);

    public async Task<LimitedEvent?> ActiveEvent(string packTypeId, int serverId, DateTimeOffset now) =>
        await db.LimitedEvents.AsNoTracking()
            .Where(e => e.PackTypeId == packTypeId && !e.Cancelled && e.StartsAt <= now && e.EndsAt > now && (e.ServerId == null || e.ServerId == serverId))
            .OrderBy(e => e.EndsAt).FirstOrDefaultAsync();

    public async Task<StarInvitation?> ValidInvitation(long membershipId, int serverId, DateTimeOffset now) =>
        await db.StarInvitations.AsNoTracking()
            .Where(i => i.MembershipId == membershipId && i.ServerId == serverId && i.Status == "active" && i.ExpiresAt > now && i.UsedPacks < i.MaxPacks)
            .OrderBy(i => i.ExpiresAt).FirstOrDefaultAsync();

    /// <summary>Evaluates every condition for buying <paramref name="quantity"/> packs right now. Nothing from the client is trusted.</summary>
    public async Task<Result> Check(PackTypeDef p, long membershipId, int serverId, int quantity)
    {
        var now = DateTimeOffset.UtcNow;
        if (!OnSale(p, now)) return new("pack_not_available", null, null, 0);
        if (p.Price.Amount <= 0) return new("pack_not_purchasable", null, null, 0);
        var req = p.Requirements;
        int level = await db.Memberships.Where(m => m.Id == membershipId).Select(m => m.Level).FirstAsync();
        if (level < req.MinLevel) return new("pack_level_too_low", null, null, 0);
        if (req.MinVip > 0 && (await vip.Get(membershipId)).Level < req.MinVip) return new("pack_vip_too_low", null, null, 0);
        LimitedEvent? ev = null; int bought = 0;
        if (req.RequiresEvent)
        {
            ev = await ActiveEvent(p.Id, serverId, now);
            if (ev == null) return new("pack_event_not_active", null, null, 0);
            bought = await db.PackPurchases.Where(x => x.EventId == ev.Id && x.MembershipId == membershipId).SumAsync(x => (int?)x.Quantity) ?? 0;
            if (ev.PerPlayerLimit is int per && bought + quantity > per) return new("pack_event_player_limit", ev, null, bought);
            if (ev.TotalLimit is int total && ev.Sold + quantity > total) return new("pack_event_sold_out", ev, null, bought);
        }
        StarInvitation? inv = null;
        if (req.RequiresInvitation)
        {
            inv = await ValidInvitation(membershipId, serverId, now);
            if (inv == null) return new("pack_invitation_required", ev, null, bought);
            if (inv.UsedPacks + quantity > inv.MaxPacks) return new("pack_invitation_exhausted", ev, inv, bought);
        }
        return new(null, ev, inv, bought);
    }
}
