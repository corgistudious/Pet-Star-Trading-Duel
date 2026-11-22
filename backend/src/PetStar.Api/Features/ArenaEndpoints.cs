using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using PetStar.Api.Data;
using PetStar.Api.Infrastructure;
using PetStar.Rules.Arena;

namespace PetStar.Api.Features;

public sealed class SaveArenaDeckRequest { public long? Id { get; set; } public string Name { get; set; } = ""; public bool IsDefault { get; set; } public List<string> EnergyTypes { get; set; } = new(); public List<ArenaDeckEntry> Cards { get; set; } = new(); }
public sealed class ArenaDeckChoice { public long? DeckId { get; set; } }
public sealed class ArenaPveRequest { public string StageId { get; set; } = ""; public long? DeckId { get; set; } }
public sealed class ArenaQueueRequest { public string Mode { get; set; } = "casual"; public long? DeckId { get; set; } }
public sealed class ArenaInviteRequest { public long Uid { get; set; } public long? DeckId { get; set; } }
public sealed class ArenaActionRequest { public string? ActionId { get; set; } public int Version { get; set; } public ArenaAction Action { get; set; } = new(); }

/// <summary>
/// Pet Battle Arena API (server-authoritative). Modes: PvE vs AI, Casual and Ranked matchmaking, Friend Battle, Private
/// Room. Clients send intents with an action id and the state version they saw; they receive only their own view.
/// Realtime: the WebSocket hub pushes "arena_update", "arena_match_found" and "arena_invite"; clients then GET the view.
/// </summary>
public static class ArenaEndpoints
{
    private const int MaxDecks = 12;
    private static readonly TimeSpan InviteTtl = TimeSpan.FromMinutes(2), RoomTtl = TimeSpan.FromMinutes(10);

    public static void Map(WebApplication app)
    {
        var g = ServerEndpoints.GameGroup(app);

        // ---------------------------------------------------------------- cards, collection, decks

        g.MapGet("/arena/cards", (ArenaRulesProvider rp) => rp.Rules);

        g.MapGet("/arena/collection", async (HttpContext ctx, PetStarDb db, ArenaService arena) =>
        {
            var (sid, mid) = ctx.User.Player();
            await using (var tx = await db.Database.BeginTransactionAsync()) { await arena.EnsureStarter(mid, sid); await tx.CommitAsync(); }
            var pets = await db.Cards.AsNoTracking().Where(c => c.OwnerMembershipId == mid && c.ServerId == sid)
                .Select(c => new { cardId = c.Id, defId = c.FormId, rarity = c.Rarity, busy = c.BusyState != null }).ToListAsync();
            return new { version = arena.Rules.Version, config = arena.Rules.Config, pets = pets.Where(p => arena.Rules.Has(p.defId)), trainers = await arena.OwnedTrainers(mid) };
        });

        g.MapGet("/arena/decks", async (HttpContext ctx, PetStarDb db) =>
        {
            var (_, mid) = ctx.User.Player();
            var rows = await db.ArenaDecks.AsNoTracking().Include(d => d.Cards).Where(d => d.MembershipId == mid).OrderByDescending(d => d.IsDefault).ThenBy(d => d.Id).ToListAsync();
            return rows.Select(ArenaService.ToDto);
        });

        g.MapPost("/arena/decks", async (SaveArenaDeckRequest req, HttpContext ctx, PetStarDb db, ArenaService arena) =>
        {
            var (sid, mid) = ctx.User.Player();
            await using var tx = await db.Database.BeginTransactionAsync();
            await arena.EnsureStarter(mid, sid);
            var (_, energy) = await arena.Validate(mid, sid, req.Cards, req.EnergyTypes);
            ArenaDeckRow deck;
            if (req.Id is long id)
            {
                deck = await db.ArenaDecks.Include(d => d.Cards).FirstOrDefaultAsync(d => d.Id == id && d.MembershipId == mid) ?? throw ApiException.NotFound("deck_not_found");
                db.ArenaDeckCards.RemoveRange(deck.Cards);
                deck.Cards = new();
            }
            else
            {
                if (await db.ArenaDecks.CountAsync(d => d.MembershipId == mid) >= MaxDecks) throw ApiException.Conflict("too_many_decks");
                deck = new ArenaDeckRow { MembershipId = mid, ServerId = sid };
                db.ArenaDecks.Add(deck);
            }
            bool first = !await db.ArenaDecks.AnyAsync(d => d.MembershipId == mid && d.Id != deck.Id);
            if (req.IsDefault || first) await db.ArenaDecks.Where(d => d.MembershipId == mid && d.Id != deck.Id).ExecuteUpdateAsync(u => u.SetProperty(d => d.IsDefault, false));
            var name = string.IsNullOrWhiteSpace(req.Name) ? "Deck" : req.Name.Trim()[..Math.Min(24, req.Name.Trim().Length)];
            deck.Name = name; deck.IsDefault = req.IsDefault || first; deck.EnergyTypes = string.Join(",", energy); deck.UpdatedAt = DateTimeOffset.UtcNow;
            foreach (var c in req.Cards) deck.Cards.Add(new ArenaDeckCardRow { DefId = c.DefId, CardInstanceId = c.CardInstanceId > 0 ? c.CardInstanceId : null, Count = c.Count });
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return ArenaService.ToDto(deck);
        });

        g.MapDelete("/arena/decks/{id:long}", async (long id, HttpContext ctx, PetStarDb db) =>
        {
            var (_, mid) = ctx.User.Player();
            await db.ArenaQueue.Where(q => q.DeckId == id && q.MembershipId == mid).ExecuteDeleteAsync();
            int n = await db.ArenaDecks.Where(d => d.Id == id && d.MembershipId == mid).ExecuteDeleteAsync();
            return n == 0 ? Results.NotFound() : Results.Ok(new { deleted = id });
        });

        g.MapPost("/arena/decks/auto", async (HttpContext ctx, PetStarDb db, ArenaService arena) =>
        {
            var (sid, mid) = ctx.User.Player();
            await using (var tx = await db.Database.BeginTransactionAsync()) { await arena.EnsureStarter(mid, sid); await tx.CommitAsync(); }
            var cards = await arena.AutoDeck(mid, sid);
            return new { cards, energyTypes = ArenaDecks.DefaultEnergy(arena.Rules, cards.Select(c => c.DefId)) };
        });

        // ---------------------------------------------------------------- start: PvE, matchmaking, friends, rooms

        g.MapPost("/arena/pve", (ArenaPveRequest req, HttpContext ctx, PetStarDb db, GameData data, ArenaService arena, IdempotencyService idem) =>
            idem.Run(ctx, "arena.pve", req, async () =>
            {
                var (sid, mid) = ctx.User.Player();
                var stage = data.Pve.Stages.FirstOrDefault(s => s.Id == req.StageId) ?? throw ApiException.NotFound("stage_not_found");
                if (stage.RequiresStage != null && !await db.PveProgress.AnyAsync(p => p.MembershipId == mid && p.StageId == stage.RequiresStage && p.ClearCount > 0))
                    throw ApiException.Conflict("stage_locked");
                await EnsureFree(db, mid, allowAbandonPve: true);
                var (deck, energy, _) = await arena.PlayerDeck(mid, sid, req.DeckId);
                var (aiDeck, aiEnergy) = arena.StageDeck(stage);
                var m = await arena.Start(sid, "pve", new(mid, await Nick(db, mid), deck, energy, false), new(null, "AI " + stage.Id, aiDeck, aiEnergy, true), stage.Id);
                return await arena.Dto(m, ArenaService.Load(m), mid);
            }));

        g.MapPost("/arena/queue", async (ArenaQueueRequest req, HttpContext ctx, PetStarDb db, ArenaService arena) =>
        {
            var (sid, mid) = ctx.User.Player();
            if (req.Mode is not ("casual" or "ranked")) throw ApiException.BadRequest("invalid_mode");
            await EnsureFree(db, mid, allowAbandonPve: false, queueing: true);
            var (_, _, row) = await arena.PlayerDeck(mid, sid, req.DeckId);
            int rating = 1000;
            if (req.Mode == "ranked")
            {
                var season = await RankedEndpoints.ActiveSeason(db, sid);
                rating = await db.ArenaRatings.Where(r => r.SeasonId == season.Id && r.MembershipId == mid).Select(r => (int?)r.Rating).FirstOrDefaultAsync() ?? 1000;
            }
            await db.ArenaQueue.Where(q => q.MembershipId == mid).ExecuteDeleteAsync();
            db.ArenaQueue.Add(new ArenaQueueEntry { MembershipId = mid, ServerId = sid, Mode = req.Mode, DeckId = row.Id, Rating = rating, EnqueuedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
            var matched = await ArenaMatchmaker.TryPair(ctx.RequestServices, sid, req.Mode, mid);
            return matched is Guid id ? Results.Ok(new { status = "matched", matchId = id }) : Results.Ok(new { status = "queued", mode = req.Mode });
        });

        g.MapDelete("/arena/queue", async (HttpContext ctx, PetStarDb db) =>
        {
            var (_, mid) = ctx.User.Player();
            int n = await db.ArenaQueue.Where(q => q.MembershipId == mid).ExecuteDeleteAsync();
            return new { left = n > 0 };
        });

        g.MapPost("/arena/invite", async (ArenaInviteRequest req, HttpContext ctx, PetStarDb db, ArenaService arena, RealtimeHub hub) =>
        {
            var (sid, mid) = ctx.User.Player();
            var opp = await db.Memberships.AsNoTracking().FirstOrDefaultAsync(m => m.AccountId == req.Uid && m.ServerId == sid) ?? throw ApiException.NotFound("player_not_found");
            if (opp.Id == mid) throw ApiException.BadRequest("cannot_challenge_self");
            await EnsureFree(db, mid, allowAbandonPve: false);
            var (deck, energy, row) = await arena.PlayerDeck(mid, sid, req.DeckId);
            await arena.EnsureCardVersion();
            var now = DateTimeOffset.UtcNow;
            var m = new ArenaMatch { Id = Guid.NewGuid(), ServerId = sid, Mode = "friend", Status = "invited", DefsVersion = arena.Rules.Version, CreatedAt = now, UpdatedAt = now, Deadline = now.Add(InviteTtl) };
            db.ArenaMatches.Add(m);
            db.ArenaMatchPlayers.Add(new ArenaMatchPlayer { MatchId = m.Id, Side = 0, MembershipId = mid, Name = await Nick(db, mid), DeckJson = System.Text.Json.JsonSerializer.Serialize(new { deckId = row.Id }) });
            db.ArenaMatchPlayers.Add(new ArenaMatchPlayer { MatchId = m.Id, Side = 1, MembershipId = opp.Id, Name = opp.Nickname });
            await db.SaveChangesAsync();
            await hub.ToMembers(new[] { opp.Id }, "arena_invite", new { matchId = m.Id, from = await Nick(db, mid) });
            return new { matchId = m.Id, status = m.Status, expiresAt = m.Deadline };
        });

        g.MapPost("/arena/matches/{id:guid}/accept", async (Guid id, ArenaDeckChoice req, HttpContext ctx, PetStarDb db, ArenaService arena) =>
        {
            var (sid, mid) = ctx.User.Player();
            await using var tx = await db.Database.BeginTransactionAsync();
            var m = await Lock(db, id, mid, sid);
            if (m.Status != "invited" || m.Deadline < DateTimeOffset.UtcNow) throw ApiException.Conflict("invite_expired");
            var players = await db.ArenaMatchPlayers.AsNoTracking().Where(p => p.MatchId == id).OrderBy(p => p.Side).ToListAsync();
            if (players[1].MembershipId != mid) throw ApiException.NotFound("match_not_found");
            await EnsureFree(db, mid, allowAbandonPve: false);
            long? hostDeck = System.Text.Json.JsonDocument.Parse(players[0].DeckJson).RootElement.TryGetProperty("deckId", out var d) ? d.GetInt64() : null;
            var a = await arena.PlayerDeck(players[0].MembershipId!.Value, sid, hostDeck);
            var b = await arena.PlayerDeck(mid, sid, req.DeckId);
            await arena.Start(sid, "friend", new(players[0].MembershipId, players[0].Name, a.deck, a.energy, false), new(mid, players[1].Name, b.deck, b.energy, false), existing: m);
            await tx.CommitAsync();
            var st = ArenaService.Load(m);
            await arena.Push(m, st, mid);
            return await arena.Dto(m, st, mid);
        });

        g.MapPost("/arena/matches/{id:guid}/decline", async (Guid id, HttpContext ctx, PetStarDb db) =>
        {
            var (_, mid) = ctx.User.Player();
            var mine = await db.ArenaMatchPlayers.AnyAsync(p => p.MatchId == id && p.MembershipId == mid);
            int n = mine ? await db.ArenaMatches.Where(m => m.Id == id && (m.Status == "invited" || m.Status == "waiting")).ExecuteUpdateAsync(u => u.SetProperty(m => m.Status, "declined")) : 0;
            return n == 0 ? Results.NotFound() : Results.Ok(new { declined = id });
        });

        g.MapPost("/arena/rooms", async (ArenaDeckChoice req, HttpContext ctx, PetStarDb db, ArenaService arena) =>
        {
            var (sid, mid) = ctx.User.Player();
            await EnsureFree(db, mid, allowAbandonPve: false);
            var (_, _, row) = await arena.PlayerDeck(mid, sid, req.DeckId);
            await arena.EnsureCardVersion();
            var now = DateTimeOffset.UtcNow;
            for (int attempt = 0; ; attempt++)
            {
                var m = new ArenaMatch { Id = Guid.NewGuid(), ServerId = sid, Mode = "private", Status = "waiting", RoomCode = RoomCode(), DefsVersion = arena.Rules.Version, CreatedAt = now, UpdatedAt = now, Deadline = now.Add(RoomTtl) };
                db.ArenaMatches.Add(m);
                db.ArenaMatchPlayers.Add(new ArenaMatchPlayer { MatchId = m.Id, Side = 0, MembershipId = mid, Name = await Nick(db, mid), DeckJson = System.Text.Json.JsonSerializer.Serialize(new { deckId = row.Id }) });
                try { await db.SaveChangesAsync(); return Results.Ok(new { matchId = m.Id, roomCode = m.RoomCode, expiresAt = m.Deadline }); }
                catch (DbUpdateException) when (attempt < 4) { db.ChangeTracker.Clear(); }   // code collision: try another
            }
        });

        g.MapPost("/arena/rooms/{code}/join", async (string code, ArenaDeckChoice req, HttpContext ctx, PetStarDb db, ArenaService arena) =>
        {
            var (sid, mid) = ctx.User.Player();
            await using var tx = await db.Database.BeginTransactionAsync();
            var m = await db.ArenaMatches.FromSqlRaw("SELECT * FROM arena_matches WHERE room_code = {0} AND status = 'waiting' FOR UPDATE", code.Trim().ToUpperInvariant()).FirstOrDefaultAsync();
            if (m == null || m.ServerId != sid || m.Deadline < DateTimeOffset.UtcNow) throw ApiException.NotFound("room_not_found");
            var host = await db.ArenaMatchPlayers.AsNoTracking().FirstAsync(p => p.MatchId == m.Id && p.Side == 0);
            if (host.MembershipId == mid) throw ApiException.BadRequest("cannot_join_own_room");
            await EnsureFree(db, mid, allowAbandonPve: false);
            long? hostDeck = System.Text.Json.JsonDocument.Parse(host.DeckJson).RootElement.TryGetProperty("deckId", out var d) ? d.GetInt64() : null;
            var a = await arena.PlayerDeck(host.MembershipId!.Value, sid, hostDeck);
            var b = await arena.PlayerDeck(mid, sid, req.DeckId);
            m.RoomCode = null;
            await arena.Start(sid, "private", new(host.MembershipId, host.Name, a.deck, a.energy, false), new(mid, await Nick(db, mid), b.deck, b.energy, false), existing: m);
            await tx.CommitAsync();
            var st = ArenaService.Load(m);
            await arena.Push(m, st, mid);
            return await arena.Dto(m, st, mid);
        });

        // ---------------------------------------------------------------- play, reconnect, history, replay

        g.MapGet("/arena/active", async (HttpContext ctx, PetStarDb db, ArenaService arena) =>
        {
            var (_, mid) = ctx.User.Player();
            var id = await (from p in db.ArenaMatchPlayers join m in db.ArenaMatches on p.MatchId equals m.Id
                            where p.MembershipId == mid && m.Status == "active" orderby m.CreatedAt descending select (Guid?)m.Id).FirstOrDefaultAsync();
            if (id != null) return Results.Ok(new { kind = "match", match = await Fresh(db, arena, id.Value, mid) });
            var q = await db.ArenaQueue.AsNoTracking().FirstOrDefaultAsync(x => x.MembershipId == mid);
            if (q != null) return Results.Ok(new { kind = "queue", mode = q.Mode, since = q.EnqueuedAt });
            var room = await (from p in db.ArenaMatchPlayers join m in db.ArenaMatches on p.MatchId equals m.Id
                              where p.MembershipId == mid && m.Status == "waiting" select new { m.Id, m.RoomCode, m.Deadline }).FirstOrDefaultAsync();
            if (room != null) return Results.Ok(new { kind = "room", matchId = room.Id, roomCode = room.RoomCode, expiresAt = room.Deadline });
            return Results.NoContent();
        });

        g.MapGet("/arena/matches/{id:guid}", async (Guid id, HttpContext ctx, PetStarDb db, ArenaService arena) =>
            await Fresh(db, arena, id, ctx.User.Player().membershipId));

        g.MapPost("/arena/matches/{id:guid}/actions", async (Guid id, ArenaActionRequest req, HttpContext ctx, PetStarDb db, ArenaService arena) =>
        {
            var (sid, mid) = ctx.User.Player();
            if (req.ActionId is { Length: > 64 }) throw ApiException.BadRequest("invalid_action_id");
            ArenaMatch m; ArenaState st;
            await using (var tx = await db.Database.BeginTransactionAsync())
            {
                m = await Lock(db, id, mid, sid);
                if (m.Status == "finished") throw ApiException.Conflict("match_finished");
                if (m.Status != "active") throw ApiException.Conflict("match_not_started");
                int side = await db.ArenaMatchPlayers.Where(p => p.MatchId == id && p.MembershipId == mid).Select(p => p.Side).FirstAsync();
                try
                {
                    (st, bool dup) = await arena.Act(m, side, req.ActionId, req.Version, req.Action);
                    if (dup) { await tx.CommitAsync(); return await arena.Dto(m, st, mid); }
                }
                catch (ArenaService.ArenaStaleException e)
                {
                    if (e.Saved) await tx.CommitAsync();
                    throw ApiException.Conflict("stale_state");
                }
                await db.SaveChangesAsync();
                await tx.CommitAsync();
            }
            await arena.Push(m, st, mid);
            return await arena.Dto(m, st, mid);
        });

        g.MapGet("/arena/history", async (HttpContext ctx, PetStarDb db, int? page) =>
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

        g.MapGet("/arena/matches/{id:guid}/replay", async (Guid id, HttpContext ctx, PetStarDb db, ArenaService arena) =>
        {
            var (sid, mid) = ctx.User.Player();
            var m = await db.ArenaMatches.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.ServerId == sid) ?? throw ApiException.NotFound("match_not_found");
            if (!await db.ArenaMatchPlayers.AnyAsync(p => p.MatchId == id && p.MembershipId == mid)) throw ApiException.NotFound("match_not_found");
            if (m.Status != "finished") throw ApiException.Conflict("replay_after_finish");   // replays reveal hidden cards
            return await arena.Replay(m);
        });

        g.MapGet("/arena/ratings", async (HttpContext ctx, PetStarDb db) =>
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

    private static string RoomCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";      // no 0/O/1/I
        return new string(Enumerable.Range(0, 6).Select(_ => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]).ToArray());
    }

    private static Task<string> Nick(PetStarDb db, long mid) => db.Memberships.Where(m => m.Id == mid).Select(m => m.Nickname).FirstAsync();

    /// <summary>One live match per player. An unfinished PvE match may be abandoned when starting another PvE match.</summary>
    private static async Task EnsureFree(PetStarDb db, long mid, bool allowAbandonPve, bool queueing = false)
    {
        if (!queueing && await db.ArenaQueue.AnyAsync(q => q.MembershipId == mid)) throw ApiException.Conflict("already_in_queue");
        var active = await (from p in db.ArenaMatchPlayers join m in db.ArenaMatches on p.MatchId equals m.Id
                            where p.MembershipId == mid && m.Status == "active" select m).ToListAsync();
        foreach (var m in active)
        {
            if (m.Mode == "pve" && allowAbandonPve)
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

    /// <summary>Reads a match for one player, applying expired timers first (that is how a reconnecting player catches up).</summary>
    private static async Task<ArenaViewDto> Fresh(PetStarDb db, ArenaService arena, Guid id, long mid)
    {
        var sid = await db.Memberships.Where(m => m.Id == mid).Select(m => m.ServerId).FirstAsync();
        await using var tx = await db.Database.BeginTransactionAsync();
        var m = await Lock(db, id, mid, sid);
        if (m.Status is "invited" or "waiting" or "declined" or "expired") throw ApiException.Conflict("match_not_started");
        var st = ArenaService.Load(m);
        if (m.Status == "active" && await arena.ApplyTimeouts(m, st, DateTimeOffset.UtcNow))
        {
            arena.Save(m, st, DateTimeOffset.UtcNow, false);
            if (st.Finished) await arena.Finish(m, st);
            await db.SaveChangesAsync();
        }
        await tx.CommitAsync();
        return await arena.Dto(m, st, mid);
    }
}

/// <summary>Pairs queued players (rating window widens with waiting time) and runs expired timers; every 2 s.</summary>
public sealed class ArenaMatchmaker(IServiceProvider sp, ILogger<ArenaMatchmaker> log) : BackgroundService
{
    public static TimeSpan Interval = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try { await RunOnce(sp, stop); }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning(ex, "arena pass failed"); }
            try { await Task.Delay(Interval, stop); } catch (OperationCanceledException) { }
        }
    }

    public static int Window(TimeSpan waited) => 150 + (int)Math.Min(850, waited.TotalSeconds * 10);

    /// <summary>Tries to pair one queued player (or, with mid = null, the oldest) with a compatible opponent.</summary>
    public static async Task<Guid?> TryPair(IServiceProvider services, int sid, string mode, long? mid)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PetStarDb>();
        var arena = scope.ServiceProvider.GetRequiredService<ArenaService>();
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
                var pa = await arena.PlayerDeck(first.MembershipId, sid, first.DeckId);
                var pb = await arena.PlayerDeck(second.MembershipId, sid, second.DeckId);
                int? season = mode == "ranked" ? (await RankedEndpoints.ActiveSeason(db, sid)).Id : null;
                var names = await db.Memberships.Where(m => m.Id == first.MembershipId || m.Id == second.MembershipId).ToDictionaryAsync(m => m.Id, m => m.Nickname);
                db.ArenaQueue.RemoveRange(first, second);
                var m = await arena.Start(sid, mode, new(first.MembershipId, names[first.MembershipId], pa.deck, pa.energy, false), new(second.MembershipId, names[second.MembershipId], pb.deck, pb.energy, false), seasonId: season);
                await tx.CommitAsync();
                await hub.ToMembers(new[] { first.MembershipId, second.MembershipId }, "arena_match_found", new { matchId = m.Id, mode });
                return m.Id;
            }
            catch (ApiException)
            {   // a deck became illegal while waiting (card sold, locked): drop that player from the queue
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
            var arena = scope.ServiceProvider.GetRequiredService<ArenaService>();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var m = await db.ArenaMatches.FromSqlRaw("SELECT * FROM arena_matches WHERE id = {0} FOR UPDATE SKIP LOCKED", id).FirstOrDefaultAsync(ct);
            if (m == null || m.Status != "active") continue;
            var st = ArenaService.Load(m);
            if (!await arena.ApplyTimeouts(m, st, DateTimeOffset.UtcNow)) continue;
            arena.Save(m, st, DateTimeOffset.UtcNow, false);
            if (st.Finished) await arena.Finish(m, st);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            await arena.Push(m, st);
        }
    }
}
