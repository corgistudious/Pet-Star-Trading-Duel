using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using PetStar.Api.Data;
using PetStar.Api.Features;

namespace PetStar.Api.Infrastructure;

public sealed class MissionDef
{
    public string Id { get; set; } = "";
    public string Trigger { get; set; } = "";
    public int Target { get; set; }
    public RewardBundle Rewards { get; set; } = new();
}

public sealed class MissionCatalog
{
    public List<MissionDef> Daily { get; set; } = new();
    public List<MissionDef> Weekly { get; set; } = new();
    public IEnumerable<MissionDef> All => Daily.Concat(Weekly);

    public static readonly string[] Triggers =
        { "login_claimed", "pack_opened", "battle_completed", "battle_won", "ranked_won", "live_pvp_completed", "evolution", "skill_trained", "missions_claimed" };

    public static MissionCatalog Load(string dir)
    {
        var node = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "missions.json")))!;
        var o = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var cat = new MissionCatalog
        {
            Daily = node["daily"].Deserialize<List<MissionDef>>(o)!,
            Weekly = node["weekly"].Deserialize<List<MissionDef>>(o)!,
        };
        var ids = cat.All.Select(m => m.Id).ToList();
        if (ids.Distinct().Count() != ids.Count) throw new InvalidDataException("missions: duplicate id");
        foreach (var m in cat.All)
        {
            if (!Triggers.Contains(m.Trigger)) throw new InvalidDataException($"missions: unknown trigger {m.Trigger}");
            if (m.Target < 1) throw new InvalidDataException($"missions: target < 1 for {m.Id}");
            if (m.Rewards.Cards.Count > 0) throw new InvalidDataException("missions: card rewards are not allowed");
        }
        foreach (var list in new[] { cat.Daily, cat.Weekly })
            foreach (var m in list.Where(x => x.Trigger == "missions_claimed"))
                if (m.Target != list.Count - 1) throw new InvalidDataException($"missions: {m.Id} target must equal the number of other missions");
        return cat;
    }
}

/// <summary>UTC reset windows. Daily = calendar day; weekly = ISO week (Monday 00:00 UTC).</summary>
public static class MissionPeriods
{
    public static (string key, DateTimeOffset start, DateTimeOffset end) Daily(DateTimeOffset now)
    {
        var start = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        return ("d:" + start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), start, start.AddDays(1));
    }

    public static (string key, DateTimeOffset start, DateTimeOffset end) Weekly(DateTimeOffset now)
    {
        var day = now.UtcDateTime.Date;
        int offset = ((int)day.DayOfWeek + 6) % 7; // Monday = 0
        var start = new DateTimeOffset(day.AddDays(-offset), TimeSpan.Zero);
        int week = ISOWeek.GetWeekOfYear(start.UtcDateTime), year = ISOWeek.GetYear(start.UtcDateTime);
        return ($"w:{year}-W{week:00}", start, start.AddDays(7));
    }
}

public sealed record MissionView(string Id, string Trigger, int Target, int Progress, bool Completed, bool Claimed, RewardBundle Rewards);
public sealed record MissionPeriodView(string PeriodKey, DateTimeOffset ResetsAt, List<MissionView> Items);

/// <summary>Computes progress from authoritative records within the period and pays claims exactly once.</summary>
public sealed class MissionService(PetStarDb db, MissionCatalog missions)
{
    public async Task<(MissionPeriodView daily, MissionPeriodView weekly)> View(long mid, DateTimeOffset now)
    {
        var d = MissionPeriods.Daily(now);
        var w = MissionPeriods.Weekly(now);
        return (await Period(mid, missions.Daily, d), await Period(mid, missions.Weekly, w));
    }

    private async Task<MissionPeriodView> Period(long mid, List<MissionDef> list, (string key, DateTimeOffset start, DateTimeOffset end) p)
    {
        var claimed = (await db.MissionClaims.AsNoTracking().Where(c => c.MembershipId == mid && c.PeriodKey == p.key).Select(c => c.MissionId).ToListAsync()).ToHashSet();
        var cache = new Dictionary<string, int>();
        var items = new List<MissionView>();
        foreach (var m in list)
        {
            int progress = m.Trigger == "missions_claimed"
                ? list.Count(x => x.Id != m.Id && claimed.Contains(x.Id))
                : cache.TryGetValue(m.Trigger, out var v) ? v : cache[m.Trigger] = await Count(mid, m.Trigger, p.start, p.end);
            progress = Math.Min(progress, m.Target);
            items.Add(new MissionView(m.Id, m.Trigger, m.Target, progress, progress >= m.Target, claimed.Contains(m.Id), m.Rewards));
        }
        return new MissionPeriodView(p.key, p.end, items);
    }

    /// <summary>Only battles the player actually played count (attacker in async modes, either side live); surrender/forfeit excluded.</summary>
    private IQueryable<Battle> PlayedBattles(long mid, DateTimeOffset from, DateTimeOffset to) =>
        db.Battles.AsNoTracking().Where(b => b.Status == "finished" && b.FinishedAt >= from && b.FinishedAt < to
            && b.EndReason != "surrender" && b.EndReason != "disconnect"
            && (b.PlayerAMembershipId == mid || (b.Mode.StartsWith("live_") && b.PlayerBMembershipId == mid)));

    private async Task<int> Count(long mid, string trigger, DateTimeOffset from, DateTimeOffset to) => trigger switch
    {
        "login_claimed" => await db.LoginRewards.CountAsync(c => c.MembershipId == mid && c.At >= from && c.At < to),
        "pack_opened" => await db.PackOpenings.CountAsync(o => o.MembershipId == mid && o.CreatedAt >= from && o.CreatedAt < to),
        "battle_completed" => await PlayedBattles(mid, from, to).CountAsync(),
        "battle_won" => await PlayedBattles(mid, from, to).Join(db.BattleResults, b => b.Id, r => r.BattleId, (b, r) => r).CountAsync(r => r.WinnerMembershipId == mid),
        "ranked_won" => await PlayedBattles(mid, from, to).Where(b => b.Mode == "ranked").Join(db.BattleResults, b => b.Id, r => r.BattleId, (b, r) => r).CountAsync(r => r.WinnerMembershipId == mid),
        "live_pvp_completed" => await PlayedBattles(mid, from, to).CountAsync(b => b.Mode.StartsWith("live_")),
        "evolution" => await db.CardEvolutions.CountAsync(e => e.MembershipId == mid && e.At >= from && e.At < to),
        "skill_trained" => await db.Ledger.Where(l => l.MembershipId == mid && l.Reason == "training" && l.Delta < 0 && l.At >= from && l.At < to).Select(l => l.RefId).Distinct().CountAsync(),
        _ => 0,
    };

    /// <summary>Inserts the claim row (PK makes it once per period) and pays through the ledger with an idempotent ref.</summary>
    public async Task<MissionView> Claim(EconomyService eco, CardFactory factory, GameData data, long mid, int sid, string missionId, DateTimeOffset now)
    {
        var isDaily = missions.Daily.Any(m => m.Id == missionId);
        var def = missions.All.FirstOrDefault(m => m.Id == missionId) ?? throw ApiException.NotFound("mission_not_found");
        var (daily, weekly) = await View(mid, now);
        var period = isDaily ? daily : weekly;
        var view = period.Items.First(i => i.Id == missionId);
        if (view.Claimed) throw ApiException.Conflict("mission_already_claimed");
        if (!view.Completed) throw ApiException.Conflict("mission_not_completed");

        await using var tx = await db.Database.BeginTransactionAsync();
        int inserted = await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO mission_claims (membership_id, server_id, mission_id, period_key, at) VALUES ({0}, {1}, {2}, {3}, now()) ON CONFLICT DO NOTHING",
            mid, sid, missionId, period.PeriodKey);
        if (inserted == 0) throw ApiException.Conflict("mission_already_claimed");
        await RewardsEndpoints.Grant(eco, factory, data, mid, sid, def.Rewards, "mission", $"mission:{missionId}:{period.PeriodKey}");
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return view with { Claimed = true };
    }
}
