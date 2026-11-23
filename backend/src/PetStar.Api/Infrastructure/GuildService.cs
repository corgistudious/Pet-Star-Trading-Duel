using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using PetStar.Api.Data;
using PetStar.Api.Features;
using PetStar.Rules;
using PetStar.Rules.Team;

namespace PetStar.Api.Infrastructure;

public sealed class GuildConfig
{
    public string Version { get; set; } = "";
    public List<string> Roles { get; set; } = new();
    public Dictionary<string, int> RoleRank { get; set; } = new();
    public List<long> Levels { get; set; } = new();
    public int MembersBase { get; set; }
    public int MembersPerLevel { get; set; }
    public List<string> Emblems { get; set; } = new();
    public List<string> Banners { get; set; } = new();
    public List<GuildMissionDef> Missions { get; set; } = new();
    public GuildBossConfig Boss { get; set; } = new();
    public GuildWarConfig War { get; set; } = new();

    public int Rank(string role) => RoleRank.GetValueOrDefault(role, 1);
    public int MaxMembers(int level) => MembersBase + MembersPerLevel * (level - 1);
    public int LevelFor(long exp) { int l = 1; for (int i = 0; i < Levels.Count; i++) if (exp >= Levels[i]) l = i + 1; return l; }

    public static GuildConfig Load(string dir) =>
        JsonSerializer.Deserialize<GuildConfig>(File.ReadAllText(Path.Combine(dir, "guild.json")), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
}
public sealed class GuildMissionDef { public string Id { get; set; } = ""; public string Period { get; set; } = ""; public string Category { get; set; } = ""; public string Kind { get; set; } = ""; public int Target { get; set; } public int GuildExp { get; set; } public int Contribution { get; set; } public Dictionary<string, long> Reward { get; set; } = new(); }
public sealed class GuildBossDef { public string Id { get; set; } = ""; public string FormId { get; set; } = ""; public long HpBase { get; set; } public long HpPerLevel { get; set; } public List<string> Minions { get; set; } = new(); }
public sealed class GuildBossConfig { public int AttacksPerDay { get; set; } public int Rounds { get; set; } public List<GuildBossDef> Bosses { get; set; } = new(); public Dictionary<string, long> RewardOnDefeat { get; set; } = new(); public int GuildExpOnDefeat { get; set; } }
public sealed class GuildWarConfig { public int PointsWin { get; set; } public int PointsDraw { get; set; } public int BattlesPerMemberPerDay { get; set; } public Dictionary<string, Dictionary<string, long>> Rewards { get; set; } = new(); public Dictionary<string, int> GuildExp { get; set; } = new(); }

/// <summary>Guild progression shared by every feature: mission progress hooks, contribution, guild EXP/level, logs,
/// the server-simulated Guild Boss and Guild War scoring. All amounts are computed here, never accepted from a client.</summary>
public sealed class GuildService(PetStarDb db, GuildConfig cfg, TeamRulesProvider rp, GameData data, EconomyService eco, NotificationService notes)
{
    public GuildConfig Config => cfg;
    public static string Week(DateTimeOffset now) => MissionPeriods.Weekly(now).key;
    public static string Day(DateTimeOffset now) => MissionPeriods.Daily(now).key;
    public string PeriodKey(string period, DateTimeOffset now) => period == "daily" ? Day(now) : Week(now);

    public async Task Log(long guildId, long? actor, string action, object? data = null)
    {
        db.GuildLogs.Add(new GuildLog { GuildId = guildId, ActorMembershipId = actor, Action = action, DataJson = JsonSerializer.Serialize(data ?? new { }, IdempotencyService.Json), At = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
    }

    public async Task AddExp(long guildId, long exp)
    {
        if (exp <= 0) return;
        await db.Guilds.Where(g => g.Id == guildId).ExecuteUpdateAsync(u => u.SetProperty(g => g.Exp, g => g.Exp + exp));
        var g = await db.Guilds.AsNoTracking().FirstAsync(x => x.Id == guildId);
        int lvl = cfg.LevelFor(g.Exp);
        if (lvl > g.Level)
        {
            await db.Guilds.Where(x => x.Id == guildId).ExecuteUpdateAsync(u => u.SetProperty(x => x.Level, lvl).SetProperty(x => x.MaxMembers, cfg.MaxMembers(lvl)));
            await Log(guildId, null, "level_up", new { level = lvl });
        }
    }

    public async Task Contribute(long guildId, long mid, long points)
    {
        if (points <= 0) return;
        var week = Week(DateTimeOffset.UtcNow);
        foreach (var key in new[] { week, "all" })
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO guild_contributions (guild_id, membership_id, period_key, points) VALUES ({0}, {1}, {2}, {3}) " +
                "ON CONFLICT (guild_id, membership_id, period_key) DO UPDATE SET points = guild_contributions.points + EXCLUDED.points", guildId, mid, key, points);
    }

    /// <summary>Called from game features (PvE/PvP finish, pack opening, evolution, posts, boss attacks).</summary>
    public async Task Progress(long mid, string kind, int amount)
    {
        var gid = await db.GuildMembers.AsNoTracking().Where(m => m.MembershipId == mid).Select(m => (long?)m.GuildId).FirstOrDefaultAsync();
        if (gid == null || amount <= 0) return;
        var now = DateTimeOffset.UtcNow;
        long contribution = 0;
        foreach (var m in cfg.Missions.Where(m => m.Kind == kind))
        {
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO guild_mission_progress (guild_id, mission_id, period_key, progress) VALUES ({0}, {1}, {2}, {3}) " +
                "ON CONFLICT (guild_id, mission_id, period_key) DO UPDATE SET progress = LEAST({4}, guild_mission_progress.progress + EXCLUDED.progress)",
                gid.Value, m.Id, PeriodKey(m.Period, now), amount, m.Target * 10);
            contribution = Math.Max(contribution, m.Contribution * amount);
        }
        await Contribute(gid.Value, mid, contribution);
    }

    public async Task<object> Missions(long guildId, long mid)
    {
        var now = DateTimeOffset.UtcNow;
        var keys = cfg.Missions.Select(m => PeriodKey(m.Period, now)).Distinct().ToList();
        var prog = await db.GuildMissionProgress.AsNoTracking().Where(p => p.GuildId == guildId && keys.Contains(p.PeriodKey)).ToListAsync();
        var claims = await db.GuildMissionClaims.AsNoTracking().Where(c => c.GuildId == guildId && c.MembershipId == mid && keys.Contains(c.PeriodKey)).ToListAsync();
        return cfg.Missions.Select(m =>
        {
            var key = PeriodKey(m.Period, now);
            int p = prog.FirstOrDefault(x => x.MissionId == m.Id && x.PeriodKey == key)?.Progress ?? 0;
            return new { m.Id, m.Period, m.Category, m.Kind, m.Target, progress = Math.Min(p, m.Target), completed = p >= m.Target, claimed = claims.Any(c => c.MissionId == m.Id && c.PeriodKey == key), m.Reward, m.GuildExp };
        }).ToList();
    }

    /// <summary>Each member claims a completed guild mission once per period; the first claim also grants guild EXP.</summary>
    public async Task<object> Claim(long guildId, long mid, int sid, string missionId)
    {
        var m = cfg.Missions.FirstOrDefault(x => x.Id == missionId) ?? throw ApiException.NotFound("mission_not_found");
        var key = PeriodKey(m.Period, DateTimeOffset.UtcNow);
        var p = await db.GuildMissionProgress.AsNoTracking().FirstOrDefaultAsync(x => x.GuildId == guildId && x.MissionId == m.Id && x.PeriodKey == key);
        if ((p?.Progress ?? 0) < m.Target) throw ApiException.Conflict("mission_not_completed");
        await using var tx = await db.Database.BeginTransactionAsync();
        int n = await db.Database.ExecuteSqlRawAsync("INSERT INTO guild_mission_claims (guild_id, mission_id, period_key, membership_id, at) VALUES ({0}, {1}, {2}, {3}, now()) ON CONFLICT DO NOTHING", guildId, m.Id, key, mid);
        if (n == 0) throw ApiException.Conflict("already_claimed");
        bool first = await db.GuildMissionClaims.CountAsync(c => c.GuildId == guildId && c.MissionId == m.Id && c.PeriodKey == key) == 1;
        foreach (var (cur, amt) in m.Reward) await eco.Currency(mid, sid, cur, amt, "guild_mission", $"gm:{guildId}:{m.Id}:{key}:{mid}");
        if (first) await AddExp(guildId, m.GuildExp);
        await tx.CommitAsync();
        return new { claimed = true, m.Reward, guildExp = first ? m.GuildExp : 0 };
    }

    // ------------------------------------------------------------------ Guild Boss

    public async Task<GuildBoss> CurrentBoss(long guildId)
    {
        var week = Week(DateTimeOffset.UtcNow);
        var boss = await db.GuildBosses.FirstOrDefaultAsync(b => b.GuildId == guildId && b.PeriodKey == week);
        if (boss != null) return boss;
        var g = await db.Guilds.AsNoTracking().FirstAsync(x => x.Id == guildId);
        var def = cfg.Boss.Bosses[Math.Abs(week.GetHashCode()) % cfg.Boss.Bosses.Count];
        long hp = def.HpBase + def.HpPerLevel * (g.Level - 1);
        await db.Database.ExecuteSqlRawAsync("INSERT INTO guild_bosses (guild_id, period_key, boss_id, max_hp, hp, status, created_at) VALUES ({0}, {1}, {2}, {3}, {3}, 'active', now()) ON CONFLICT DO NOTHING",
            guildId, week, def.Id, hp);
        return await db.GuildBosses.FirstAsync(b => b.GuildId == guildId && b.PeriodKey == week);
    }

    /// <summary>One boss attack: the member's saved 3v3 team fights the boss team for N rounds with the shared engine
    /// (AI on both sides, server seed). Damage dealt to the boss unit is subtracted from the shared HP pool under a row lock.</summary>
    public async Task<object> AttackBoss(long guildId, long mid, int sid, TeamService teams)
    {
        var boss = await CurrentBoss(guildId);
        if (boss.Status != "active") throw ApiException.Conflict("boss_defeated");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (await db.GuildBossAttacks.CountAsync(a => a.BossId == boss.Id && a.MembershipId == mid && a.Day == today) >= cfg.Boss.AttacksPerDay) throw ApiException.Conflict("boss_attacks_used");
        var (units, _) = await teams.PlayerTeam(mid, sid, null);
        var def = cfg.Boss.Bosses.First(b => b.Id == boss.BossId);
        var rules = rp.Rules;
        var g = await db.Guilds.AsNoTracking().FirstAsync(x => x.Id == guildId);
        var bossUnit = TeamBuilder.Unit(data.Catalog, rules, def.FormId, "SR", "B", new CardProgress { Level = 6 + 2 * g.Level }, 0);
        bossUnit.Hp *= 40;   // a raid-sized body for the simulated window (percent effects stay in int range); the pool below is the real HP
        var enemy = new List<TeamSetupUnit> { bossUnit };
        enemy.AddRange(def.Minions.Select(f => TeamBuilder.Unit(data.Catalog, rules, f, "R", "B", new CardProgress { Level = 3 + g.Level }, 0)));
        long seed = SecureRandomSource.NewSeed();
        var st = TeamEngine.Create(rules, new TeamSetupPlayer { Name = "team", Units = units }, new TeamSetupPlayer { Name = def.Id, Units = enemy }, unchecked((ulong)seed));
        while (!st.Finished && st.Round <= cfg.Boss.Rounds)
        {
            TeamEngine.Apply(rules, st, 0, TeamAi.Decide(rules, st, 0));
            if (!st.Finished) TeamEngine.Apply(rules, st, 1, TeamAi.Decide(rules, st, 1));
        }
        // damage dealt to the whole boss formation (the boss and its guards) counts against the shared pool
        // = every hit (direct, counter and damage over time) landed on the boss formation, from the engine's event log
        var bossSide = st.Units.Where(u => u.Side == 1).Select(u => u.Uid).ToHashSet();
        long damage = st.Log.Where(e => e.Type is "damage" or "counter" or "burn" or "poison" && bossSide.Contains(e.B)).Sum(e => (long)e.N);

        await using var tx = await db.Database.BeginTransactionAsync();
        var locked = await db.GuildBosses.FromSqlRaw("SELECT * FROM guild_bosses WHERE id = {0} FOR UPDATE", boss.Id).FirstAsync();
        if (locked.Status != "active") throw ApiException.Conflict("boss_defeated");
        if (await db.GuildBossAttacks.CountAsync(a => a.BossId == boss.Id && a.MembershipId == mid && a.Day == today) >= cfg.Boss.AttacksPerDay) throw ApiException.Conflict("boss_attacks_used");
        long applied = Math.Min(damage, locked.Hp);
        locked.Hp -= applied;
        bool defeated = locked.Hp == 0;
        if (defeated) { locked.Status = "defeated"; locked.DefeatedAt = DateTimeOffset.UtcNow; }
        db.GuildBossAttacks.Add(new GuildBossAttack { BossId = boss.Id, MembershipId = mid, Damage = applied, Day = today, Seed = seed, Rounds = st.Round - 1, At = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        await Contribute(guildId, mid, 5 + applied / 2000);
        if (defeated)
        {
            await AddExp(guildId, cfg.Boss.GuildExpOnDefeat);
            var hitters = await db.GuildBossAttacks.Where(a => a.BossId == boss.Id).Select(a => a.MembershipId).Distinct().ToListAsync();
            foreach (var h in hitters)
            {
                var hm = await db.Memberships.AsNoTracking().FirstAsync(x => x.Id == h);
                foreach (var (cur, amt) in cfg.Boss.RewardOnDefeat) await eco.Currency(h, hm.ServerId, cur, amt, "guild_boss", $"gboss:{boss.Id}:{h}", tolerateDuplicate: true);
                await notes.Notify(h, "guild_boss_defeated", new { guildId, bossId = boss.BossId }, $"gboss:{boss.Id}:{h}");
            }
            await Log(guildId, mid, "boss_defeated", new { boss = boss.BossId });
        }
        await tx.CommitAsync();
        await Progress(mid, "boss_attack", 1);
        return new { damage = applied, rounds = st.Round - 1, seed, hp = locked.Hp, maxHp = locked.MaxHp, defeated, attacksLeft = cfg.Boss.AttacksPerDay - await db.GuildBossAttacks.CountAsync(a => a.BossId == boss.Id && a.MembershipId == mid && a.Day == today) };
    }

    // ------------------------------------------------------------------ Guild War

    /// <summary>Pairs unpaired registered guilds of the current week by closest level (called on register).</summary>
    public async Task Pair(int sid, string week)
    {
        var open = await db.GuildWarEntries.Where(e => e.ServerId == sid && e.PeriodKey == week && e.OpponentGuildId == null).OrderBy(e => e.RegisteredAt).ToListAsync();
        var levels = await db.Guilds.AsNoTracking().Where(g => open.Select(o => o.GuildId).Contains(g.Id)).ToDictionaryAsync(g => g.Id, g => g.Level);
        while (open.Count >= 2)
        {
            var a = open[0];
            var b = open.Skip(1).OrderBy(x => Math.Abs(levels[x.GuildId] - levels[a.GuildId])).ThenBy(x => x.RegisteredAt).First();
            a.OpponentGuildId = b.GuildId; b.OpponentGuildId = a.GuildId;
            open.Remove(a); open.Remove(b);
            await Log(a.GuildId, null, "war_paired", new { opponent = b.GuildId });
            await Log(b.GuildId, null, "war_paired", new { opponent = a.GuildId });
        }
        await db.SaveChangesAsync();
    }

    /// <summary>Hook from TeamService.Finish for mode "guildwar" (StageId = "gw:{entryId}"): scores for the attacker's guild.</summary>
    public async Task OnWarBattle(string? stageId, int? winnerSide, long mid)
    {
        if (stageId == null || !stageId.StartsWith("gw:") || !long.TryParse(stageId[3..], out var entryId)) return;
        int pts = winnerSide == 0 ? cfg.War.PointsWin : winnerSide == null ? cfg.War.PointsDraw : 0;
        await db.GuildWarEntries.Where(e => e.Id == entryId).ExecuteUpdateAsync(u => u
            .SetProperty(e => e.Points, e => e.Points + pts).SetProperty(e => e.Battles, e => e.Battles + 1).SetProperty(e => e.Wins, e => e.Wins + (winnerSide == 0 ? 1 : 0)));
        var gid = await db.GuildWarEntries.Where(e => e.Id == entryId).Select(e => e.GuildId).FirstAsync();
        await Contribute(gid, mid, pts * 10);
    }

    /// <summary>Settles finished weeks (lazy, idempotent): result by points, rewards to members who fought.</summary>
    public async Task SettleWars(int sid)
    {
        var current = Week(DateTimeOffset.UtcNow);
        var due = await db.GuildWarEntries.Where(e => e.ServerId == sid && e.PeriodKey != current && e.SettledAt == null).ToListAsync();
        foreach (var e in due)
        {
            var opp = due.FirstOrDefault(x => x.PeriodKey == e.PeriodKey && x.GuildId == e.OpponentGuildId)
                      ?? await db.GuildWarEntries.AsNoTracking().FirstOrDefaultAsync(x => x.PeriodKey == e.PeriodKey && x.GuildId == e.OpponentGuildId);
            e.Result = opp == null ? "draw" : e.Points > opp.Points ? "win" : e.Points < opp.Points ? "loss" : "draw";
            e.SettledAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            await AddExp(e.GuildId, cfg.War.GuildExp.GetValueOrDefault(e.Result));
            var reward = cfg.War.Rewards.GetValueOrDefault(e.Result) ?? new();
            var fighters = await db.ArenaMatchPlayers.AsNoTracking().Where(p => p.Side == 0 && p.MembershipId != null)
                .Join(db.ArenaMatches.Where(m => m.Mode == "guildwar" && m.StageId == "gw:" + e.Id), p => p.MatchId, m => m.Id, (p, m) => p.MembershipId!.Value).Distinct().ToListAsync();
            await using var tx = await db.Database.BeginTransactionAsync();
            foreach (var f in fighters)
                foreach (var (cur, amt) in reward) await eco.Currency(f, sid, cur, amt, "guild_war", $"gwar:{e.Id}:{f}", tolerateDuplicate: true);
            await tx.CommitAsync();
            await Log(e.GuildId, null, "war_settled", new { e.Result, e.Points });
        }
    }
}
