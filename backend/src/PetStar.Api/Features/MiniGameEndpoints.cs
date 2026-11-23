using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PetStar.Api.Data;
using PetStar.Api.Infrastructure;
using PetStar.Rules;

namespace PetStar.Api.Features;

public sealed class MiniGameFinishRequest { public List<int>? Moves { get; set; } public List<int>? Taps { get; set; } public List<int>? Answers { get; set; } }

/// <summary>Mini games with server-authoritative scoring: the server creates the board from its own seed, keeps the
/// clock, replays the submitted moves/taps/answers and computes the score. Scores sent by a client are never trusted.</summary>
public static class MiniGameEndpoints
{
    public static readonly string[] Games = { "memory", "puzzle", "sprint", "quiz", "daily" };
    public const int RewardedPerDay = 3, SprintMs = 20_000, MinTapGapMs = 55, StartsPerDay = 40, Pairs = 8, QuizCount = 5;

    public static void Map(WebApplication app)
    {
        var g = ServerEndpoints.GameGroup(app);

        g.MapGet("/minigames", async (HttpContext ctx, PetStarDb db) =>
        {
            var (_, mid) = ctx.User.Player();
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var best = await db.MiniGameSessions.AsNoTracking().Where(s => s.MembershipId == mid && s.Score != null).GroupBy(s => s.Game).Select(x => new { game = x.Key, best = x.Max(s => s.Score) }).ToListAsync();
            int rewarded = await db.MiniGameSessions.CountAsync(s => s.MembershipId == mid && s.Day == today && s.Rewarded && s.Game != "daily");
            bool dailyDone = await db.MiniGameSessions.AnyAsync(s => s.MembershipId == mid && s.Day == today && s.Game == "daily" && s.Score != null);
            var history = await db.MiniGameSessions.AsNoTracking().Where(s => s.MembershipId == mid && s.FinishedAt != null).OrderByDescending(s => s.FinishedAt).Take(20)
                .Select(s => new { s.Game, s.Score, s.Rewarded, s.RewardJson, s.FinishedAt, s.Rejected }).ToListAsync();
            return new { games = Games, best, rewardedToday = rewarded, rewardedPerDay = RewardedPerDay, dailyDone, history };
        });

        g.MapPost("/minigames/{game}/start", async (string game, HttpContext ctx, PetStarDb db, GameData data) =>
        {
            var (sid, mid) = ctx.User.Player();
            if (!Games.Contains(game)) throw ApiException.BadRequest("unknown_game");
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            if (await db.MiniGameSessions.CountAsync(s => s.MembershipId == mid && s.Day == today) >= StartsPerDay) throw new ApiException(429, "minigame_daily_limit");
            if (game == "daily" && await db.MiniGameSessions.AnyAsync(s => s.MembershipId == mid && s.Day == today && s.Game == "daily" && s.Score != null)) throw ApiException.Conflict("daily_already_played");
            // daily challenge: the same board for everyone today
            long seed = game == "daily" ? DaySeed(today) : SecureRandomSource.NewSeed();
            var s = new MiniGameSession { Id = Guid.NewGuid(), MembershipId = mid, ServerId = sid, Game = game, Seed = seed, Day = today, StartedAt = DateTimeOffset.UtcNow };
            db.MiniGameSessions.Add(s);
            await db.SaveChangesAsync();
            return Results.Ok(new { sessionId = s.Id, game, payload = Payload(game, seed, data) });
        });

        g.MapPost("/minigames/sessions/{id:guid}/finish", async (Guid id, MiniGameFinishRequest req, HttpContext ctx, PetStarDb db, GameData data, EconomyService eco) =>
        {
            var (sid, mid) = ctx.User.Player();
            await using var tx = await db.Database.BeginTransactionAsync();
            var s = await db.MiniGameSessions.FromSqlRaw("SELECT * FROM minigame_sessions WHERE id = {0} FOR UPDATE", id).FirstOrDefaultAsync();
            if (s == null || s.MembershipId != mid) throw ApiException.NotFound("session_not_found");
            if (s.FinishedAt != null) return Results.Ok(new { score = s.Score, rewarded = s.Rewarded, reward = JsonDocument.Parse(s.RewardJson).RootElement, replay = true });
            var now = DateTimeOffset.UtcNow;
            double secs = (now - s.StartedAt).TotalSeconds;
            s.FinishedAt = now;
            int score;
            try
            {
                if (secs > 15 * 60) throw ApiException.Conflict("session_expired");
                score = Score(s.Game, s.Seed, req, secs, data);
            }
            catch (ApiException e)
            {
                s.Rejected = e.Code; await db.SaveChangesAsync(); await tx.CommitAsync();
                throw;
            }
            s.Score = score;
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            bool canReward = s.Game == "daily"
                ? !await db.MiniGameSessions.AnyAsync(x => x.MembershipId == mid && x.Day == today && x.Game == "daily" && x.Rewarded)
                : await db.MiniGameSessions.CountAsync(x => x.MembershipId == mid && x.Day == today && x.Rewarded && x.Game != "daily") < RewardedPerDay;
            long beans = 0;
            if (canReward && score > 0)
            {
                beans = s.Game == "daily" ? Math.Clamp(score / 20, 10, 60) : Math.Clamp(score / 25, 5, 40);
                await eco.Currency(mid, sid, Rules.Currency.MiniGreenBean, beans, "minigame_reward", "mg:" + s.Id);
                s.Rewarded = true;
            }
            s.RewardJson = JsonSerializer.Serialize(new Dictionary<string, long> { [Rules.Currency.MiniGreenBean] = beans });
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return Results.Ok(new { score, seconds = Math.Round(secs, 1), rewarded = s.Rewarded, reward = new Dictionary<string, long> { [Rules.Currency.MiniGreenBean] = beans }, replay = false });
        });
    }

    public static long DaySeed(DateOnly d) => d.DayNumber * 2654435761L ^ 0x5EED_2026L;

    private static List<string> FormPool(GameData data) =>
        data.Catalog.Families.SelectMany(f => f.Forms).Where(f => f.Stage == 1).Select(f => f.Id).OrderBy(x => x, StringComparer.Ordinal).ToList();

    public static List<string> MemoryBoard(long seed, GameData data)
    {
        var rng = new SeededRandom(unchecked((ulong)seed));
        var pool = FormPool(data);
        var chosen = new List<string>();
        while (chosen.Count < Pairs) { var f = pool[rng.Next(pool.Count)]; if (!chosen.Contains(f)) chosen.Add(f); }
        var board = chosen.Concat(chosen).ToList();
        for (int i = board.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (board[i], board[j]) = (board[j], board[i]); }
        return board;
    }

    /// <summary>3x3 sliding puzzle, scrambled by legal moves from solved (always solvable). 0 = blank.</summary>
    public static int[] PuzzleStart(long seed)
    {
        var rng = new SeededRandom(unchecked((ulong)seed));
        var t = new[] { 1, 2, 3, 4, 5, 6, 7, 8, 0 };
        int last = -1;
        for (int k = 0; k < 80; k++)
        {
            int b = Array.IndexOf(t, 0);
            var opts = Neighbours(b).Where(n => n != last).ToList();
            int pick = opts[rng.Next(opts.Count)];
            (t[b], t[pick]) = (t[pick], t[b]); last = b;
        }
        if (t.SequenceEqual(new[] { 1, 2, 3, 4, 5, 6, 7, 8, 0 })) { int b = Array.IndexOf(t, 0); int n = Neighbours(b).First(); (t[b], t[n]) = (t[n], t[b]); }
        return t;
    }
    private static IEnumerable<int> Neighbours(int i) { int r = i / 3, c = i % 3; if (r > 0) yield return i - 3; if (r < 2) yield return i + 3; if (c > 0) yield return i - 1; if (c < 2) yield return i + 1; }

    public sealed record QuizQ(string FormId, List<string> Options, int Answer);
    public static List<QuizQ> Quiz(long seed, GameData data)
    {
        var rng = new SeededRandom(unchecked((ulong)seed));
        var elements = data.Catalog.Families.Select(f => f.Element).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
        var fams = data.Catalog.Families.OrderBy(f => f.Id, StringComparer.Ordinal).ToList();
        var qs = new List<QuizQ>();
        var used = new HashSet<string>();
        while (qs.Count < QuizCount)
        {
            var fam = fams[rng.Next(fams.Count)];
            if (!used.Add(fam.Id)) continue;
            var form = fam.Forms[rng.Next(fam.Forms.Count)];
            var opts = new List<string> { fam.Element };
            while (opts.Count < 4) { var e = elements[rng.Next(elements.Count)]; if (!opts.Contains(e)) opts.Add(e); }
            for (int i = opts.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (opts[i], opts[j]) = (opts[j], opts[i]); }
            qs.Add(new QuizQ(form.Id, opts, opts.IndexOf(fam.Element)));
        }
        return qs;
    }

    private static object Payload(string game, long seed, GameData data) => game switch
    {
        "memory" or "daily" => new { board = MemoryBoard(seed, data) },
        "puzzle" => new { tiles = PuzzleStart(seed) },
        "sprint" => new { durationMs = SprintMs, minGapMs = MinTapGapMs },
        _ => new { questions = Quiz(seed, data).Select(q => new { q.FormId, q.Options }) },   // answers stay on the server
    };

    public static int Score(string game, long seed, MiniGameFinishRequest req, double secs, GameData data)
    {
        switch (game)
        {
            case "memory": case "daily":
            {
                var board = MemoryBoard(seed, data);
                var moves = req.Moves ?? throw ApiException.BadRequest("moves_required");
                if (moves.Count % 2 != 0 || moves.Count > 200) throw ApiException.BadRequest("invalid_moves");
                var matched = new bool[board.Count]; int turns = 0;
                for (int i = 0; i < moves.Count; i += 2)
                {
                    int a = moves[i], b = moves[i + 1];
                    if (a < 0 || b < 0 || a >= board.Count || b >= board.Count || a == b || matched[a] || matched[b]) throw ApiException.BadRequest("invalid_moves");
                    turns++;
                    if (board[a] == board[b]) matched[a] = matched[b] = true;
                }
                if (!matched.All(x => x)) throw ApiException.BadRequest("game_not_completed");
                if (secs < 0.6 * turns || secs < 6) throw ApiException.BadRequest("too_fast");   // humans need time to flip cards
                return Math.Max(100, 1000 - (turns - Pairs) * 25 - (int)secs * 2);
            }
            case "puzzle":
            {
                var t = PuzzleStart(seed);
                var moves = req.Moves ?? throw ApiException.BadRequest("moves_required");
                if (moves.Count > 400) throw ApiException.BadRequest("invalid_moves");
                foreach (var m in moves)
                {
                    int b = Array.IndexOf(t, 0);
                    if (m < 0 || m > 8 || !Neighbours(b).Contains(m)) throw ApiException.BadRequest("invalid_moves");
                    (t[b], t[m]) = (t[m], t[b]);
                }
                if (!t.SequenceEqual(new[] { 1, 2, 3, 4, 5, 6, 7, 8, 0 })) throw ApiException.BadRequest("game_not_completed");
                if (secs < 0.25 * moves.Count || secs < 4) throw ApiException.BadRequest("too_fast");
                return Math.Max(100, 1200 - moves.Count * 8 - (int)secs * 3);
            }
            case "sprint":
            {
                var taps = req.Taps ?? throw ApiException.BadRequest("taps_required");
                if (secs < SprintMs / 1000.0 - 1) throw ApiException.BadRequest("too_fast");
                if (taps.Count > SprintMs / MinTapGapMs + 5) throw ApiException.BadRequest("autoclick_detected");
                int valid = 0, invalid = 0, last = -MinTapGapMs;
                for (int i = 0; i < taps.Count; i++)
                {
                    if (taps[i] < 0 || taps[i] > SprintMs || (i > 0 && taps[i] < taps[i - 1])) throw ApiException.BadRequest("invalid_taps");
                    if (taps[i] - last < MinTapGapMs) invalid++; else { valid++; last = taps[i]; }
                }
                if (taps.Count > 20 && invalid * 10 > taps.Count) throw ApiException.BadRequest("autoclick_detected");
                // perfectly regular intervals are a bot signature
                if (taps.Count > 30) { var gaps = taps.Zip(taps.Skip(1), (a, b) => b - a).ToList(); double avg = gaps.Average(); if (gaps.All(x => Math.Abs(x - avg) < 2)) throw ApiException.BadRequest("autoclick_detected"); }
                return valid * 10;
            }
            default:
            {
                var qs = Quiz(seed, data);
                var ans = req.Answers ?? throw ApiException.BadRequest("answers_required");
                if (ans.Count != qs.Count) throw ApiException.BadRequest("invalid_answers");
                if (secs < 3) throw ApiException.BadRequest("too_fast");
                int correct = qs.Select((q, i) => ans[i] == q.Answer ? 1 : 0).Sum();
                return correct * 200 + Math.Max(0, 300 - (int)secs * 10) * correct / qs.Count;
            }
        }
    }
}
