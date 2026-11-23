using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PetStar.Api.Data;
using PetStar.Api.Features;
using PetStar.Rules;
using PetStar.Rules.Team;

namespace PetStar.Api.Infrastructure;

public sealed class TrainerCatalog
{
    public sealed class Character { public string Id { get; set; } = ""; public Dictionary<string, string> Name { get; set; } = new(); public string DefaultOutfit { get; set; } = ""; public string DefaultAppearance { get; set; } = "default"; }
    public sealed class Outfit { public string Id { get; set; } = ""; public string CharacterId { get; set; } = ""; public Dictionary<string, string> Name { get; set; } = new(); public string Source { get; set; } = ""; public object? StatBonus { get; set; } }
    public string ChangePolicy { get; set; } = "locked";
    public List<Character> Characters { get; set; } = new();
    public List<Outfit> Outfits { get; set; } = new();
    public static TrainerCatalog Load(string dir) =>
        JsonSerializer.Deserialize<TrainerCatalog>(File.ReadAllText(Path.Combine(dir, "trainers.json")), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
}

public sealed class TeamRulesProvider(TeamRules rules, TrainerCatalog trainers)
{
    public TeamRules Rules { get; } = rules;
    public TrainerCatalog Trainers { get; } = trainers;
}

public sealed record TeamViewDto(Guid Id, string Mode, string Status, int You, int Version, DateTimeOffset? DeadlineAt, DateTimeOffset ServerNow,
    string? OpponentName, TeamState State, string? RoomCode, string? RewardsJson);

/// <summary>
/// Server side of Battle 3v3: teams built only from the player's own free Pet cards (stats computed here, never sent by
/// clients), the authoritative engine, AI sides, simultaneous hidden plans, plan timers, an append-only action log with
/// idempotent action ids, snapshots, filtered views, replays, ratings and rewards. Reuses the match tables of the
/// first match server (arena_matches, arena_events, ...).
/// </summary>
public sealed class TeamService(PetStarDb db, GameData data, TeamRulesProvider rp, EconomyService eco, AchievementService ach, RealtimeHub hub)
{
    private static readonly JsonSerializerOptions J = IdempotencyService.Json;
    public TeamRules Rules => rp.Rules;
    public const int SnapshotEvery = 10;

    // ------------------------------------------------------------------ teams

    public async Task<List<TeamSetupUnit>> BuildTeam(long mid, int sid, IReadOnlyList<long> cardIds, bool ranked)
    {
        if (cardIds.Count != Rules.Config.TeamSize || cardIds.Distinct().Count() != cardIds.Count) throw ApiException.Conflict("team_needs_3_different_pets");
        var cards = await db.Cards.AsNoTracking().Where(c => cardIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id);
        foreach (var id in cardIds)
        {
            if (!cards.TryGetValue(id, out var c) || c.OwnerMembershipId != mid || c.ServerId != sid) throw ApiException.Conflict("team_card_not_owned");
            if (c.BusyState != null) throw ApiException.Conflict("team_card_busy");
            if (!Rules.Kits.ContainsKey(c.FormId)) throw ApiException.Conflict("unknown_pet");
        }
        var engr = await CardProgressLoader.Engravings(db, cards.Keys);
        return cardIds.Select(id =>
        {
            var c = cards[id];
            return TeamBuilder.Unit(data.Catalog, Rules, c.FormId, c.Rarity, c.Quality, CardProgressLoader.Progress(c, engr.GetValueOrDefault(c.Id)), c.Id, ranked ? 145 : null);
        }).ToList();
    }

    public async Task<(List<TeamSetupUnit> units, TeamLineup row)> PlayerTeam(long mid, int sid, long? lineupId, bool ranked = false)
    {
        var row = await db.TeamLineups.AsNoTracking().Where(l => l.MembershipId == mid && (lineupId == null ? l.IsDefault : l.Id == lineupId)).FirstOrDefaultAsync()
                  ?? throw ApiException.Conflict("no_team");
        return (await BuildTeam(mid, sid, row.CardIds, ranked), row);
    }

    /// <summary>PvE team from the stage's enemies (first three, with their stage levels).</summary>
    public List<TeamSetupUnit> StageTeam(PveStage stage, int bossBonusPct = 100)
    {
        var enemies = stage.Enemies.Where(e => Rules.Kits.ContainsKey(e.FormId)).Take(3).ToList();
        while (enemies.Count < 3) enemies.Add(enemies[enemies.Count % Math.Max(1, enemies.Count)]);
        return enemies.Select((e, i) =>
        {
            var u = TeamBuilder.Unit(data.Catalog, Rules, e.FormId, e.Rarity, e.Quality, new CardProgress { Level = Math.Max(1, e.Level) }, 0);
            if (bossBonusPct != 100 && i == 0) { u.Hp = u.Hp * bossBonusPct / 100; u.Atk = u.Atk * (100 + (bossBonusPct - 100) / 4) / 100; u.Mag = u.Mag * (100 + (bossBonusPct - 100) / 4) / 100; }
            return u;
        }).ToList();
    }

    public async Task EnsureDefsVersion()
    {
        if (await db.ArenaCardVersions.AnyAsync(v => v.Version == Rules.Version)) return;
        db.ArenaCardVersions.Add(new ArenaCardVersion { Version = Rules.Version, DefinitionsJson = JsonSerializer.Serialize(Rules, J), CreatedAt = DateTimeOffset.UtcNow });
        try { await db.SaveChangesAsync(); } catch (DbUpdateException) { db.ChangeTracker.Clear(); }
    }

    // ------------------------------------------------------------------ matches

    public sealed record Side(long? Mid, string Name, List<TeamSetupUnit> Units, bool Ai);
    public sealed class ReplayTeam { public string Name { get; set; } = ""; public List<TeamSetupUnit> Units { get; set; } = new(); }

    public async Task<ArenaMatch> Start(int sid, string mode, Side a, Side b, string? stageId = null, int? seasonId = null, ArenaMatch? existing = null)
    {
        await EnsureDefsVersion();
        long seed = SecureRandomSource.NewSeed();
        var st = TeamEngine.Create(Rules, new TeamSetupPlayer { Name = a.Name, Units = a.Units }, new TeamSetupPlayer { Name = b.Name, Units = b.Units }, unchecked((ulong)seed));
        var now = DateTimeOffset.UtcNow;
        var m = existing ?? new ArenaMatch { Id = Guid.NewGuid(), ServerId = sid, Mode = mode, CreatedAt = now };
        m.Status = "active"; m.DefsVersion = Rules.Version; m.Seed = seed; m.StageId = stageId; m.SeasonId = seasonId; m.StartedAt = now; m.Mode = mode;
        if (existing == null) db.ArenaMatches.Add(m);
        else await db.ArenaMatchPlayers.Where(p => p.MatchId == m.Id).ExecuteDeleteAsync();
        foreach (var (s, side) in new[] { (a, 0), (b, 1) })
            db.ArenaMatchPlayers.Add(new ArenaMatchPlayer { MatchId = m.Id, Side = side, MembershipId = s.Mid, Name = s.Name, IsAi = s.Ai, DeckJson = JsonSerializer.Serialize(new ReplayTeam { Name = s.Name, Units = s.Units }, J) });
        await db.SaveChangesAsync();
        await RunAi(m, st);
        Save(m, st, now, true, await AiSides(m));
        await db.SaveChangesAsync();
        return m;
    }

    public static TeamState Load(ArenaMatch m) => JsonSerializer.Deserialize<TeamState>(m.StateJson, J)!;
    private async Task<int[]> AiSides(ArenaMatch m) => (await db.ArenaMatchPlayers.AsNoTracking().Where(p => p.MatchId == m.Id && p.IsAi).Select(p => p.Side).ToListAsync()).ToArray();

    private void Record(ArenaMatch m, int side, string kind, object action, int versionAfter, string? actionId = null)
    {
        m.EventSeq++;
        db.ArenaEvents.Add(new ArenaEventRow { MatchId = m.Id, Seq = m.EventSeq, Side = side, Kind = kind, ActionId = actionId, ActionJson = JsonSerializer.Serialize(action, J), VersionAfter = versionAfter, At = DateTimeOffset.UtcNow });
    }

    /// <summary>AI sides lock their plan as soon as a new round opens (each AI plan is logged like a player's).</summary>
    public async Task RunAi(ArenaMatch m, TeamState st)
    {
        var ai = await AiSides(m);
        for (int guard = 0; guard < 100 && !st.Finished; guard++)
        {
            int side = ai.FirstOrDefault(s => TeamEngine.MustAct(st, s), -1);
            if (side < 0) break;
            var a = TeamAi.Decide(Rules, st, side);
            TeamEngine.Apply(Rules, st, side, a);
            Record(m, side, "action", a, st.Version);
        }
    }

    public DateTimeOffset? Deadline(TeamState st, int[] ai, DateTimeOffset from) =>
        st.Finished || !Enumerable.Range(0, 2).Any(q => TeamEngine.MustAct(st, q) && !ai.Contains(q)) ? null : from.AddSeconds(Rules.Config.PlanSeconds);

    public void Save(ArenaMatch m, TeamState st, DateTimeOffset now, bool resetDeadline, int[] ai)
    {
        if (resetDeadline) m.Deadline = Deadline(st, ai, now);
        m.StateJson = JsonSerializer.Serialize(st, J);
        m.Version = st.Version; m.UpdatedAt = now;
        if (m.EventSeq > 0 && (m.EventSeq % SnapshotEvery == 0 || st.Finished) && !db.ArenaSnapshots.Local.Any(s => s.MatchId == m.Id && s.Seq == m.EventSeq))
            db.ArenaSnapshots.Add(new ArenaSnapshot { MatchId = m.Id, Seq = m.EventSeq, StateJson = m.StateJson, At = now });
    }

    public async Task<bool> ApplyTimeouts(ArenaMatch m, TeamState st, DateTimeOffset now)
    {
        bool changed = false; int guard = 0;
        var ai = await AiSides(m);
        while (!st.Finished && m.Deadline is DateTimeOffset dl && dl < now && guard++ < 12)
        {
            TeamEngine.Timeout(Rules, st);
            Record(m, -1, "timeout", new { }, st.Version);
            await RunAi(m, st);
            m.Deadline = Deadline(st, ai, dl);
            changed = true;
        }
        return changed;
    }

    public sealed class StaleException(bool saved) : Exception("stale_state") { public bool Saved { get; } = saved; }

    public async Task<(TeamState st, bool duplicate)> Act(ArenaMatch m, int side, string? actionId, int version, TeamAction action)
    {
        var st = Load(m);
        var now = DateTimeOffset.UtcNow;
        if (!string.IsNullOrEmpty(actionId) && await db.ArenaEvents.AnyAsync(e => e.MatchId == m.Id && e.ActionId == actionId)) return (st, true);
        bool timedOut = await ApplyTimeouts(m, st, now);
        var ai = await AiSides(m);
        if (!st.Finished)
        {
            if (action.Type != "surrender" && version != st.Version)
            {
                if (timedOut) { Save(m, st, now, false, ai); if (st.Finished) await Finish(m, st); await db.SaveChangesAsync(); }
                throw new StaleException(timedOut);
            }
            try { TeamEngine.Apply(Rules, st, side, action); }
            catch (TeamRuleException e) { throw ApiException.Conflict(e.Code); }
            Record(m, side, "action", action, st.Version, actionId);
            await RunAi(m, st);
        }
        Save(m, st, now, true, ai);
        if (st.Finished) await Finish(m, st);
        return (st, false);
    }

    /// <summary>What one side may see: the opponent's plan stays hidden until the round resolves; RNG state is removed.</summary>
    public static TeamState View(TeamState st, int side)
    {
        var v = JsonSerializer.Deserialize<TeamState>(JsonSerializer.Serialize(st, J), J)!;
        v.Rng = 0;
        var opp = v.Players[1 - side];
        if (opp.Plan != null) opp.Plan = new List<TeamOrder>();
        return v;
    }

    public async Task<TeamViewDto> Dto(ArenaMatch m, TeamState st, long mid)
    {
        var players = await db.ArenaMatchPlayers.AsNoTracking().Where(p => p.MatchId == m.Id).ToListAsync();
        int you = players.FirstOrDefault(p => p.MembershipId == mid)?.Side ?? 0;
        var opp = players.FirstOrDefault(p => p.Side == 1 - you)?.Name;
        var rewards = m.Status == "finished" ? await db.ArenaRewards.AsNoTracking().Where(r => r.MatchId == m.Id && r.MembershipId == mid).Select(r => r.RewardsJson).FirstOrDefaultAsync() : null;
        return new TeamViewDto(m.Id, m.Mode, m.Status, you, st.Version, m.Deadline, DateTimeOffset.UtcNow, opp, View(st, you), m.RoomCode, rewards);
    }

    public async Task Push(ArenaMatch m, TeamState st, long? except = null)
    {
        var humans = await db.ArenaMatchPlayers.AsNoTracking().Where(p => p.MatchId == m.Id && p.MembershipId != null).Select(p => p.MembershipId!.Value).ToListAsync();
        var to = humans.Where(h => h != except).ToList();
        if (to.Count > 0) await hub.ToMembers(to, "team_update", new { matchId = m.Id, version = st.Version, status = m.Status, round = st.Round });
    }

    // ------------------------------------------------------------------ results

    public async Task Finish(ArenaMatch m, TeamState st)
    {
        if (m.Status == "finished") return;
        m.Status = "finished"; m.WinnerSide = st.Winner; m.EndReason = st.EndReason; m.FinishedAt = DateTimeOffset.UtcNow; m.Deadline = null;
        var players = await db.ArenaMatchPlayers.Where(p => p.MatchId == m.Id).OrderBy(p => p.Side).ToListAsync();
        if (m.Mode == "ranked" && m.SeasonId is int season && players.All(p => p.MembershipId != null))
        {
            var r = new ArenaRating[2];
            for (int s = 0; s < 2; s++)
            {
                long mid = players[s].MembershipId!.Value;
                r[s] = await db.ArenaRatings.FirstOrDefaultAsync(x => x.SeasonId == season && x.MembershipId == mid)
                       ?? db.ArenaRatings.Add(new ArenaRating { SeasonId = season, MembershipId = mid, ServerId = m.ServerId }).Entity;
            }
            double ea = 1.0 / (1 + Math.Pow(10, (r[1].Rating - r[0].Rating) / 400.0));
            double sa = st.Winner == 0 ? 1 : st.Winner == 1 ? 0 : 0.5;
            int delta = (int)Math.Round(32 * (sa - ea));
            for (int s = 0; s < 2; s++)
            {
                players[s].RatingBefore = r[s].Rating;
                r[s].Rating = Math.Max(0, r[s].Rating + (s == 0 ? delta : -delta));
                players[s].RatingAfter = r[s].Rating;
                if (st.Winner == null) r[s].Draws++; else if (st.Winner == s) r[s].Wins++; else r[s].Losses++;
                r[s].UpdatedAt = DateTimeOffset.UtcNow;
            }
        }
        foreach (var p in players.Where(p => p.MembershipId != null))
        {
            long mid = p.MembershipId!.Value;
            object rewards = new { };
            if ((m.Mode == "pve" || m.Mode == "boss") && st.Winner == p.Side && m.StageId != null)
                rewards = await PveRewards(m, mid, data.Pve.Stages.First(s => s.Id == m.StageId));
            db.ArenaRewards.Add(new ArenaReward { MatchId = m.Id, MembershipId = mid, RewardsJson = JsonSerializer.Serialize(new { result = st.Winner == null ? "draw" : st.Winner == p.Side ? "win" : "loss", ratingBefore = p.RatingBefore, ratingAfter = p.RatingAfter, rewards }, J), At = DateTimeOffset.UtcNow });
        }
        await db.SaveChangesAsync();
        if (st.Winner is int w && players[w].MembershipId is long winner) await ach.Evaluate(winner, m.ServerId, "duel_win");
    }

    /// <summary>Stage rewards for a PvE/Boss win: the existing stage tables and the existing daily cap (shared counter).
    /// Practice matches pay nothing.</summary>
    private async Task<Dictionary<string, object>> PveRewards(ArenaMatch m, long mid, PveStage stage)
    {
        var res = new Dictionary<string, object>();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var p = await db.PveProgress.FirstOrDefaultAsync(x => x.MembershipId == mid && x.StageId == stage.Id);
        if (p == null) { p = new PveProgress { MembershipId = mid, StageId = stage.Id, DailyDate = today }; db.PveProgress.Add(p); }
        if (p.DailyDate != today) { p.DailyDate = today; p.DailyRewardedClears = 0; }
        bool first = p.ClearCount == 0;
        p.ClearCount++; p.FirstClearedAt ??= DateTimeOffset.UtcNow;
        var grants = new List<PveRewards>();
        if (p.DailyRewardedClears < data.Pve.DailyRewardedClearsPerStage) { grants.Add(stage.Rewards); p.DailyRewardedClears++; } else res["dailyCapReached"] = true;
        if (first) { grants.Add(stage.FirstClearRewards); res["firstClear"] = true; }
        var currencies = new Dictionary<string, long>(); var items = new Dictionary<string, long>(); var packs = new Dictionary<string, long>();
        foreach (var (gr, idx) in grants.Select((g, i) => (g, i)))
        {
            string r = $"team:{m.Id:N}:{idx}";
            foreach (var (cur, amt) in gr.Currencies) { await eco.Currency(mid, m.ServerId, cur, amt, "team_pve_reward", r); currencies[cur] = currencies.GetValueOrDefault(cur) + amt; }
            foreach (var (item, amt) in gr.Items) { await eco.Item(mid, m.ServerId, item, amt, "team_pve_reward", r); items[item] = items.GetValueOrDefault(item) + amt; }
            foreach (var (pack, amt) in gr.Packs) { await eco.Packs(mid, m.ServerId, pack, amt, "team_pve_reward", r); packs[pack] = packs.GetValueOrDefault(pack) + amt; }
        }
        res["currencies"] = currencies; res["items"] = items; res["packs"] = packs;
        return res;
    }

    // ------------------------------------------------------------------ replay

    public sealed class ReplayDto
    {
        public Guid MatchId { get; set; }
        public string Mode { get; set; } = "";
        public string DefsVersion { get; set; } = "";
        public long Seed { get; set; }
        public List<ReplayTeam> Teams { get; set; } = new();
        public List<ReplayEvent> Events { get; set; } = new();
        public int? Winner { get; set; }
        public string? EndReason { get; set; }
    }
    public sealed class ReplayEvent { public int Seq { get; set; } public int Side { get; set; } public string Kind { get; set; } = ""; public TeamAction Action { get; set; } = new(); }

    public async Task<ReplayDto> Replay(ArenaMatch m)
    {
        var players = await db.ArenaMatchPlayers.AsNoTracking().Where(p => p.MatchId == m.Id).OrderBy(p => p.Side).ToListAsync();
        var events = await db.ArenaEvents.AsNoTracking().Where(e => e.MatchId == m.Id).OrderBy(e => e.Seq).ToListAsync();
        return new ReplayDto
        {
            MatchId = m.Id, Mode = m.Mode, DefsVersion = m.DefsVersion, Seed = m.Seed, Winner = m.WinnerSide, EndReason = m.EndReason,
            Teams = players.Select(p => JsonSerializer.Deserialize<ReplayTeam>(p.DeckJson, J)!).ToList(),
            Events = events.Select(e => new ReplayEvent { Seq = e.Seq, Side = e.Side, Kind = e.Kind, Action = e.Kind == "timeout" ? new TeamAction() : JsonSerializer.Deserialize<TeamAction>(e.ActionJson, J)! }).ToList(),
        };
    }

    /// <summary>Rebuilds a match from its replay with the battle data of its own version (not today's).</summary>
    public async Task<TeamState> Rebuild(ReplayDto r)
    {
        var defs = await db.ArenaCardVersions.AsNoTracking().Where(v => v.Version == r.DefsVersion).Select(v => v.DefinitionsJson).FirstAsync();
        var rules = JsonSerializer.Deserialize<TeamRules>(defs, J)!;
        var st = TeamEngine.Create(rules, new TeamSetupPlayer { Name = r.Teams[0].Name, Units = r.Teams[0].Units }, new TeamSetupPlayer { Name = r.Teams[1].Name, Units = r.Teams[1].Units }, unchecked((ulong)r.Seed));
        foreach (var e in r.Events)
        {
            if (e.Kind == "timeout") TeamEngine.Timeout(rules, st);
            else TeamEngine.Apply(rules, st, e.Side, e.Action);
        }
        return st;
    }
}
