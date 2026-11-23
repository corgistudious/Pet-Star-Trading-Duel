using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PetStar.Api.Data;
using PetStar.Api.Infrastructure;
using PetStar.Rules.Team;

namespace PetStar.Api.Features;

public sealed class SaveTeamRequest { public long? Id { get; set; } public string Name { get; set; } = ""; public bool IsDefault { get; set; } public List<long> CardIds { get; set; } = new(); }
public sealed class TeamChoice { public long? LineupId { get; set; } }
public sealed class TeamPveRequest { public string StageId { get; set; } = ""; public long? LineupId { get; set; } public string Kind { get; set; } = "pve"; }
public sealed class TeamQueueRequest { public string Mode { get; set; } = "casual"; public long? LineupId { get; set; } }
public sealed class TeamInviteRequest { public long Uid { get; set; } public long? LineupId { get; set; } }
public sealed class TeamActionRequest { public string? ActionId { get; set; } public int Version { get; set; } public TeamAction Action { get; set; } = new(); }

/// <summary>
/// Battle 3v3 API (server-authoritative). Modes: PvE, Boss, Practice (vs AI), Casual and Ranked matchmaking, Friend
/// Battle, Private Room. Each round both players lock hidden orders (with an action id and the state version they saw);
/// the server resolves and returns each player's filtered view. Realtime pushes: team_update, team_match_found, team_invite.
/// </summary>
public static class TeamEndpoints
{
    private const int MaxTeams = 10;
    private static readonly TimeSpan InviteTtl = TimeSpan.FromMinutes(2), RoomTtl = TimeSpan.FromMinutes(10);

    public static void Map(WebApplication app)
    {
        var g = ServerEndpoints.GameGroup(app);

        g.MapGet("/team/rules", (TeamRulesProvider rp) => rp.Rules);

        // ---------------------------------------------------------------- teams (3 owned Pet cards)

        g.MapGet("/team/lineups", async (HttpContext ctx, PetStarDb db) =>
        {
            var (_, mid) = ctx.User.Player();
            return await db.TeamLineups.AsNoTracking().Where(l => l.MembershipId == mid).OrderByDescending(l => l.IsDefault).ThenBy(l => l.Id)
                .Select(l => new { l.Id, l.Name, l.IsDefault, cardIds = new[] { l.Card1Id, l.Card2Id, l.Card3Id } }).ToListAsync();
        });

        g.MapPost("/team/lineups", async (SaveTeamRequest req, HttpContext ctx, PetStarDb db, TeamService team) =>
        {
            var (sid, mid) = ctx.User.Player();
            var units = await team.BuildTeam(mid, sid, req.CardIds, false);   // ownership, locks, 3 different cards
            await using var tx = await db.Database.BeginTransactionAsync();
            TeamLineup row;
            if (req.Id is long id) row = await db.TeamLineups.FirstOrDefaultAsync(l => l.Id == id && l.MembershipId == mid) ?? throw ApiException.NotFound("team_not_found");
            else
            {
                if (await db.TeamLineups.CountAsync(l => l.MembershipId == mid) >= MaxTeams) throw ApiException.Conflict("too_many_teams");
                row = new TeamLineup { MembershipId = mid, ServerId = sid };
                db.TeamLineups.Add(row);
            }
            bool first = !await db.TeamLineups.AnyAsync(l => l.MembershipId == mid && l.Id != row.Id);
            if (req.IsDefault || first) await db.TeamLineups.Where(l => l.MembershipId == mid && l.Id != row.Id).ExecuteUpdateAsync(u => u.SetProperty(l => l.IsDefault, false));
            row.Name = string.IsNullOrWhiteSpace(req.Name) ? "Team" : req.Name.Trim()[..Math.Min(24, req.Name.Trim().Length)];
            row.IsDefault = req.IsDefault || first; row.Card1Id = req.CardIds[0]; row.Card2Id = req.CardIds[1]; row.Card3Id = req.CardIds[2]; row.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return new { row.Id, row.Name, row.IsDefault, cardIds = row.CardIds, preview = units };
        });

        g.MapDelete("/team/lineups/{id:long}", async (long id, HttpContext ctx, PetStarDb db) =>
        {
            var (_, mid) = ctx.User.Player();
            await db.ArenaQueue.Where(q => q.LineupId == id && q.MembershipId == mid).ExecuteDeleteAsync();
            int n = await db.TeamLineups.Where(l => l.Id == id && l.MembershipId == mid).ExecuteDeleteAsync();
            return n == 0 ? Results.NotFound() : Results.Ok(new { deleted = id });
        });

        // ---------------------------------------------------------------- start: PvE / Boss / Practice, matchmaking, friends, rooms

        g.MapPost("/team/pve", (TeamPveRequest req, HttpContext ctx, PetStarDb db, GameData data, TeamService team, IdempotencyService idem) =>
            idem.Run(ctx, "team.pve", req, async () =>
            {
                var (sid, mid) = ctx.User.Player();
                if (req.Kind is not ("pve" or "boss" or "practice")) throw ApiException.BadRequest("invalid_mode");
                var stage = data.Pve.Stages.FirstOrDefault(s => s.Id == req.StageId) ?? throw ApiException.NotFound("stage_not_found");
                if (stage.RequiresStage != null && !await db.PveProgress.AnyAsync(p => p.MembershipId == mid && p.StageId == stage.RequiresStage && p.ClearCount > 0))
                    throw ApiException.Conflict("stage_locked");
                await EnsureFree(db, mid, allowAbandonPve: true);
                var (units, _) = await team.PlayerTeam(mid, sid, req.LineupId);
                var ai = req.Kind == "boss" ? team.StageTeam(stage, 250) : team.StageTeam(stage);
                var m = await team.Start(sid, req.Kind, new(mid, await Nick(db, mid), units, false), new(null, (req.Kind == "boss" ? "Boss " : "AI ") + stage.Id, ai, true), req.Kind == "practice" ? null : stage.Id);
                return await team.Dto(m, TeamService.Load(m), mid);
            }));

        g.MapPost("/team/queue", async (TeamQueueRequest req, HttpContext ctx, PetStarDb db, TeamService team) =>
        {
            var (sid, mid) = ctx.User.Player();
            if (req.Mode is not ("casual" or "ranked")) throw ApiException.BadRequest("invalid_mode");
            await EnsureFree(db, mid, allowAbandonPve: false, queueing: true);
            var (_, row) = await team.PlayerTeam(mid, sid, req.LineupId, req.Mode == "ranked");
            int rating = 1000;
            if (req.Mode == "ranked")
            {
                var season = await RankedEndpoints.ActiveSeason(db, sid);
                rating = await db.ArenaRatings.Where(r => r.SeasonId == season.Id && r.MembershipId == mid).Select(r => (int?)r.Rating).FirstOrDefaultAsync() ?? 1000;
            }
            await db.ArenaQueue.Where(q => q.MembershipId == mid).ExecuteDeleteAsync();
            db.ArenaQueue.Add(new ArenaQueueEntry { MembershipId = mid, ServerId = sid, Mode = req.Mode, LineupId = row.Id, Rating = rating, EnqueuedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
            var matched = await TeamMatchmaker.TryPair(ctx.RequestServices, sid, req.Mode, mid);
            return matched is Guid id ? Results.Ok(new { status = "matched", matchId = id }) : Results.Ok(new { status = "queued", mode = req.Mode });
        });

        g.MapDelete("/team/queue", async (HttpContext ctx, PetStarDb db) =>
        {
            var (_, mid) = ctx.User.Player();
            return new { left = await db.ArenaQueue.Where(q => q.MembershipId == mid).ExecuteDeleteAsync() > 0 };
        });

        g.MapPost("/team/invite", async (TeamInviteRequest req, HttpContext ctx, PetStarDb db, TeamService team, RealtimeHub hub) =>
        {
            var (sid, mid) = ctx.User.Player();
            var opp = await db.Memberships.AsNoTracking().FirstOrDefaultAsync(m => m.AccountId == req.Uid && m.ServerId == sid) ?? throw ApiException.NotFound("player_not_found");
            if (opp.Id == mid) throw ApiException.BadRequest("cannot_challenge_self");
            await EnsureFree(db, mid, allowAbandonPve: false);
            var (_, row) = await team.PlayerTeam(mid, sid, req.LineupId);
            await team.EnsureDefsVersion();
            var now = DateTimeOffset.UtcNow;
            var m = new ArenaMatch { Id = Guid.NewGuid(), ServerId = sid, Mode = "friend", Status = "invited", DefsVersion = team.Rules.Version, CreatedAt = now, UpdatedAt = now, Deadline = now.Add(InviteTtl) };
            db.ArenaMatches.Add(m);
            db.ArenaMatchPlayers.Add(new ArenaMatchPlayer { MatchId = m.Id, Side = 0, MembershipId = mid, Name = await Nick(db, mid), DeckJson = JsonSerializer.Serialize(new { lineupId = row.Id }) });
            db.ArenaMatchPlayers.Add(new ArenaMatchPlayer { MatchId = m.Id, Side = 1, MembershipId = opp.Id, Name = opp.Nickname });
            await db.SaveChangesAsync();
            await hub.ToMembers(new[] { opp.Id }, "team_invite", new { matchId = m.Id, from = await Nick(db, mid) });
            return new { matchId = m.Id, status = m.Status, expiresAt = m.Deadline };
        });

        g.MapPost("/team/matches/{id:guid}/accept", async (Guid id, TeamChoice req, HttpContext ctx, PetStarDb db, TeamService team) =>
        {
            var (sid, mid) = ctx.User.Player();
            await using var tx = await db.Database.BeginTransactionAsync();
            var m = await Lock(db, id, mid, sid);
            if (m.Status != "invited" || m.Deadline < DateTimeOffset.UtcNow) throw ApiException.Conflict("invite_expired");
            var players = await db.ArenaMatchPlayers.AsNoTracking().Where(p => p.MatchId == id).OrderBy(p => p.Side).ToListAsync();
            if (players[1].MembershipId != mid) throw ApiException.NotFound("match_not_found");
            await EnsureFree(db, mid, allowAbandonPve: false);
            var a = await team.PlayerTeam(players[0].MembershipId!.Value, sid, HostLineup(players[0]));
            var b = await team.PlayerTeam(mid, sid, req.LineupId);
            await team.Start(sid, "friend", new(players[0].MembershipId, players[0].Name, a.units, false), new(mid, players[1].Name, b.units, false), existing: m);
            await tx.CommitAsync();
            var st = TeamService.Load(m);
            await team.Push(m, st, mid);
            return await team.Dto(m, st, mid);
        });

        g.MapPost("/team/matches/{id:guid}/decline", async (Guid id, HttpContext ctx, PetStarDb db) =>
        {
            var (_, mid) = ctx.User.Player();
            var mine = await db.ArenaMatchPlayers.AnyAsync(p => p.MatchId == id && p.MembershipId == mid);
            int n = mine ? await db.ArenaMatches.Where(m => m.Id == id && (m.Status == "invited" || m.Status == "waiting")).ExecuteUpdateAsync(u => u.SetProperty(m => m.Status, "declined")) : 0;
            return n == 0 ? Results.NotFound() : Results.Ok(new { declined = id });
        });

        g.MapPost("/team/rooms", async (TeamChoice req, HttpContext ctx, PetStarDb db, TeamService team) =>
        {
            var (sid, mid) = ctx.User.Player();
            await EnsureFree(db, mid, allowAbandonPve: false);
            var (_, row) = await team.PlayerTeam(mid, sid, req.LineupId);
            await team.EnsureDefsVersion();
            var now = DateTimeOffset.UtcNow;
            for (int attempt = 0; ; attempt++)
            {
                var m = new ArenaMatch { Id = Guid.NewGuid(), ServerId = sid, Mode = "private", Status = "waiting", RoomCode = RoomCode(), DefsVersion = team.Rules.Version, CreatedAt = now, UpdatedAt = now, Deadline = now.Add(RoomTtl) };
                db.ArenaMatches.Add(m);
                db.ArenaMatchPlayers.Add(new ArenaMatchPlayer { MatchId = m.Id, Side = 0, MembershipId = mid, Name = await Nick(db, mid), DeckJson = JsonSerializer.Serialize(new { lineupId = row.Id }) });
                try { await db.SaveChangesAsync(); return Results.Ok(new { matchId = m.Id, roomCode = m.RoomCode, expiresAt = m.Deadline }); }
                catch (DbUpdateException) when (attempt < 4) { db.ChangeTracker.Clear(); }
            }
        });

        g.MapPost("/team/rooms/{code}/join", async (string code, TeamChoice req, HttpContext ctx, PetStarDb db, TeamService team) =>
        {
            var (sid, mid) = ctx.User.Player();
            await using var tx = await db.Database.BeginTransactionAsync();
            var m = await db.ArenaMatches.FromSqlRaw("SELECT * FROM arena_matches WHERE room_code = {0} AND status = 'waiting' FOR UPDATE", code.Trim().ToUpperInvariant()).FirstOrDefaultAsync();
            if (m == null || m.ServerId != sid || m.Deadline < DateTimeOffset.UtcNow) throw ApiException.NotFound("room_not_found");
            var host = await db.ArenaMatchPlayers.AsNoTracking().FirstAsync(p => p.MatchId == m.Id && p.Side == 0);
            if (host.MembershipId == mid) throw ApiException.BadRequest("cannot_join_own_room");
            await EnsureFree(db, mid, allowAbandonPve: false);
            var a = await team.PlayerTeam(host.MembershipId!.Value, sid, HostLineup(host));
            var b = await team.PlayerTeam(mid, sid, req.LineupId);
            m.RoomCode = null;
            await team.Start(sid, "private", new(host.MembershipId, host.Name, a.units, false), new(mid, await Nick(db, mid), b.units, false), existing: m);
            await tx.CommitAsync();
            var st = TeamService.Load(m);
            await team.Push(m, st, mid);
            return await team.Dto(m, st, mid);
        });

        // ---------------------------------------------------------------- play, reconnect, history, replay, ratings

        g.MapGet("/team/active", async (HttpContext ctx, PetStarDb db, TeamService team) =>
        {
            var (_, mid) = ctx.User.Player();
            var id = await (from p in db.ArenaMatchPlayers join m in db.ArenaMatches on p.MatchId equals m.Id
                            where p.MembershipId == mid && m.Status == "active" orderby m.CreatedAt descending select (Guid?)m.Id).FirstOrDefaultAsync();
            if (id != null) return Results.Ok(new { kind = "match", match = await Fresh(db, team, id.Value, mid) });
            var q = await db.ArenaQueue.AsNoTracking().FirstOrDefaultAsync(x => x.MembershipId == mid);
            if (q != null) return Results.Ok(new { kind = "queue", mode = q.Mode, since = q.EnqueuedAt });
            var room = await (from p in db.ArenaMatchPlayers join m in db.ArenaMatches on p.MatchId equals m.Id
                              where p.MembershipId == mid && m.Status == "waiting" select new { m.Id, m.RoomCode, m.Deadline }).FirstOrDefaultAsync();
            if (room != null) return Results.Ok(new { kind = "room", matchId = room.Id, roomCode = room.RoomCode, expiresAt = room.Deadline });
            return Results.NoContent();
        });

        g.MapGet("/team/matches/{id:guid}", async (Guid id, HttpContext ctx, PetStarDb db, TeamService team) => await Fresh(db, team, id, ctx.User.Player().membershipId));

        g.MapPost("/team/matches/{id:guid}/actions", async (Guid id, TeamActionRequest req, HttpContext ctx, PetStarDb db, TeamService team) =>
        {
            var (sid, mid) = ctx.User.Player();
            if (req.ActionId is { Length: > 64 }) throw ApiException.BadRequest("invalid_action_id");
            ArenaMatch m; TeamState st;
            await using (var tx = await db.Database.BeginTransactionAsync())
            {
                m = await Lock(db, id, mid, sid);
                if (m.Status == "finished") throw ApiException.Conflict("match_finished");
                if (m.Status != "active") throw ApiException.Conflict("match_not_started");
                int side = await db.ArenaMatchPlayers.Where(p => p.MatchId == id && p.MembershipId == mid).Select(p => p.Side).FirstAsync();
                try
                {
                    (st, bool dup) = await team.Act(m, side, req.ActionId, req.Version, req.Action);
                    if (dup) { await tx.CommitAsync(); return await team.Dto(m, st, mid); }
                }
                catch (TeamService.StaleException e) { if (e.Saved) await tx.CommitAsync(); throw ApiException.Conflict("stale_state"); }
                await db.SaveChangesAsync();
                await tx.CommitAsync();
            }
            await team.Push(m, st, mid);
            return await team.Dto(m, st, mid);
        });

        g.MapGet("/team/history", async (HttpContext ctx, PetStarDb db, int? page) =>
        {
            var (_, mid) = ctx.User.Player();
            int pg = Math.Max(1, page ?? 1);
            var rows = await (from p in db.ArenaMatchPlayers join m in db.ArenaMatches on p.MatchId equals m.Id
                              where p.MembershipId == mid && m.Status == "finished"
                              orderby m.FinishedAt descending
                              select new { m.Id, m.Mode, m.WinnerSide, m.EndReason, m.FinishedAt, p.Side, p.RatingBefore, p.RatingAfter,
                                  Opponent = db.ArenaMatchPlayers.Where(o => o.MatchId == m.Id && o.Side != p.Side).Select(o => o.Name).FirstOrDefault() })
                .Skip((pg - 1) * 20).Take(20).ToListAsync();
            return rows.Select(r => new { matchId = r.Id, r.Mode, result = r.WinnerSide == null ? "draw" : r.WinnerSide == r.Side ? "win" : "loss", r.EndReason, r.FinishedAt, r.Opponent, r.RatingBefore, r.RatingAfter });
        });

        g.MapGet("/team/matches/{id:guid}/replay", async (Guid id, HttpContext ctx, PetStarDb db, TeamService team) =>
        {
            var (sid, mid) = ctx.User.Player();
            var m = await db.ArenaMatches.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.ServerId == sid) ?? throw ApiException.NotFound("match_not_found");
            if (!await db.ArenaMatchPlayers.AnyAsync(p => p.MatchId == id && p.MembershipId == mid)) throw ApiException.NotFound("match_not_found");
            if (m.Status != "finished") throw ApiException.Conflict("replay_after_finish");
            return await team.Replay(m);
        });

        g.MapGet("/team/ratings", async (HttpContext ctx, PetStarDb db) =>
        {
            var (sid, mid) = ctx.User.Player();
            var season = await RankedEndpoints.ActiveSeason(db, sid);
            var top = await (from r in db.ArenaRatings join m in db.Memberships on r.MembershipId equals m.Id
                             where r.SeasonId == season.Id orderby r.Rating descending, r.Wins descending
                             select new { m.Nickname, r.Rating, r.Wins, r.Losses, r.Draws }).Take(50).ToListAsync();
            var me = await db.ArenaRatings.AsNoTracking().FirstOrDefaultAsync(r => r.SeasonId == season.Id && r.MembershipId == mid);
            return new { season = new { season.Id, season.Name, season.EndsAt }, me = me == null ? null : new { me.Rating, me.Wins, me.Losses, me.Draws }, top };
        });
    }

    private static long? HostLineup(ArenaMatchPlayer p) =>
        JsonDocument.Parse(p.DeckJson).RootElement.TryGetProperty("lineupId", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetInt64() : null;

    private static string RoomCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        return new string(Enumerable.Range(0, 6).Select(_ => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]).ToArray());
    }

    private static Task<string> Nick(PetStarDb db, long mid) => db.Memberships.Where(m => m.Id == mid).Select(m => m.Nickname).FirstAsync();

    private static async Task EnsureFree(PetStarDb db, long mid, bool allowAbandonPve, bool queueing = false)
    {
        if (!queueing && await db.ArenaQueue.AnyAsync(q => q.MembershipId == mid)) throw ApiException.Conflict("already_in_queue");
        var active = await (from p in db.ArenaMatchPlayers join m in db.ArenaMatches on p.MatchId equals m.Id where p.MembershipId == mid && m.Status == "active" select m).ToListAsync();
        foreach (var m in active)
        {
            if ((m.Mode is "pve" or "boss" or "practice") && allowAbandonPve)
                await db.ArenaMatches.Where(x => x.Id == m.Id && x.Status == "active").ExecuteUpdateAsync(u => u.SetProperty(x => x.Status, "abandoned").SetProperty(x => x.EndReason, "abandoned").SetProperty(x => x.FinishedAt, DateTimeOffset.UtcNow));
            else throw ApiException.Conflict("already_in_match");
        }
    }

    internal static async Task<ArenaMatch> Lock(PetStarDb db, Guid id, long mid, int sid)
    {
        var m = await db.ArenaMatches.FromSqlRaw("SELECT * FROM arena_matches WHERE id = {0} FOR UPDATE", id).FirstOrDefaultAsync();
        if (m == null || m.ServerId != sid || !await db.ArenaMatchPlayers.AnyAsync(p => p.MatchId == id && p.MembershipId == mid)) throw ApiException.NotFound("match_not_found");
        return m;
    }

    private static async Task<TeamViewDto> Fresh(PetStarDb db, TeamService team, Guid id, long mid)
    {
        var sid = await db.Memberships.Where(m => m.Id == mid).Select(m => m.ServerId).FirstAsync();
        await using var tx = await db.Database.BeginTransactionAsync();
        var m = await Lock(db, id, mid, sid);
        if (m.Status is "invited" or "waiting" or "declined" or "expired") throw ApiException.Conflict("match_not_started");
        var st = TeamService.Load(m);
        if (m.Status == "active" && await team.ApplyTimeouts(m, st, DateTimeOffset.UtcNow))
        {
            team.Save(m, st, DateTimeOffset.UtcNow, false, Array.Empty<int>());
            if (st.Finished) await team.Finish(m, st);
            await db.SaveChangesAsync();
        }
        await tx.CommitAsync();
        return await team.Dto(m, st, mid);
    }
}

/// <summary>Pairs queued players (rating window widens with waiting time) and runs expired plan timers; every 2 s.</summary>
public sealed class TeamMatchmaker(IServiceProvider sp, ILogger<TeamMatchmaker> log) : BackgroundService
{
    public static TimeSpan Interval = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try { await RunOnce(sp, stop); }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning(ex, "team pass failed"); }
            try { await Task.Delay(Interval, stop); } catch (OperationCanceledException) { }
        }
    }

    public static int Window(TimeSpan waited) => 150 + (int)Math.Min(850, waited.TotalSeconds * 10);

    public static async Task<Guid?> TryPair(IServiceProvider services, int sid, string mode, long? mid)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PetStarDb>();
        var team = scope.ServiceProvider.GetRequiredService<TeamService>();
        var hub = scope.ServiceProvider.GetRequiredService<RealtimeHub>();
        await using var tx = await db.Database.BeginTransactionAsync();
        var queue = await db.ArenaQueue.FromSqlRaw("SELECT * FROM arena_queue WHERE server_id = {0} AND mode = {1} ORDER BY enqueued_at FOR UPDATE SKIP LOCKED", sid, mode).ToListAsync();
        var now = DateTimeOffset.UtcNow;
        foreach (var a in queue.Where(q => mid == null || q.MembershipId == mid))
        {
            var b = queue.Where(q => q.MembershipId != a.MembershipId && (mode == "casual" || Math.Abs(q.Rating - a.Rating) <= Math.Max(Window(now - a.EnqueuedAt), Window(now - q.EnqueuedAt))))
                .OrderBy(q => Math.Abs(q.Rating - a.Rating)).ThenBy(q => q.EnqueuedAt).FirstOrDefault();
            if (b == null) continue;
            var first = a.EnqueuedAt <= b.EnqueuedAt ? a : b; var second = first == a ? b : a;
            try
            {
                bool ranked = mode == "ranked";
                var pa = await team.PlayerTeam(first.MembershipId, sid, first.LineupId, ranked);
                var pb = await team.PlayerTeam(second.MembershipId, sid, second.LineupId, ranked);
                int? season = ranked ? (await RankedEndpoints.ActiveSeason(db, sid)).Id : null;
                var names = await db.Memberships.Where(m => m.Id == first.MembershipId || m.Id == second.MembershipId).ToDictionaryAsync(m => m.Id, m => m.Nickname);
                db.ArenaQueue.RemoveRange(first, second);
                var m = await team.Start(sid, mode, new(first.MembershipId, names[first.MembershipId], pa.units, false), new(second.MembershipId, names[second.MembershipId], pb.units, false), seasonId: season);
                await tx.CommitAsync();
                await hub.ToMembers(new[] { first.MembershipId, second.MembershipId }, "team_match_found", new { matchId = m.Id, mode });
                return m.Id;
            }
            catch (ApiException)
            {
                db.ChangeTracker.Clear();
                await db.ArenaQueue.Where(q => q.MembershipId == first.MembershipId || q.MembershipId == second.MembershipId).ExecuteDeleteAsync();
                await tx.CommitAsync();
                return null;
            }
        }
        await tx.CommitAsync();
        return null;
    }

    public static async Task RunOnce(IServiceProvider sp, CancellationToken ct = default)
    {
        List<(int sid, string mode)> queues; List<Guid> due;
        var now = DateTimeOffset.UtcNow;
        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PetStarDb>();
            queues = (await db.ArenaQueue.AsNoTracking().Select(q => new { q.ServerId, q.Mode }).Distinct().ToListAsync(ct)).Select(x => (x.ServerId, x.Mode)).ToList();
            due = await db.ArenaMatches.AsNoTracking().Where(m => m.Status == "active" && m.Deadline != null && m.Deadline < now).Select(m => m.Id).Take(100).ToListAsync(ct);
            await db.ArenaMatches.Where(m => (m.Status == "invited" || m.Status == "waiting") && m.Deadline < now)
                .ExecuteUpdateAsync(u => u.SetProperty(m => m.Status, "expired").SetProperty(m => m.RoomCode, (string?)null), ct);
        }
        foreach (var (sid, mode) in queues)
            for (int i = 0; i < 20; i++) if (await TryPair(sp, sid, mode, null) == null) break;
        foreach (var id in due)
        {
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PetStarDb>();
            var team = scope.ServiceProvider.GetRequiredService<TeamService>();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var m = await db.ArenaMatches.FromSqlRaw("SELECT * FROM arena_matches WHERE id = {0} FOR UPDATE SKIP LOCKED", id).FirstOrDefaultAsync(ct);
            if (m == null || m.Status != "active") continue;
            var st = TeamService.Load(m);
            if (!await team.ApplyTimeouts(m, st, DateTimeOffset.UtcNow)) continue;
            team.Save(m, st, DateTimeOffset.UtcNow, false, Array.Empty<int>());
            if (st.Finished) await team.Finish(m, st);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            await team.Push(m, st);
        }
    }
}
