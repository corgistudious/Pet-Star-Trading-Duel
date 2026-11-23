using Microsoft.EntityFrameworkCore;
using PetStar.Api.Auth;
using PetStar.Api.Data;
using PetStar.Api.Infrastructure;

namespace PetStar.Api.Features;

public sealed class CloseSeasonRequest { public int ServerId { get; set; } }

/// <summary>17 leaderboards computed only from server-recorded data (ratings, matches, cards, evolutions, PvE clears,
/// boss hits, mini-game sessions, guild progress, social interactions). Ties break on the earliest subject id.
/// Social boards count each liker once, never the author, and ignore accounts younger than 24 hours.</summary>
public static class LeaderboardEndpoints
{
    public static readonly string[] PlayerBoards = { "pvp_ranked", "pvp_wins", "pvp_winrate", "collection", "rare_collector", "evolution", "pve_progress", "boss_damage", "trainer_level", "achievements", "minigames", "guild_contribution", "showcase_liked", "creators" };
    public static readonly string[] GuildBoards = { "guild_ranking", "guild_boss", "guild_war" };
    public static readonly string[] Timed = { "pvp_wins", "pvp_winrate", "evolution", "boss_damage", "minigames", "guild_contribution", "showcase_liked", "creators", "guild_boss", "guild_war" };
    private static readonly string[] PvpModes = { "casual", "ranked", "friend", "private", "guildwar" };

    public sealed record Row(int Rank, long SubjectId, long Uid, string Name, long Score, string? Extra, SocialAccess.Badge? Badge);

    public static void Map(WebApplication app)
    {
        var g = ServerEndpoints.GameGroup(app);

        g.MapGet("/leaderboards", () => new { player = PlayerBoards, guild = GuildBoards, timed = Timed, periods = new[] { "week", "month", "season", "all" } });

        g.MapGet("/leaderboards/{board}", async (string board, string? period, HttpContext ctx, PetStarDb db, GameData data) =>
        {
            var (sid, mid) = ctx.User.Player();
            if (!PlayerBoards.Contains(board) && !GuildBoards.Contains(board)) throw ApiException.NotFound("board_not_found");
            period ??= "all";
            if (period is not ("week" or "month" or "season" or "all")) throw ApiException.BadRequest("invalid_period");
            var season = await CurrentSeason(db, sid);
            var since = !Timed.Contains(board) ? DateTimeOffset.MinValue : period switch
            {
                "week" => MissionPeriods.Weekly(DateTimeOffset.UtcNow).start,
                "month" => new DateTimeOffset(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, TimeSpan.Zero),
                "season" => season.StartsAt,
                _ => DateTimeOffset.MinValue,
            };
            var rows = await Compute(db, data, sid, board, since);
            bool guild = GuildBoards.Contains(board);
            long myKey = guild ? (await SocialAccess.GuildOf(db, mid) ?? -1) : mid;
            int myIdx = rows.FindIndex(r => r.id == myKey);
            var top = rows.Take(100).ToList();
            var view = await Decorate(db, top, guild);
            Row? me = null;
            if (myIdx >= 0) me = (await Decorate(db, new List<(long, long, string?)> { rows[myIdx] }, guild)).Select(r => r with { Rank = myIdx + 1 }).First();
            return new { board, period = Timed.Contains(board) ? period : "all", season = new { season.Key, season.StartsAt, season.EndsAt }, podium = view.Take(3), rows = view, me, total = rows.Count };
        });

        g.MapGet("/leaderboards/seasons/history", async (HttpContext ctx, PetStarDb db) =>
        {
            var (sid, mid) = ctx.User.Player();
            var seasons = await db.LeaderboardSeasons.AsNoTracking().Where(s => s.ServerId == sid && s.Status == "closed").OrderByDescending(s => s.Id).Take(10).ToListAsync();
            var ids = seasons.Select(s => s.Id).ToList();
            var mine = await db.LeaderboardSnapshots.AsNoTracking().Where(x => ids.Contains(x.SeasonId) && x.SubjectType == "player" && x.SubjectId == mid).ToListAsync();
            var podiums = await db.LeaderboardSnapshots.AsNoTracking().Where(x => ids.Contains(x.SeasonId) && x.Rank <= 3).ToListAsync();
            var rewards = await db.RankingRewards.AsNoTracking().Where(r => ids.Contains(r.SeasonId) && r.MembershipId == mid).ToListAsync();
            return seasons.Select(s => new
            {
                s.Key, s.StartsAt, s.EndsAt,
                mine = mine.Where(m => m.SeasonId == s.Id).Select(m => new { m.Board, m.Rank, m.Score }),
                podiums = podiums.Where(p => p.SeasonId == s.Id).GroupBy(p => p.Board).ToDictionary(x => x.Key, x => x.OrderBy(p => p.Rank).Select(p => new { p.Rank, p.Name, p.Score })),
                rewards = rewards.Where(r => r.SeasonId == s.Id).Select(r => new { r.Id, r.Board, r.Rank, r.RewardJson, claimed = r.ClaimedAt != null }),
            });
        });

        g.MapPost("/leaderboards/rewards/{id:long}/claim", async (long id, HttpContext ctx, PetStarDb db, EconomyService eco) =>
        {
            var (sid, mid) = ctx.User.Player();
            await using var tx = await db.Database.BeginTransactionAsync();
            var r = await db.RankingRewards.FromSqlRaw("SELECT * FROM ranking_rewards WHERE id = {0} FOR UPDATE", id).FirstOrDefaultAsync();
            if (r == null || r.MembershipId != mid) throw ApiException.NotFound("reward_not_found");
            if (r.ClaimedAt != null) throw ApiException.Conflict("already_claimed");
            var reward = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, long>>(r.RewardJson) ?? new();
            foreach (var (cur, amt) in reward.Where(x => Rules.Currency.All.Contains(x.Key))) await eco.Currency(mid, sid, cur, amt, "ranking_reward", "rank:" + r.Id);
            if (r.Rank <= 3) await db.Database.ExecuteSqlRawAsync("INSERT INTO cosmetics_owned (membership_id, item_id, source, acquired_at) VALUES ({0}, 'frame_season_champion', 'ranking', now()) ON CONFLICT DO NOTHING", mid);
            r.ClaimedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return Results.Ok(new { claimed = true, reward, frame = r.Rank <= 3 ? "frame_season_champion" : null });
        });

        // Season close for an admin panel later (API only): snapshots top 100 per board, top 10 earn rewards, new season opens.
        var admin = app.MapGroup("/api/admin").RequireAuthorization("ServerAdmin").RequireRateLimiting("user");
        admin.MapPost("/leaderboards/close-season", async (CloseSeasonRequest req, HttpContext ctx, PetStarDb db, GameData data, AuditService audit) =>
        {
            var season = await CurrentSeason(db, req.ServerId);
            await using var tx = await db.Database.BeginTransactionAsync();
            foreach (var board in PlayerBoards.Concat(GuildBoards))
            {
                bool guild = GuildBoards.Contains(board);
                var rows = (await Compute(db, data, req.ServerId, board, Timed.Contains(board) ? season.StartsAt : DateTimeOffset.MinValue)).Take(100).ToList();
                var view = await Decorate(db, rows, guild);
                foreach (var v in view)
                {
                    db.LeaderboardSnapshots.Add(new LeaderboardSnapshot { SeasonId = season.Id, Board = board, Rank = v.Rank, SubjectType = guild ? "guild" : "player", SubjectId = v.SubjectId, Name = v.Name, Score = v.Score });
                    if (!guild && v.Rank <= 10)
                        db.RankingRewards.Add(new RankingReward { SeasonId = season.Id, Board = board, MembershipId = v.SubjectId, Rank = v.Rank,
                            RewardJson = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, long> { [Rules.Currency.MiniGreenBean] = v.Rank == 1 ? 500 : v.Rank <= 3 ? 300 : 100 }) });
                }
            }
            var s = await db.LeaderboardSeasons.FirstAsync(x => x.Id == season.Id);
            s.Status = "closed"; s.EndsAt = DateTimeOffset.UtcNow;
            var now = DateTimeOffset.UtcNow;
            db.LeaderboardSeasons.Add(new LeaderboardSeason { ServerId = req.ServerId, Key = "S" + now.ToString("yyyyMMddHHmm"), StartsAt = now, EndsAt = now.AddDays(90) });
            audit.Add("leaderboard.close_season", ctx.User.AccountId(), "season", season.Id.ToString(), req.ServerId);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return Results.Ok(new { closed = season.Key });
        });
    }

    public static async Task<LeaderboardSeason> CurrentSeason(PetStarDb db, int sid)
    {
        var s = await db.LeaderboardSeasons.AsNoTracking().Where(x => x.ServerId == sid && x.Status == "active").OrderByDescending(x => x.Id).FirstOrDefaultAsync();
        if (s != null) return s;
        var now = DateTimeOffset.UtcNow;
        var start = new DateTimeOffset(now.Year, (now.Month - 1) / 3 * 3 + 1, 1, 0, 0, 0, TimeSpan.Zero);
        await db.Database.ExecuteSqlRawAsync("INSERT INTO leaderboard_seasons (server_id, key, starts_at, ends_at, status) VALUES ({0}, {1}, {2}, {3}, 'active') ON CONFLICT DO NOTHING",
            sid, $"S{start:yyyy}Q{(start.Month - 1) / 3 + 1}", start, start.AddMonths(3));
        return await db.LeaderboardSeasons.AsNoTracking().Where(x => x.ServerId == sid && x.Status == "active").OrderByDescending(x => x.Id).FirstAsync();
    }

    /// <summary>(subject id, score, extra) sorted by score desc, then subject id asc (deterministic tie-break).</summary>
    public static async Task<List<(long id, long score, string? extra)>> Compute(PetStarDb db, GameData data, int sid, string board, DateTimeOffset since)
    {
        var members = db.Memberships.AsNoTracking().Where(m => m.ServerId == sid && m.Status == "active");
        List<(long id, long score, string? extra)> rows;
        switch (board)
        {
            case "pvp_ranked":
            {
                var season = await RankedEndpoints.ActiveSeason(db, sid);
                rows = (await db.ArenaRatings.AsNoTracking().Where(r => r.SeasonId == season.Id && r.ServerId == sid && (r.Wins + r.Losses + r.Draws) > 0)
                    .Select(r => new { r.MembershipId, r.Rating, r.Wins, r.Losses }).ToListAsync()).Select(r => (r.MembershipId, (long)r.Rating, (string?)$"{r.Wins}W {r.Losses}L")).ToList();
                break;
            }
            case "pvp_wins": case "pvp_winrate":
            {
                var games = await db.ArenaMatchPlayers.AsNoTracking().Where(p => p.MembershipId != null)
                    .Join(db.ArenaMatches.Where(m => m.ServerId == sid && m.Status == "finished" && PvpModes.Contains(m.Mode) && m.FinishedAt >= since), p => p.MatchId, m => m.Id,
                        (p, m) => new { mid = p.MembershipId!.Value, win = m.WinnerSide == p.Side, ai = db.ArenaMatchPlayers.Any(o => o.MatchId == m.Id && o.Side != p.Side && o.IsAi) })
                    .Where(x => !x.ai).GroupBy(x => x.mid).Select(x => new { mid = x.Key, games = x.Count(), wins = x.Count(y => y.win) }).ToListAsync();
                rows = board == "pvp_wins"
                    ? games.Where(x => x.wins > 0).Select(x => (x.mid, (long)x.wins, (string?)$"{x.games}")).ToList()
                    : games.Where(x => x.games >= 10).Select(x => (x.mid, (long)(x.wins * 10000L / x.games), (string?)$"{x.wins}/{x.games}")).ToList();   // basis points, min 10 games
                break;
            }
            case "collection":
                rows = (await db.Codex.AsNoTracking().Where(c => members.Any(m => m.Id == c.MembershipId)).GroupBy(c => c.MembershipId).Select(x => new { x.Key, n = x.Count() }).ToListAsync())
                    .Select(x => (x.Key, (long)x.n, (string?)null)).ToList();
                break;
            case "rare_collector":
            {
                var rareIds = data.Catalog.Rarities.Where(r => r.Order >= FeedEndpoints.RareRank).Select(r => r.Id).ToList();
                rows = (await db.Cards.AsNoTracking().Where(c => c.ServerId == sid && rareIds.Contains(c.Rarity)).GroupBy(c => c.OwnerMembershipId).Select(x => new { x.Key, n = x.Count() }).ToListAsync())
                    .Select(x => (x.Key, (long)x.n, (string?)null)).ToList();
                break;
            }
            case "evolution":
                rows = (await db.CardEvolutions.AsNoTracking().Where(e => e.At >= since && members.Any(m => m.Id == e.MembershipId)).GroupBy(e => e.MembershipId).Select(x => new { x.Key, n = x.Count() }).ToListAsync())
                    .Select(x => (x.Key, (long)x.n, (string?)null)).ToList();
                break;
            case "pve_progress":
                rows = (await db.PveProgress.AsNoTracking().Where(p => p.ClearCount > 0 && members.Any(m => m.Id == p.MembershipId)).GroupBy(p => p.MembershipId).Select(x => new { x.Key, n = x.Count() }).ToListAsync())
                    .Select(x => (x.Key, (long)x.n, (string?)null)).ToList();
                break;
            case "boss_damage":
                rows = (await db.GuildBossAttacks.AsNoTracking().Where(a => a.At >= since && members.Any(m => m.Id == a.MembershipId)).GroupBy(a => a.MembershipId).Select(x => new { x.Key, n = x.Sum(a => a.Damage) }).ToListAsync())
                    .Select(x => (x.Key, x.n, (string?)null)).ToList();
                break;
            case "trainer_level":
                rows = (await members.Select(m => new { m.Id, m.Level, m.Exp }).ToListAsync()).Select(m => (m.Id, (long)m.Level * 1_000_000 + m.Exp, (string?)$"Lv.{m.Level}")).ToList();
                break;
            case "achievements":
                rows = (await db.Achievements.AsNoTracking().Where(a => members.Any(m => m.Id == a.MembershipId)).GroupBy(a => a.MembershipId).Select(x => new { x.Key, n = x.Count() }).ToListAsync())
                    .Select(x => (x.Key, (long)x.n * 10, (string?)null)).ToList();
                break;
            case "minigames":
                rows = (await db.MiniGameSessions.AsNoTracking().Where(s => s.ServerId == sid && s.Score != null && s.FinishedAt >= since).GroupBy(s => new { s.MembershipId, s.Game })
                        .Select(x => new { x.Key.MembershipId, best = x.Max(s => s.Score!.Value) }).ToListAsync())
                    .GroupBy(x => x.MembershipId).Select(x => (x.Key, (long)x.Sum(y => y.best), (string?)null)).ToList();
                break;
            case "guild_contribution":
            {
                var key = since == DateTimeOffset.MinValue ? "all" : null;
                var q = db.GuildContributions.AsNoTracking().Where(c => members.Any(m => m.Id == c.MembershipId));
                q = key != null ? q.Where(c => c.PeriodKey == "all") : q.Where(c => c.PeriodKey != "all" && string.Compare(c.PeriodKey, GuildService.Week(since)) >= 0);
                rows = (await q.GroupBy(c => c.MembershipId).Select(x => new { x.Key, n = x.Sum(c => c.Points) }).ToListAsync()).Select(x => (x.Key, x.n, (string?)null)).ToList();
                break;
            }
            case "showcase_liked": case "creators":
            {
                var cutoff = DateTimeOffset.UtcNow.AddHours(-24);
                var likes = await db.Likes.AsNoTracking().Where(l => l.At >= since)
                    .Join(db.Posts.Where(p => p.ServerId == sid && p.Status == "visible" && p.Visibility == "public" && (board == "creators" || p.ShowcaseKind != null)), l => l.PostId, p => p.Id, (l, p) => new { l.MembershipId, p.AuthorMembershipId, p.Id })
                    .Join(db.Memberships.Where(m => m.CreatedAt < cutoff), x => x.MembershipId, m => m.Id, (x, m) => x)
                    .Where(x => x.MembershipId != x.AuthorMembershipId).Distinct().ToListAsync();
                rows = board == "showcase_liked"
                    // best single showcase post per author
                    ? likes.GroupBy(x => x.Id).Select(x => new { post = x.Key, author = x.First().AuthorMembershipId, n = x.Select(y => y.MembershipId).Distinct().Count() })
                        .GroupBy(x => x.author).Select(x => (x.Key, (long)x.Max(y => y.n), (string?)x.OrderByDescending(y => y.n).First().post.ToString())).ToList()
                    // creators: distinct supporters across all public posts
                    : likes.GroupBy(x => x.AuthorMembershipId).Select(x => (x.Key, (long)x.Select(y => y.MembershipId).Distinct().Count(), (string?)$"{x.Select(y => y.Id).Distinct().Count()}")).ToList();
                break;
            }
            case "guild_ranking":
                rows = (await db.Guilds.AsNoTracking().Where(x => x.ServerId == sid).Select(x => new { x.Id, x.Level, x.Exp }).ToListAsync()).Select(x => (x.Id, x.Exp, (string?)$"Lv.{x.Level}")).ToList();
                break;
            case "guild_boss":
                rows = (await db.GuildBossAttacks.AsNoTracking().Where(a => a.At >= since).Join(db.GuildBosses, a => a.BossId, b => b.Id, (a, b) => new { b.GuildId, a.Damage })
                        .Join(db.Guilds.Where(x => x.ServerId == sid), x => x.GuildId, gu => gu.Id, (x, gu) => x).GroupBy(x => x.GuildId).Select(x => new { x.Key, n = x.Sum(y => y.Damage) }).ToListAsync())
                    .Select(x => (x.Key, x.n, (string?)null)).ToList();
                break;
            default: // guild_war
            {
                var sinceWeek = since == DateTimeOffset.MinValue ? "" : GuildService.Week(since);
                rows = (await db.GuildWarEntries.AsNoTracking().Where(e => e.ServerId == sid && string.Compare(e.PeriodKey, sinceWeek) >= 0).GroupBy(e => e.GuildId)
                        .Select(x => new { x.Key, pts = x.Sum(e => e.Points), wins = x.Sum(e => e.Wins) }).ToListAsync())
                    .Select(x => (x.Key, (long)x.pts, (string?)$"{x.wins}W")).ToList();
                break;
            }
        }
        // only existing guilds / active members of this server appear
        if (!GuildBoards.Contains(board))
        {
            var ids = rows.Select(r => r.id).ToList();
            var valid = (await members.Where(m => ids.Contains(m.Id)).Select(m => m.Id).ToListAsync()).ToHashSet();
            rows = rows.Where(r => valid.Contains(r.id)).ToList();
        }
        return rows.Where(r => r.score > 0).OrderByDescending(r => r.score).ThenBy(r => r.id).ToList();
    }

    private static async Task<List<Row>> Decorate(PetStarDb db, List<(long id, long score, string? extra)> rows, bool guild)
    {
        if (guild)
        {
            var ids = rows.Select(r => r.id).ToList();
            var gs = await db.Guilds.AsNoTracking().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
            return rows.Select((r, i) => new Row(i + 1, r.id, 0, gs.TryGetValue(r.id, out var gg) ? (gg.Tag.Length > 0 ? $"[{gg.Tag}] " : "") + gg.Name : "?", r.score, r.extra, null)).ToList();
        }
        var badges = await SocialAccess.Badges(db, rows.Select(r => r.id));
        return rows.Select((r, i) => { var b = badges.GetValueOrDefault(r.id); return new Row(i + 1, r.id, b?.Uid ?? 0, b?.Nickname ?? "?", r.score, r.extra, b); }).ToList();
    }
}
