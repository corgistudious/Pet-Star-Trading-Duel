using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PetStar.Api.Data;
using PetStar.Api.Infrastructure;
using PetStar.Contracts;
using PetStar.Rules;
using PetStar.Rules.Battle;

namespace PetStar.Api.Features;

public sealed class FriendDuelRequest { public long Uid { get; set; } }

/// <summary>
/// Ranked seasons with asynchronous PvP: the attacker plays live against the defender's saved default deck, which the
/// server AI controls with the same rules and only its own energy. Matchmaking weighs rating and team power. Premium
/// currency gives no direct advantage (ranked uses cards only; no purchasable boosts).
/// </summary>
public static class RankedEndpoints
{
    public const int KFactor = 32;
    public const int MaxRatedVsSameOpponentPerDay = 3;

    public static async Task<RankedSeason> ActiveSeason(PetStarDb db, int sid)
    {
        var now = DateTimeOffset.UtcNow;
        var s = await db.Seasons.FirstOrDefaultAsync(x => x.ServerId == sid && x.Status == "active");
        if (s != null) return s;
        int n = await db.Seasons.CountAsync(x => x.ServerId == sid);
        s = new RankedSeason { ServerId = sid, Name = $"Season {n + 1}", StartsAt = now, EndsAt = now.AddDays(28) };
        db.Seasons.Add(s);
        await db.SaveChangesAsync();
        return s;
    }

    public static (int a, int b) Elo(int ra, int rb, double scoreA, int k = KFactor)
    {
        double ea = 1.0 / (1 + Math.Pow(10, (rb - ra) / 400.0));
        int da = (int)Math.Round(k * (scoreA - ea));
        return (ra + da, rb - (int)Math.Round(da / 2.0));   // defender's (AI-played) result moves half as much
    }

    public static void Map(WebApplication app)
    {
        var g = ServerEndpoints.GameGroup(app);

        g.MapGet("/ranked", async (HttpContext ctx, PetStarDb db) =>
        {
            var (sid, mid) = ctx.User.Player();
            var s = await ActiveSeason(db, sid);
            var r = await db.RankedRecords.AsNoTracking().FirstOrDefaultAsync(x => x.SeasonId == s.Id && x.MembershipId == mid);
            int rating = r?.Rating ?? 1000;
            int rank = await db.RankedRecords.CountAsync(x => x.SeasonId == s.Id && x.Rating > rating) + 1;
            return new { season = s, rating, wins = r?.Wins ?? 0, losses = r?.Losses ?? 0, streak = r?.Streak ?? 0, bestStreak = r?.BestStreak ?? 0, rank = r == null ? (int?)null : rank };
        });

        g.MapGet("/ranked/leaderboard", async (int? seasonId, HttpContext ctx, PetStarDb db) =>
        {
            var (sid, _) = ctx.User.Player();
            var s = seasonId.HasValue ? await db.Seasons.AsNoTracking().FirstOrDefaultAsync(x => x.Id == seasonId && x.ServerId == sid) ?? throw ApiException.NotFound() : await ActiveSeason(db, sid);
            var rows = await db.RankedRecords.AsNoTracking().Where(r => r.SeasonId == s.Id).OrderByDescending(r => r.Rating).ThenByDescending(r => r.Wins).Take(100)
                .Join(db.Memberships, r => r.MembershipId, m => m.Id, (r, m) => new { uid = m.AccountId, m.Nickname, r.Rating, r.Wins, r.Losses, r.BestStreak }).ToListAsync();
            return new { season = s, entries = rows.Select((r, i) => new { rank = i + 1, r.uid, r.Nickname, r.Rating, r.Wins, r.Losses, r.BestStreak }) };
        });

        g.MapPost("/ranked/match", (HttpContext ctx, PetStarDb db, GameData data, IdempotencyService idem) =>
            idem.Run(ctx, "ranked.match", new { }, async () =>
            {
                var (sid, mid) = ctx.User.Player();
                var season = await ActiveSeason(db, sid);
                var (mine, myPower) = await LoadDeck(db, data, mid, sid, 0, requireFree: true);
                var myRating = (await db.RankedRecords.AsNoTracking().FirstOrDefaultAsync(r => r.SeasonId == season.Id && r.MembershipId == mid))?.Rating ?? 1000;
                var blocked = await MarketEndpoints.BlockedIds(db, mid);
                var today = DateTimeOffset.UtcNow.Date;
                var candidates = await db.Decks.AsNoTracking().Where(d => d.IsDefault && d.ServerId == sid && d.MembershipId != mid && !blocked.Contains(d.MembershipId))
                    .Select(d => d.MembershipId).Take(500).ToListAsync();
                if (candidates.Count == 0) throw ApiException.Conflict("no_opponent_available");
                var ratings = await db.RankedRecords.AsNoTracking().Where(r => r.SeasonId == season.Id && candidates.Contains(r.MembershipId)).ToDictionaryAsync(r => r.MembershipId, r => r.Rating);
                var scored = new List<(long mid, double score, int power)>();
                foreach (var c in candidates)
                {
                    try
                    {
                        var (_, power) = await LoadDeck(db, data, c, sid, 1, requireFree: false);
                        int rating = ratings.GetValueOrDefault(c, 1000);
                        scored.Add((c, Math.Abs(rating - myRating) + Math.Abs(power - myPower) / 10.0, power));
                    }
                    catch (ApiException) { /* invalid defense deck: skip */ }
                }
                if (scored.Count == 0) throw ApiException.Conflict("no_opponent_available");
                var pick = scored.OrderBy(x => x.score).Take(5).ElementAt(new SecureRandomSource().Next(Math.Min(5, scored.Count)));
                var (enemy, _) = await LoadDeck(db, data, pick.mid, sid, 1, requireFree: false);
                return await StartBattle(db, data, sid, mid, pick.mid, "ranked", mine, enemy, season.Id.ToString());
            }));

        // Friend duel (unrated, no rewards): fight against a friend's default deck; the friend gets a notification.
        g.MapPost("/duels/friend", (FriendDuelRequest req, HttpContext ctx, PetStarDb db, GameData data, NotificationService notes, IdempotencyService idem) =>
            idem.Run(ctx, "duels.friend", req, async () =>
            {
                var (sid, mid) = ctx.User.Player();
                var friend = await SocialEndpoints.Member(db, req.Uid, sid);
                if (!await db.Friendships.AnyAsync(f => f.Status == "accepted" && ((f.RequesterMembershipId == mid && f.AddresseeMembershipId == friend.Id) || (f.AddresseeMembershipId == mid && f.RequesterMembershipId == friend.Id))))
                    throw ApiException.Forbidden("not_friends");
                var (mine, _) = await LoadDeck(db, data, mid, sid, 0, requireFree: true);
                var (enemy, _) = await LoadDeck(db, data, friend.Id, sid, 1, requireFree: false);
                var b = await StartBattle(db, data, sid, mid, friend.Id, "friend", mine, enemy, null);
                await notes.Notify(friend.Id, "duel_challenge", new { uid = ctx.User.AccountId(), battleId = b.Id }, "duel:" + b.Id);
                return b;
            }));

        g.MapGet("/battles/history", async (HttpContext ctx, PetStarDb db) =>
        {
            var (sid, mid) = ctx.User.Player();
            return await db.Battles.AsNoTracking().Where(b => b.ServerId == sid && (b.PlayerAMembershipId == mid || b.PlayerBMembershipId == mid) && b.Status != "active")
                .OrderByDescending(b => b.CreatedAt).Take(50)
                .Select(b => new { b.Id, b.Mode, b.StageId, b.Status, attacker = b.PlayerAMembershipId == mid, won = (b.PlayerAMembershipId == mid && b.WinnerSide == 0) || (b.PlayerBMembershipId == mid && b.WinnerSide == 1), b.Round, b.CreatedAt })
                .ToListAsync();
        });
    }

    public static async Task<(List<BattleUnit> units, int power)> LoadDeck(PetStarDb db, GameData data, long mid, int sid, int side, bool requireFree)
    {
        var deck = await db.Decks.AsNoTracking().Include(d => d.Slots).FirstOrDefaultAsync(d => d.MembershipId == mid && d.IsDefault) ?? throw ApiException.Conflict("no_deck");
        var ids = deck.Slots.Select(s => s.CardId).ToList();
        var cards = await db.Cards.AsNoTracking().Where(c => ids.Contains(c.Id) && c.OwnerMembershipId == mid && c.ServerId == sid).ToDictionaryAsync(c => c.Id);
        var err = DeckRules.Validate(data.Catalog, deck.Slots.Select(s => new DeckCardInfo { CardId = s.CardId, Position = s.Position, FormId = cards.TryGetValue(s.CardId, out var c) ? c.FormId : "" }).ToList());
        if (err != null || cards.Count != 5) throw ApiException.Conflict("deck_invalid", err);
        if (requireFree && cards.Values.Any(c => c.BusyState != null)) throw ApiException.Conflict("deck_card_busy");
        var units = deck.Slots.OrderBy(s => s.Position).Select((s, i) =>
        {
            var c = cards[s.CardId];
            return BattleEngine.CreateUnit(data.Catalog, side, i, s.Position, c.Id, c.FormId, c.Level, c.Rarity, c.Quality, c.SkillLevel);
        }).ToList();
        int power = cards.Values.Sum(c => StatCalculator.Compute(data.Catalog, c.FormId, c.Level, c.Rarity, c.Quality).Power);
        return (units, power);
    }

    private static async Task<BattleDto> StartBattle(PetStarDb db, GameData data, int sid, long mid, long opponent, string mode, List<BattleUnit> a, List<BattleUnit> b, string? stageId)
    {
        var now = DateTimeOffset.UtcNow;
        await db.Battles.Where(x => x.PlayerAMembershipId == mid && x.Status == "active")
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.Status, "abandoned").SetProperty(x => x.FinishedAt, now).SetProperty(x => x.EndReason, "abandoned"));
        long seed = SecureRandomSource.NewSeed();
        var st = new BattleEngine(data.Catalog).Start(a, b, unchecked((ulong)seed));
        var battle = new Battle
        {
            Id = Guid.NewGuid(), ServerId = sid, Mode = mode, StageId = stageId, PlayerAMembershipId = mid, PlayerBMembershipId = opponent, Status = "active",
            StateJson = JsonSerializer.Serialize(st, IdempotencyService.Json), Seed = seed, Round = st.Round, CreatedAt = now, UpdatedAt = now,
        };
        db.Battles.Add(battle);
        await db.SaveChangesAsync();
        return BattleEndpoints.ToDto(battle, st, new List<BattleEvent>(), null);
    }

    /// <summary>Applies ranked/friend results (called inside the round transaction when the battle finishes).</summary>
    public static async Task<BattleRewardsDto> Finish(PetStarDb db, EconomyService eco, AchievementService ach, Battle b, BattleState st, int sid, long mid)
    {
        var rewards = new BattleRewardsDto();
        db.BattleResults.Add(new BattleResult { BattleId = b.Id, WinnerMembershipId = st.Winner == 0 ? mid : st.Winner == 1 ? b.PlayerBMembershipId : null, RewardsJson = "{}", At = DateTimeOffset.UtcNow });
        if (b.Mode != "ranked" || b.PlayerBMembershipId is not long opp) { await db.SaveChangesAsync(); return rewards; }
        int seasonId = int.Parse(b.StageId!);
        var dayStart = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero);
        int sameToday = await db.Battles.CountAsync(x => x.Mode == "ranked" && x.PlayerAMembershipId == mid && x.PlayerBMembershipId == opp && x.Status == "finished" && x.FinishedAt >= dayStart && x.Id != b.Id);
        if (sameToday >= MaxRatedVsSameOpponentPerDay) { rewards.DailyCapReached = true; await db.SaveChangesAsync(); return rewards; }   // anti-farm: unrated
        var me = await Record(db, seasonId, mid);
        var them = await Record(db, seasonId, opp);
        double score = st.Winner == 0 ? 1 : st.Winner == -1 ? 0.5 : 0;
        (me.Rating, them.Rating) = Elo(me.Rating, them.Rating, score);
        me.Rating = Math.Max(0, me.Rating); them.Rating = Math.Max(0, them.Rating);
        if (score == 1) { me.Wins++; me.Streak++; me.BestStreak = Math.Max(me.BestStreak, me.Streak); }
        else if (score == 0) { me.Losses++; me.Streak = 0; }
        me.UpdatedAt = them.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        if (score == 1)
        {
            await eco.Currency(mid, sid, Currency.MiniGreenBean, 40, "ranked_win", "battle:" + b.Id.ToString("N"));
            rewards.Currencies[Currency.MiniGreenBean] = 40;
            await ach.Evaluate(mid, sid, "ranked_win", "duel_win");
        }
        return rewards;
    }

    private static async Task<RankedRecord> Record(PetStarDb db, int seasonId, long mid)
    {
        var r = await db.RankedRecords.FirstOrDefaultAsync(x => x.SeasonId == seasonId && x.MembershipId == mid);
        if (r == null) { r = new RankedRecord { SeasonId = seasonId, MembershipId = mid, UpdatedAt = DateTimeOffset.UtcNow }; db.RankedRecords.Add(r); }
        return r;
    }
}
