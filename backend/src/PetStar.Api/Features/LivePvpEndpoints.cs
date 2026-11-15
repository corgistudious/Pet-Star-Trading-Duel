using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PetStar.Api.Data;
using PetStar.Api.Infrastructure;
using PetStar.Contracts;
using PetStar.Rules.Battle;

namespace PetStar.Api.Features;

public sealed class ChallengeRequest { public long Uid { get; set; } }

/// <summary>
/// Real-time PvP between two humans. Each side submits a sealed plan per round (validated by the engine, hidden from
/// the opponent). The round resolves when both plans are in, or at the deadline (missing side → default actions).
/// Three consecutive missed rounds = forfeit (disconnect). Results are pushed over WebSocket; REST works as fallback.
/// </summary>
public static class LivePvpEndpoints
{
    public static int RoundSeconds = 30;
    public const int MaxMissedRounds = 3;
    private static readonly JsonSerializerOptions J = IdempotencyService.Json;

    public static bool IsLive(Battle b) => b.Mode.StartsWith("live_");

    public static void Map(WebApplication app)
    {
        var g = ServerEndpoints.GameGroup(app);

        g.MapPost("/pvp/challenges", async (ChallengeRequest req, HttpContext ctx, PetStarDb db, GameData data, NotificationService notes, RealtimeHub hub) =>
        {
            var (sid, mid) = ctx.User.Player();
            var other = await SocialEndpoints.Member(db, req.Uid, sid);
            if (other.Id == mid) throw ApiException.BadRequest("cannot_challenge_self");
            if (!await db.Friendships.AnyAsync(f => f.Status == "accepted" && ((f.RequesterMembershipId == mid && f.AddresseeMembershipId == other.Id) || (f.AddresseeMembershipId == mid && f.RequesterMembershipId == other.Id))))
                throw ApiException.Forbidden("not_friends");
            await RankedEndpoints.LoadDeck(db, data, mid, sid, 0, requireFree: true);
            if (await db.PvpChallenges.AnyAsync(c => c.ChallengerMembershipId == mid && c.OpponentMembershipId == other.Id && c.Status == "pending" && c.ExpiresAt > DateTimeOffset.UtcNow))
                throw ApiException.Conflict("challenge_pending");
            var c = new PvpChallenge { ServerId = sid, ChallengerMembershipId = mid, OpponentMembershipId = other.Id, CreatedAt = DateTimeOffset.UtcNow, ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(2) };
            db.PvpChallenges.Add(c);
            await db.SaveChangesAsync();
            await notes.Notify(other.Id, "pvp_challenge", new { challengeId = c.Id, uid = ctx.User.AccountId() }, "pvp_challenge:" + c.Id);
            return Results.Ok(new { challengeId = c.Id, c.ExpiresAt });
        });

        g.MapGet("/pvp/challenges", async (HttpContext ctx, PetStarDb db) =>
        {
            var (_, mid) = ctx.User.Player();
            var now = DateTimeOffset.UtcNow;
            return await db.PvpChallenges.AsNoTracking().Where(c => (c.OpponentMembershipId == mid || c.ChallengerMembershipId == mid) && c.Status == "pending" && c.ExpiresAt > now)
                .Join(db.Memberships, c => c.ChallengerMembershipId == mid ? c.OpponentMembershipId : c.ChallengerMembershipId, m => m.Id,
                    (c, m) => new { c.Id, incoming = c.OpponentMembershipId == mid, otherUid = m.AccountId, otherNickname = m.Nickname, c.ExpiresAt }).ToListAsync();
        });

        g.MapPost("/pvp/challenges/{id:long}/{decision}", async (long id, string decision, HttpContext ctx, PetStarDb db, GameData data, RealtimeHub hub) =>
        {
            var (sid, mid) = ctx.User.Player();
            await using var tx = await db.Database.BeginTransactionAsync();
            var c = await db.PvpChallenges.FromSqlRaw("SELECT * FROM pvp_challenges WHERE id = {0} AND server_id = {1} FOR UPDATE", id, sid).FirstOrDefaultAsync() ?? throw ApiException.NotFound();
            if (c.Status != "pending" || c.ExpiresAt <= DateTimeOffset.UtcNow) throw ApiException.Conflict("challenge_not_pending");
            if (decision == "cancel") { if (c.ChallengerMembershipId != mid) throw ApiException.Forbidden(); c.Status = "cancelled"; await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.Ok(new { c.Status }); }
            if (c.OpponentMembershipId != mid) throw ApiException.Forbidden();
            if (decision == "decline") { c.Status = "declined"; await db.SaveChangesAsync(); await tx.CommitAsync(); await hub.ToMembers(new[] { c.ChallengerMembershipId }, "pvp_declined", new { challengeId = id }); return Results.Ok(new { c.Status }); }
            if (decision != "accept") throw ApiException.BadRequest("invalid_decision");
            var (a, _) = await RankedEndpoints.LoadDeck(db, data, c.ChallengerMembershipId, sid, 0, requireFree: true);
            var (b, _) = await RankedEndpoints.LoadDeck(db, data, mid, sid, 1, requireFree: true);
            var battle = await Start(db, data, sid, c.ChallengerMembershipId, mid, "live_friend", a, b);
            c.Status = "accepted"; c.BattleId = battle.Id;
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            await hub.ToMembers(new[] { c.ChallengerMembershipId, mid }, "pvp_start", new { battleId = battle.Id });
            return Results.Ok(new { battleId = battle.Id });
        });

        // Casual matchmaking queue (rating window widens with waiting time). Pairing uses SKIP LOCKED so concurrent
        // joins never pair the same player twice.
        g.MapPost("/pvp/queue", async (HttpContext ctx, PetStarDb db, GameData data, RealtimeHub hub) =>
        {
            var (sid, mid) = ctx.User.Player();
            var (mine, power) = await RankedEndpoints.LoadDeck(db, data, mid, sid, 0, requireFree: true);
            var season = await RankedEndpoints.ActiveSeason(db, sid);
            int rating = (await db.RankedRecords.AsNoTracking().FirstOrDefaultAsync(r => r.SeasonId == season.Id && r.MembershipId == mid))?.Rating ?? 1000;
            var blocked = await MarketEndpoints.BlockedIds(db, mid);
            await using var tx = await db.Database.BeginTransactionAsync();
            var candidates = await db.PvpQueue.FromSqlRaw(
                "SELECT * FROM pvp_queue WHERE server_id = {0} AND membership_id <> {1} ORDER BY abs(rating - {2}), enqueued_at FOR UPDATE SKIP LOCKED LIMIT 10", sid, mid, rating).ToListAsync();
            var now = DateTimeOffset.UtcNow;
            var opp = candidates.FirstOrDefault(c => !blocked.Contains(c.MembershipId) &&
                Math.Abs(c.Rating - rating) <= 150 + 20 * (now - c.EnqueuedAt).TotalSeconds && Math.Abs(c.Power - power) <= Math.Max(400, power / 2));
            if (opp == null)
            {
                await db.Database.ExecuteSqlRawAsync(
                    "INSERT INTO pvp_queue (membership_id, server_id, rating, power, enqueued_at) VALUES ({0},{1},{2},{3},now()) ON CONFLICT (membership_id) DO UPDATE SET rating = EXCLUDED.rating, power = EXCLUDED.power",
                    mid, sid, rating, power);
                await tx.CommitAsync();
                return Results.Ok(new { status = "queued" });
            }
            await db.PvpQueue.Where(q => q.MembershipId == opp.MembershipId || q.MembershipId == mid).ExecuteDeleteAsync();
            List<BattleUnit> theirs;
            try { (theirs, _) = await RankedEndpoints.LoadDeck(db, data, opp.MembershipId, sid, 0, requireFree: true); }
            catch (ApiException) { await tx.CommitAsync(); return Results.Ok(new { status = "retry" }); }  // opponent's deck changed: dropped
            foreach (var u in mine) u.Side = 1;
            var battle = await Start(db, data, sid, opp.MembershipId, mid, "live_casual", theirs, mine);
            await tx.CommitAsync();
            await hub.ToMembers(new[] { opp.MembershipId, mid }, "pvp_start", new { battleId = battle.Id });
            return Results.Ok(new { status = "matched", battleId = battle.Id });
        });

        g.MapDelete("/pvp/queue", async (HttpContext ctx, PetStarDb db) =>
        {
            var (_, mid) = ctx.User.Player();
            await db.PvpQueue.Where(q => q.MembershipId == mid).ExecuteDeleteAsync();
            return Results.Ok(new { status = "left" });
        });

        g.MapGet("/pvp/battles/{id:guid}", async (Guid id, HttpContext ctx, PetStarDb db) =>
        {
            var (_, mid) = ctx.User.Player();
            var b = await db.Battles.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && (x.PlayerAMembershipId == mid || x.PlayerBMembershipId == mid) && x.Mode.StartsWith("live_"))
                    ?? throw ApiException.NotFound("battle_not_found");
            int side = b.PlayerAMembershipId == mid ? 0 : 1;
            var st = JsonSerializer.Deserialize<BattleState>(b.StateJson, J)!;
            var last = await db.BattleRounds.AsNoTracking().Where(r => r.BattleId == id).OrderByDescending(r => r.Round).FirstOrDefaultAsync();
            bool submitted = await db.PvpPending.AnyAsync(p => p.BattleId == id && p.Round == st.Round && p.Side == side);
            return new { battle = View(b, st, side, last == null ? new() : JsonSerializer.Deserialize<List<BattleEvent>>(last.EventsJson, J)!), submitted };
        });

        g.MapPost("/pvp/battles/{id:guid}/rounds", async (Guid id, SubmitRoundRequest req, HttpContext ctx, PetStarDb db, GameData data, RealtimeHub hub) =>
        {
            var (_, mid) = ctx.User.Player();
            await using var tx = await db.Database.BeginTransactionAsync();
            var b = await db.Battles.FromSqlRaw("SELECT * FROM battles WHERE id = {0} AND (player_a_membership_id = {1} OR player_b_membership_id = {1}) FOR UPDATE", id, mid)
                .FirstOrDefaultAsync() ?? throw ApiException.NotFound("battle_not_found");
            if (!IsLive(b)) throw ApiException.BadRequest("not_live_battle");
            if (b.Status != "active") throw ApiException.Conflict("battle_finished");
            int side = b.PlayerAMembershipId == mid ? 0 : 1;
            var st = JsonSerializer.Deserialize<BattleState>(b.StateJson, J)!;
            if (req.Round != st.Round) throw ApiException.Conflict("round_mismatch");
            var normalized = new BattleEngine(data.Catalog).Normalize(st, side, req.Actions);   // throws on illegal plans
            int n = await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO pvp_pending_actions (battle_id, round, side, actions_json, submitted_at) VALUES ({0},{1},{2},{3}::jsonb,now()) ON CONFLICT DO NOTHING",
                id, st.Round, side, JsonSerializer.Serialize(req.Actions, J));
            if (n == 0) throw ApiException.Conflict("already_submitted");
            if (side == 0) b.MissedA = 0; else b.MissedB = 0;
            await db.SaveChangesAsync();
            bool both = await db.PvpPending.CountAsync(p => p.BattleId == id && p.Round == st.Round) == 2;
            object? result = null;
            if (both) result = await Resolve(db, data, hub, b, st);
            await tx.CommitAsync();
            if (!both) await hub.ToMembers(new[] { side == 0 ? b.PlayerBMembershipId!.Value : b.PlayerAMembershipId }, "pvp_opponent_ready", new { battleId = id, round = st.Round });
            return Results.Ok(new { submitted = true, resolved = both });
        });

        g.MapPost("/pvp/battles/{id:guid}/surrender", async (Guid id, HttpContext ctx, PetStarDb db, RealtimeHub hub) =>
        {
            var (_, mid) = ctx.User.Player();
            await using var tx = await db.Database.BeginTransactionAsync();
            var b = await db.Battles.FromSqlRaw("SELECT * FROM battles WHERE id = {0} AND (player_a_membership_id = {1} OR player_b_membership_id = {1}) FOR UPDATE", id, mid)
                .FirstOrDefaultAsync() ?? throw ApiException.NotFound("battle_not_found");
            if (!IsLive(b) || b.Status != "active") throw ApiException.Conflict("battle_finished");
            var st = JsonSerializer.Deserialize<BattleState>(b.StateJson, J)!;
            var ev = new List<BattleEvent>();
            BattleEngine.Forfeit(st, b.PlayerAMembershipId == mid ? 0 : 1, "surrender", ev);
            await Finish(db, b, st, ev);
            await tx.CommitAsync();
            await Push(hub, b, st, ev);
            return Results.Ok(new { finished = true });
        });
    }

    private static async Task<Battle> Start(PetStarDb db, GameData data, int sid, long a, long bMid, string mode, List<BattleUnit> sideA, List<BattleUnit> sideB)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var x in sideA) x.Side = 0;
        foreach (var x in sideB) x.Side = 1;
        await db.Battles.Where(x => (x.PlayerAMembershipId == a || x.PlayerAMembershipId == bMid || x.PlayerBMembershipId == a || x.PlayerBMembershipId == bMid) && x.Status == "active" && x.Mode.StartsWith("live_"))
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.Status, "abandoned").SetProperty(x => x.FinishedAt, now).SetProperty(x => x.EndReason, "abandoned"));
        long seed = SecureRandomSource.NewSeed();
        var st = new BattleEngine(data.Catalog).Start(sideA, sideB, unchecked((ulong)seed));
        var battle = new Battle
        {
            Id = Guid.NewGuid(), ServerId = sid, Mode = mode, PlayerAMembershipId = a, PlayerBMembershipId = bMid, Status = "active",
            StateJson = JsonSerializer.Serialize(st, J), Seed = seed, Round = st.Round, CreatedAt = now, UpdatedAt = now, RoundDeadline = now.AddSeconds(RoundSeconds),
        };
        db.Battles.Add(battle);
        await db.SaveChangesAsync();
        return battle;
    }

    /// <summary>Resolves the current round (caller holds the battle row lock). Missing plans count as missed rounds.</summary>
    public static async Task<List<BattleEvent>> Resolve(PetStarDb db, GameData data, RealtimeHub hub, Battle b, BattleState st)
    {
        var pending = await db.PvpPending.Where(p => p.BattleId == b.Id && p.Round == st.Round).ToListAsync();
        List<PlannedAction>? Plan(int side) => pending.FirstOrDefault(p => p.Side == side) is { } p ? JsonSerializer.Deserialize<List<PlannedAction>>(p.ActionsJson, J) : null;
        var planA = Plan(0); var planB = Plan(1);
        if (planA == null) b.MissedA++;
        if (planB == null) b.MissedB++;
        int round = st.Round;
        var events = new List<BattleEvent>();
        if (b.MissedA >= MaxMissedRounds || b.MissedB >= MaxMissedRounds)
            BattleEngine.Forfeit(st, b.MissedA >= MaxMissedRounds ? 0 : 1, "disconnect", events);
        else
        {
            var engine = new BattleEngine(data.Catalog);
            try { events = engine.ResolveRound(st, planA, planB); }
            catch (BattleRuleException)
            {
                // a stored plan became illegal (e.g. unit stunned meanwhile): fall back to defaults for safety
                events = engine.ResolveRound(st, null, null);
            }
        }
        db.BattleRounds.Add(new BattleRound
        {
            BattleId = b.Id, Round = round, ActionsAJson = JsonSerializer.Serialize(planA ?? new(), J), ActionsBJson = JsonSerializer.Serialize(planB ?? new(), J),
            EventsJson = JsonSerializer.Serialize(events, J), ResolvedAt = DateTimeOffset.UtcNow,
        });
        db.PvpPending.RemoveRange(pending);
        if (st.Finished) await Finish(db, b, st, events);
        else
        {
            b.StateJson = JsonSerializer.Serialize(st, J); b.Round = st.Round; b.UpdatedAt = DateTimeOffset.UtcNow; b.RoundDeadline = DateTimeOffset.UtcNow.AddSeconds(RoundSeconds);
            await db.SaveChangesAsync();
        }
        await Push(hub, b, st, events);
        return events;
    }

    private static async Task Finish(PetStarDb db, Battle b, BattleState st, List<BattleEvent> events)
    {
        b.StateJson = JsonSerializer.Serialize(st, J); b.Round = st.Round; b.Status = "finished"; b.WinnerSide = st.Winner; b.EndReason = st.EndReason;
        b.FinishedAt = DateTimeOffset.UtcNow; b.UpdatedAt = DateTimeOffset.UtcNow; b.RoundDeadline = null;
        long? winner = st.Winner == 0 ? b.PlayerAMembershipId : st.Winner == 1 ? b.PlayerBMembershipId : null;
        db.BattleResults.Add(new BattleResult { BattleId = b.Id, WinnerMembershipId = winner, RewardsJson = "{}", At = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
    }

    private static async Task Push(RealtimeHub hub, Battle b, BattleState st, List<BattleEvent> events)
    {
        await hub.ToMembers(new[] { b.PlayerAMembershipId }, "pvp_round", View(b, st, 0, events));
        if (b.PlayerBMembershipId is long pb) await hub.ToMembers(new[] { pb }, "pvp_round", View(b, st, 1, events));
    }

    public static BattleDto View(Battle b, BattleState st, int side, List<BattleEvent> events)
    {
        var dto = BattleEndpoints.ToDto(b, st, events, null);
        dto.MySide = side;
        dto.MyEnergy = st.Energy[side];
        dto.OpponentEnergy = st.Energy[1 - side];
        return dto;
    }
}

/// <summary>Resolves live rounds whose deadline passed (missing side plays default actions), expires challenges.</summary>
public sealed class PvpTimeoutService(IServiceProvider sp, ILogger<PvpTimeoutService> log) : BackgroundService
{
    public static TimeSpan Interval = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try { await RunOnce(sp, stop); }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning(ex, "pvp timeout pass failed"); }
            try { await Task.Delay(Interval, stop); } catch (OperationCanceledException) { }
        }
    }

    public static async Task RunOnce(IServiceProvider sp, CancellationToken ct = default)
    {
        List<Guid> due;
        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PetStarDb>();
            var now = DateTimeOffset.UtcNow;
            due = await db.Battles.AsNoTracking().Where(b => b.Status == "active" && b.Mode.StartsWith("live_") && b.RoundDeadline != null && b.RoundDeadline < now)
                .Select(b => b.Id).Take(100).ToListAsync(ct);
            await db.PvpChallenges.Where(c => c.Status == "pending" && c.ExpiresAt < now).ExecuteUpdateAsync(u => u.SetProperty(c => c.Status, "expired"), ct);
            await db.PvpQueue.Where(q => q.EnqueuedAt < now.AddMinutes(-10)).ExecuteDeleteAsync(ct);
        }
        foreach (var id in due)
        {
            using var scope = sp.CreateScope();
            var s = scope.ServiceProvider;
            var db = s.GetRequiredService<PetStarDb>();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var b = await db.Battles.FromSqlRaw("SELECT * FROM battles WHERE id = {0} FOR UPDATE", id).FirstAsync(ct);
            if (b.Status != "active" || b.RoundDeadline == null || b.RoundDeadline >= DateTimeOffset.UtcNow) { await tx.RollbackAsync(ct); continue; }
            var st = JsonSerializer.Deserialize<BattleState>(b.StateJson, IdempotencyService.Json)!;
            await LivePvpEndpoints.Resolve(db, s.GetRequiredService<GameData>(), s.GetRequiredService<RealtimeHub>(), b, st);
            await tx.CommitAsync(ct);
        }
    }
}
