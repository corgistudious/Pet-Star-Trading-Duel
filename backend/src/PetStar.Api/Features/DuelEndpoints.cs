using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PetStar.Api.Data;
using PetStar.Api.Infrastructure;
using PetStar.Rules.Duel;

namespace PetStar.Api.Features;

public sealed class SaveDuelDeckRequest { public long? Id { get; set; } public string Name { get; set; } = ""; public bool IsDefault { get; set; } public List<DeckEntryJson> Entries { get; set; } = new(); }
public sealed class StartDuelPveRequest { public string StageId { get; set; } = ""; public long? DeckId { get; set; } }
public sealed class StartDuelRequest { public long? DeckId { get; set; } }
public sealed class DuelChallengeRequest { public long Uid { get; set; } public long? DeckId { get; set; } }
public sealed class DuelActionRequest { public int Version { get; set; } public DuelAction Action { get; set; } = new(); }

/// <summary>
/// Pet Card Tactical Duel API. The server owns everything: shuffling, the coin flip, every rule, AI turns, timers.
/// Clients send intents with the state version they saw; they only ever receive their own view (no opponent hand,
/// deck order, face-down cards or RNG state). PvE, Ranked and PvP use the same engine and rules.
/// </summary>
public static class DuelEndpoints
{
    private const int MaxDecks = 10;

    public static void Map(WebApplication app)
    {
        var g = ServerEndpoints.GameGroup(app);

        // ---------------------------------------------------------------- collection & 40-card decks

        g.MapGet("/duel/collection", async (HttpContext ctx, PetStarDb db, DuelService duel) =>
        {
            var (sid, mid) = ctx.User.Player();
            await using (var tx = await db.Database.BeginTransactionAsync()) { await duel.EnsureStarter(mid, sid); await tx.CommitAsync(); }
            var owned = await duel.OwnedTactics(mid);
            return new
            {
                config = duel.Rules.Config,
                tactics = duel.Rules.Tactics.Values.OrderBy(t => t.Type).ThenBy(t => t.Category).ThenBy(t => t.Id).Select(t => new { def = t, owned = owned.GetValueOrDefault(t.Id) }),
            };
        });

        g.MapGet("/duel/decks", async (HttpContext ctx, PetStarDb db) =>
        {
            var (_, mid) = ctx.User.Player();
            var rows = await db.DuelDecks.AsNoTracking().Where(d => d.MembershipId == mid).OrderByDescending(d => d.IsDefault).ThenBy(d => d.Id).ToListAsync();
            return rows.Select(d => new { d.Id, d.Name, d.IsDefault, entries = DuelService.Entries(d.EntriesJson), d.UpdatedAt });
        });

        g.MapPost("/duel/decks", async (SaveDuelDeckRequest req, HttpContext ctx, PetStarDb db, DuelService duel) =>
        {
            var (sid, mid) = ctx.User.Player();
            await using var tx = await db.Database.BeginTransactionAsync();
            await duel.EnsureStarter(mid, sid);
            await duel.BuildDeck(mid, sid, req.Entries, false);   // throws a readable code if anything is illegal
            var name = string.IsNullOrWhiteSpace(req.Name) ? "Deck" : req.Name.Trim()[..Math.Min(24, req.Name.Trim().Length)];
            DuelDeck deck;
            if (req.Id is long id) deck = await db.DuelDecks.FirstOrDefaultAsync(d => d.Id == id && d.MembershipId == mid) ?? throw ApiException.NotFound("deck_not_found");
            else
            {
                if (await db.DuelDecks.CountAsync(d => d.MembershipId == mid) >= MaxDecks) throw ApiException.Conflict("too_many_decks");
                deck = new DuelDeck { MembershipId = mid, ServerId = sid };
                db.DuelDecks.Add(deck);
            }
            bool first = !await db.DuelDecks.AnyAsync(d => d.MembershipId == mid && d.Id != deck.Id);
            if (req.IsDefault || first) await db.DuelDecks.Where(d => d.MembershipId == mid && d.Id != deck.Id).ExecuteUpdateAsync(u => u.SetProperty(d => d.IsDefault, false));
            deck.Name = name; deck.IsDefault = req.IsDefault || first; deck.EntriesJson = JsonSerializer.Serialize(req.Entries, IdempotencyService.Json); deck.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return new { deck.Id, deck.Name, deck.IsDefault, entries = req.Entries };
        });

        g.MapDelete("/duel/decks/{id:long}", async (long id, HttpContext ctx, PetStarDb db) =>
        {
            var (_, mid) = ctx.User.Player();
            int n = await db.DuelDecks.Where(d => d.Id == id && d.MembershipId == mid).ExecuteDeleteAsync();
            return n == 0 ? Results.NotFound() : Results.Ok(new { deleted = id });
        });

        // a legal 40-card deck built from the player's own strongest free Pets and Spells/Traps
        g.MapPost("/duel/decks/auto", async (HttpContext ctx, PetStarDb db, DuelService duel) =>
        {
            var (sid, mid) = ctx.User.Player();
            await using var tx = await db.Database.BeginTransactionAsync();
            await duel.EnsureStarter(mid, sid);
            var entries = await duel.AutoDeck(mid, sid);
            await tx.CommitAsync();
            return new { entries };
        });

        // ---------------------------------------------------------------- start duels

        g.MapPost("/duels/pve", (StartDuelPveRequest req, HttpContext ctx, PetStarDb db, GameData data, DuelService duel, IdempotencyService idem) =>
            idem.Run(ctx, "duels.pve", req, async () =>
            {
                var (sid, mid) = ctx.User.Player();
                var stage = data.Pve.Stages.FirstOrDefault(s => s.Id == req.StageId) ?? throw ApiException.NotFound("stage_not_found");
                if (stage.RequiresStage != null && !await db.PveProgress.AnyAsync(p => p.MembershipId == mid && p.StageId == stage.RequiresStage && p.ClearCount > 0))
                    throw ApiException.Conflict("stage_locked");
                await duel.EnsureStarter(mid, sid);
                var me = await PlayerDeck(db, duel, mid, sid, req.DeckId, false);
                var nick = await db.Memberships.Where(m => m.Id == mid).Select(m => m.Nickname).FirstAsync();
                await AbandonActive(db, mid);
                return await Start(db, duel, sid, mid, null, "duel_pve", stage.Id,
                    new DuelSetupPlayer { Name = nick, Deck = me }, new DuelSetupPlayer { Name = "AI " + stage.Id, Deck = duel.StageDeck(stage) });
            }));

        // Ranked: opponent chosen by rating; their saved default deck is played by the server AI. Growth is capped.
        g.MapPost("/duels/ranked", (StartDuelRequest req, HttpContext ctx, PetStarDb db, DuelService duel, IdempotencyService idem) =>
            idem.Run(ctx, "duels.ranked", req, async () =>
            {
                var (sid, mid) = ctx.User.Player();
                var season = await RankedEndpoints.ActiveSeason(db, sid);
                await duel.EnsureStarter(mid, sid);
                var me = await PlayerDeck(db, duel, mid, sid, req.DeckId, true);
                var myRating = await db.RankedRecords.Where(r => r.SeasonId == season.Id && r.MembershipId == mid).Select(r => (int?)r.Rating).FirstOrDefaultAsync() ?? 1000;
                var candidates = await db.DuelDecks.AsNoTracking().Where(d => d.ServerId == sid && d.IsDefault && d.MembershipId != mid).Select(d => d.MembershipId).Take(200).ToListAsync();
                var ratings = await db.RankedRecords.AsNoTracking().Where(r => r.SeasonId == season.Id && candidates.Contains(r.MembershipId)).ToDictionaryAsync(r => r.MembershipId, r => r.Rating);
                List<DuelCard>? opp = null; long oppMid = 0; string oppName = "";
                foreach (var c in candidates.OrderBy(c => Math.Abs(ratings.GetValueOrDefault(c, 1000) - myRating)).ThenBy(c => c))
                {
                    try { opp = await PlayerDeck(db, duel, c, sid, null, true); oppMid = c; oppName = await db.Memberships.Where(m => m.Id == c).Select(m => m.Nickname).FirstAsync(); break; }
                    catch (ApiException) { /* opponent's deck no longer legal: try the next one */ }
                }
                if (opp == null) throw ApiException.Conflict("no_opponent_found");
                var nick = await db.Memberships.Where(m => m.Id == mid).Select(m => m.Nickname).FirstAsync();
                await AbandonActive(db, mid);
                return await Start(db, duel, sid, mid, oppMid, "ranked", season.Id.ToString(), new DuelSetupPlayer { Name = nick, Deck = me }, new DuelSetupPlayer { Name = oppName, Deck = opp });
            }));

        // PvP: challenge a player on the same server; the duel starts when they accept (both default decks).
        g.MapPost("/duels/pvp/challenge", async (DuelChallengeRequest req, HttpContext ctx, PetStarDb db, DuelService duel, RealtimeHub hub) =>
        {
            var (sid, mid) = ctx.User.Player();
            var opp = await db.Memberships.AsNoTracking().FirstOrDefaultAsync(m => m.AccountId == req.Uid && m.ServerId == sid) ?? throw ApiException.NotFound("player_not_found");
            if (opp.Id == mid) throw ApiException.BadRequest("cannot_challenge_self");
            await PlayerDeck(db, duel, mid, sid, req.DeckId, false);     // validate my deck now
            var now = DateTimeOffset.UtcNow;
            var b = new Battle
            {
                Id = Guid.NewGuid(), ServerId = sid, Mode = "live_duel", PlayerAMembershipId = mid, PlayerBMembershipId = opp.Id, Status = "pending", IsDuel = true,
                StateJson = JsonSerializer.Serialize(new { deckId = req.DeckId }), CreatedAt = now, UpdatedAt = now, RoundDeadline = now.AddMinutes(2),
            };
            db.Battles.Add(b);
            await db.SaveChangesAsync();
            await hub.ToMembers(new[] { opp.Id }, "duel_challenge", new { duelId = b.Id, from = await db.Memberships.Where(m => m.Id == mid).Select(m => m.Nickname).FirstAsync() });
            return new { duelId = b.Id, status = b.Status, expiresAt = b.RoundDeadline };
        });

        g.MapPost("/duels/{id:guid}/accept", async (Guid id, StartDuelRequest req, HttpContext ctx, PetStarDb db, DuelService duel, RealtimeHub hub) =>
        {
            var (sid, mid) = ctx.User.Player();
            await using var tx = await db.Database.BeginTransactionAsync();
            var b = await db.Battles.FromSqlRaw("SELECT * FROM battles WHERE id = {0} FOR UPDATE", id).FirstOrDefaultAsync();
            if (b == null || !b.IsDuel || b.PlayerBMembershipId != mid || b.ServerId != sid) throw ApiException.NotFound("duel_not_found");
            if (b.Status != "pending" || b.RoundDeadline < DateTimeOffset.UtcNow) throw ApiException.Conflict("challenge_expired");
            long? challengerDeck = JsonDocument.Parse(b.StateJson).RootElement.TryGetProperty("deckId", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetInt64() : null;
            var a = await PlayerDeck(db, duel, b.PlayerAMembershipId, sid, challengerDeck, false);
            var me = await PlayerDeck(db, duel, mid, sid, req.DeckId, false);
            var names = await db.Memberships.Where(m => m.Id == b.PlayerAMembershipId || m.Id == mid).ToDictionaryAsync(m => m.Id, m => m.Nickname);
            var st = DuelEngine.Create(duel.Rules, new DuelSetupPlayer { Name = names[b.PlayerAMembershipId], Deck = a }, new DuelSetupPlayer { Name = names[mid], Deck = me },
                unchecked((ulong)SecureRandomSource.NewSeed()));
            b.Status = "active";
            duel.Save(b, st, DateTimeOffset.UtcNow, resetDeadline: true);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            await hub.ToMembers(new[] { b.PlayerAMembershipId }, "duel_update", new { duelId = b.Id, version = st.Version });
            return await duel.Dto(b, st, mid);
        });

        g.MapPost("/duels/{id:guid}/decline", async (Guid id, HttpContext ctx, PetStarDb db) =>
        {
            var (_, mid) = ctx.User.Player();
            int n = await db.Battles.Where(b => b.Id == id && b.IsDuel && b.Status == "pending" && (b.PlayerBMembershipId == mid || b.PlayerAMembershipId == mid))
                .ExecuteUpdateAsync(u => u.SetProperty(b => b.Status, "declined"));
            return n == 0 ? Results.NotFound() : Results.Ok(new { declined = id });
        });

        // ---------------------------------------------------------------- play, reconnect

        // reconnect: the duel the player is currently in (any device)
        g.MapGet("/duels/active", async (HttpContext ctx, PetStarDb db, DuelService duel, AchievementService ach) =>
        {
            var (_, mid) = ctx.User.Player();
            var id = await db.Battles.AsNoTracking().Where(b => b.IsDuel && b.Status == "active" && (b.PlayerAMembershipId == mid || b.PlayerBMembershipId == mid))
                .OrderByDescending(b => b.CreatedAt).Select(b => (Guid?)b.Id).FirstOrDefaultAsync();
            return id == null ? Results.NoContent() : Results.Ok(await Fresh(db, duel, ach, id.Value, mid));
        });

        g.MapGet("/duels/{id:guid}", async (Guid id, HttpContext ctx, PetStarDb db, DuelService duel, AchievementService ach) =>
            await Fresh(db, duel, ach, id, ctx.User.Player().membershipId));

        g.MapPost("/duels/{id:guid}/actions", async (Guid id, DuelActionRequest req, HttpContext ctx, PetStarDb db, DuelService duel, AchievementService ach, RealtimeHub hub) =>
        {
            var (sid, mid) = ctx.User.Player();
            await using var tx = await db.Database.BeginTransactionAsync();
            var b = await Lock(db, id, mid, sid);
            if (b.Status != "active") throw ApiException.Conflict("duel_finished");
            var st = DuelService.Load(b);
            var now = DateTimeOffset.UtcNow;
            bool timedOut = duel.ApplyTimeouts(b, st, now);
            int side = b.PlayerAMembershipId == mid ? 0 : 1;
            if (!st.Finished)
            {
                if (req.Action.Type != "surrender" && req.Version != st.Version)
                {   // stale screen / double tap: save any timeout progress, then refuse
                    if (timedOut) { duel.Save(b, st, now, false); if (st.Finished) await duel.Finish(b, st, ach); await db.SaveChangesAsync(); await tx.CommitAsync(); }
                    throw ApiException.Conflict("stale_state");
                }
                try { DuelEngine.Apply(duel.Rules, st, side, req.Action); }
                catch (DuelRuleException e) { throw ApiException.Conflict(e.Code); }
                st.Players[side].Timeouts = 0;
                duel.RunAi(b, st);
            }
            duel.Save(b, st, now, resetDeadline: true);
            if (st.Finished) await duel.Finish(b, st, ach);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            long? other = side == 0 ? b.PlayerBMembershipId : b.PlayerAMembershipId;
            if (b.Mode == "live_duel" && other != null) await hub.ToMembers(new[] { other.Value }, "duel_update", new { duelId = b.Id, version = st.Version });
            return await duel.Dto(b, st, mid);
        });
    }

    private static async Task<Battle> Lock(PetStarDb db, Guid id, long mid, int sid)
    {
        var b = await db.Battles.FromSqlRaw("SELECT * FROM battles WHERE id = {0} FOR UPDATE", id).FirstOrDefaultAsync();
        if (b == null || !b.IsDuel || b.ServerId != sid || (b.PlayerAMembershipId != mid && b.PlayerBMembershipId != mid)) throw ApiException.NotFound("duel_not_found");
        return b;
    }

    /// <summary>Reads a duel for one side, applying expired timers first (that is how a reconnecting player catches up).</summary>
    private static async Task<DuelViewDto> Fresh(PetStarDb db, DuelService duel, AchievementService ach, Guid id, long mid)
    {
        var sid = await db.Memberships.Where(m => m.Id == mid).Select(m => m.ServerId).FirstAsync();
        await using var tx = await db.Database.BeginTransactionAsync();
        var b = await Lock(db, id, mid, sid);
        if (b.Status == "pending" || b.Status == "declined" || b.Status == "abandoned" && b.StateJson.Length < 20) throw ApiException.Conflict("duel_not_started");
        var st = DuelService.Load(b);
        if (b.Status == "active" && duel.ApplyTimeouts(b, st, DateTimeOffset.UtcNow))
        {
            duel.Save(b, st, DateTimeOffset.UtcNow, false);
            if (st.Finished) await duel.Finish(b, st, ach);
            await db.SaveChangesAsync();
        }
        await tx.CommitAsync();
        return await duel.Dto(b, st, mid);
    }

    private static async Task<List<DuelCard>> PlayerDeck(PetStarDb db, DuelService duel, long mid, int sid, long? deckId, bool ranked)
    {
        var deck = await db.DuelDecks.AsNoTracking().Where(d => d.MembershipId == mid && (deckId == null ? d.IsDefault : d.Id == deckId)).FirstOrDefaultAsync()
                   ?? throw ApiException.Conflict("no_duel_deck");
        return await duel.BuildDeck(mid, sid, DuelService.Entries(deck.EntriesJson), ranked);
    }

    private static async Task AbandonActive(PetStarDb db, long mid)
    {
        var now = DateTimeOffset.UtcNow;
        await db.Battles.Where(b => b.IsDuel && b.Status == "active" && b.PlayerAMembershipId == mid && b.Mode != "live_duel")
            .ExecuteUpdateAsync(u => u.SetProperty(b => b.Status, "abandoned").SetProperty(b => b.FinishedAt, now).SetProperty(b => b.EndReason, "abandoned"));
    }

    private static async Task<DuelViewDto> Start(PetStarDb db, DuelService duel, int sid, long mid, long? opp, string mode, string stageId, DuelSetupPlayer a, DuelSetupPlayer b2)
    {
        var st = DuelEngine.Create(duel.Rules, a, b2, unchecked((ulong)SecureRandomSource.NewSeed()));
        var now = DateTimeOffset.UtcNow;
        var b = new Battle { Id = Guid.NewGuid(), ServerId = sid, Mode = mode, StageId = stageId, PlayerAMembershipId = mid, PlayerBMembershipId = opp, Status = "active", IsDuel = true, CreatedAt = now };
        duel.RunAi(b, st);                     // the AI may have won the coin toss
        duel.Save(b, st, now, resetDeadline: true);
        db.Battles.Add(b);
        await db.SaveChangesAsync();
        return await duel.Dto(b, st, mid);
    }
}

/// <summary>Applies expired duel timers in the background (PvP opponents that left, idle players), every 2 s.</summary>
public sealed class DuelTimeoutService(IServiceProvider sp, ILogger<DuelTimeoutService> log) : BackgroundService
{
    public static TimeSpan Interval = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try { await RunOnce(sp, stop); }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning(ex, "duel timeout pass failed"); }
            try { await Task.Delay(Interval, stop); } catch (OperationCanceledException) { }
        }
    }

    public static async Task RunOnce(IServiceProvider sp, CancellationToken ct = default)
    {
        List<Guid> due;
        var now = DateTimeOffset.UtcNow;
        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PetStarDb>();
            due = await db.Battles.AsNoTracking().Where(b => b.IsDuel && b.Status == "active" && b.RoundDeadline != null && b.RoundDeadline < now).Select(b => b.Id).Take(100).ToListAsync(ct);
            await db.Battles.Where(b => b.IsDuel && b.Status == "pending" && b.RoundDeadline < now).ExecuteUpdateAsync(u => u.SetProperty(b => b.Status, "expired"), ct);
        }
        foreach (var id in due)
        {
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PetStarDb>();
            var duel = scope.ServiceProvider.GetRequiredService<DuelService>();
            var ach = scope.ServiceProvider.GetRequiredService<AchievementService>();
            var hub = scope.ServiceProvider.GetRequiredService<RealtimeHub>();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var b = await db.Battles.FromSqlRaw("SELECT * FROM battles WHERE id = {0} FOR UPDATE SKIP LOCKED", id).FirstOrDefaultAsync(ct);
            if (b == null || b.Status != "active") continue;
            var st = DuelService.Load(b);
            if (!duel.ApplyTimeouts(b, st, DateTimeOffset.UtcNow)) continue;
            duel.Save(b, st, DateTimeOffset.UtcNow, false);
            if (st.Finished) await duel.Finish(b, st, ach);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            var members = new[] { b.PlayerAMembershipId, b.PlayerBMembershipId ?? 0 }.Where(m => m > 0);
            await hub.ToMembers(members, "duel_update", new { duelId = b.Id, version = st.Version });
        }
    }
}
