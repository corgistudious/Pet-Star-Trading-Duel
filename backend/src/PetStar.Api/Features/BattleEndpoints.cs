using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PetStar.Api.Data;
using PetStar.Api.Infrastructure;
using PetStar.Contracts;
using PetStar.Rules;
using PetStar.Rules.Battle;

namespace PetStar.Api.Features;

public static class BattleEndpoints
{
    private static readonly JsonSerializerOptions J = IdempotencyService.Json;

    public static void Map(WebApplication app)
    {
        var g = ServerEndpoints.GameGroup(app);

        g.MapGet("/pve/stages", async (HttpContext ctx, PetStarDb db, GameData data) =>
        {
            var (_, mid) = ctx.User.Player();
            var prog = await db.PveProgress.AsNoTracking().Where(p => p.MembershipId == mid).ToDictionaryAsync(p => p.StageId);
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            return data.Pve.Stages.OrderBy(s => s.Chapter).ThenBy(s => s.Order).Select(s =>
            {
                prog.TryGetValue(s.Id, out var p);
                int used = p != null && p.DailyDate == today ? p.DailyRewardedClears : 0;
                return new PveStageDto
                {
                    Id = s.Id, Chapter = s.Chapter, Order = s.Order, Difficulty = s.Difficulty,
                    Unlocked = s.RequiresStage == null || (prog.TryGetValue(s.RequiresStage, out var r) && r.ClearCount > 0),
                    ClearCount = p?.ClearCount ?? 0, RewardedClearsLeftToday = Math.Max(0, data.Pve.DailyRewardedClearsPerStage - used),
                    EnemyForms = s.Enemies.OrderBy(e => e.Position).Select(e => e.FormId).ToList(),
                };
            });
        });

        g.MapPost("/battles/pve", (StartPveRequest req, HttpContext ctx, PetStarDb db, GameData data, IdempotencyService idem) =>
            idem.Run(ctx, "battles.pve.start", req, async () =>
            {
                var (sid, mid) = ctx.User.Player();
                var stage = data.Pve.Stages.FirstOrDefault(s => s.Id == req.StageId) ?? throw ApiException.NotFound("stage_not_found");
                if (stage.RequiresStage != null && !await db.PveProgress.AnyAsync(p => p.MembershipId == mid && p.StageId == stage.RequiresStage && p.ClearCount > 0))
                    throw ApiException.Conflict("stage_locked");
                var deck = await db.Decks.AsNoTracking().Include(d => d.Slots)
                    .FirstOrDefaultAsync(d => d.MembershipId == mid && (req.DeckId == null ? d.IsDefault : d.Id == req.DeckId)) ?? throw ApiException.Conflict("no_deck");
                var ids = deck.Slots.Select(s => s.CardId).ToList();
                var cards = await db.Cards.AsNoTracking().Where(c => ids.Contains(c.Id) && c.OwnerMembershipId == mid && c.ServerId == sid).ToDictionaryAsync(c => c.Id);
                var err = DeckRules.Validate(data.Catalog, deck.Slots.Select(s => new DeckCardInfo { CardId = s.CardId, Position = s.Position, FormId = cards.TryGetValue(s.CardId, out var c) ? c.FormId : "" }).ToList());
                if (err != null || cards.Count != 5) throw ApiException.Conflict("deck_invalid", err);
                if (cards.Values.Any(c => c.BusyState != null)) throw ApiException.Conflict("deck_card_busy"); // listed / in auction / fraud lock

                // one active battle at a time: abandon stale ones
                var now = DateTimeOffset.UtcNow;
                await db.Battles.Where(b => b.PlayerAMembershipId == mid && b.Status == "active")
                    .ExecuteUpdateAsync(u => u.SetProperty(b => b.Status, "abandoned").SetProperty(b => b.FinishedAt, now).SetProperty(b => b.EndReason, "abandoned"));

                var cat = data.Catalog;
                var mine = deck.Slots.OrderBy(s => s.Position).Select((s, i) =>
                {
                    var c = cards[s.CardId];
                    return BattleEngine.CreateUnit(cat, 0, i, s.Position, c.Id, c.FormId, c.Level, c.Rarity, c.Quality, c.SkillLevel);
                }).ToList();
                var enemy = stage.Enemies.OrderBy(e => e.Position).Select((e, i) =>
                    BattleEngine.CreateUnit(cat, 1, i, e.Position, 0, e.FormId, e.Level, e.Rarity, e.Quality)).ToList();
                long seed = SecureRandomSource.NewSeed();
                var st = new BattleEngine(cat).Start(mine, enemy, unchecked((ulong)seed));
                var battle = new Battle
                {
                    Id = Guid.NewGuid(), ServerId = sid, Mode = "pve", StageId = stage.Id, PlayerAMembershipId = mid, Status = "active",
                    StateJson = JsonSerializer.Serialize(st, J), Seed = seed, Round = st.Round, CreatedAt = now, UpdatedAt = now,
                };
                db.Battles.Add(battle);
                await db.SaveChangesAsync();
                return ToDto(battle, st, new List<BattleEvent>(), null);
            }));

        g.MapGet("/battles/{id:guid}", async (Guid id, HttpContext ctx, PetStarDb db) =>
        {
            var (_, mid) = ctx.User.Player();
            var b = await db.Battles.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.PlayerAMembershipId == mid) ?? throw ApiException.NotFound("battle_not_found");
            var st = JsonSerializer.Deserialize<BattleState>(b.StateJson, J)!;
            var last = await db.BattleRounds.AsNoTracking().Where(r => r.BattleId == id).OrderByDescending(r => r.Round).FirstOrDefaultAsync();
            var res = await db.BattleResults.AsNoTracking().FirstOrDefaultAsync(r => r.BattleId == id);
            return ToDto(b, st, last == null ? new() : JsonSerializer.Deserialize<List<BattleEvent>>(last.EventsJson, J)!,
                res == null ? null : JsonSerializer.Deserialize<BattleRewardsDto>(res.RewardsJson, J));
        });

        // Submitting a round is naturally idempotent: (battle, round) is a primary key and a stale round number
        // returns the already-resolved state instead of resolving again.
        g.MapPost("/battles/{id:guid}/rounds", async (Guid id, SubmitRoundRequest req, HttpContext ctx, PetStarDb db, GameData data, EconomyService eco, AchievementService ach) =>
        {
            var (sid, mid) = ctx.User.Player();
            await using var tx = await db.Database.BeginTransactionAsync();
            var b = await db.Battles.FromSqlRaw("SELECT * FROM battles WHERE id = {0} AND player_a_membership_id = {1} FOR UPDATE", id, mid)
                .FirstOrDefaultAsync() ?? throw ApiException.NotFound("battle_not_found");
            if (LivePvpEndpoints.IsLive(b)) throw ApiException.BadRequest("use_pvp_endpoint");
            var st = JsonSerializer.Deserialize<BattleState>(b.StateJson, J)!;
            if (b.Status != "active" || req.Round < st.Round)
            {
                var prev = await db.BattleRounds.AsNoTracking().FirstOrDefaultAsync(r => r.BattleId == id && r.Round == req.Round);
                var res = await db.BattleResults.AsNoTracking().FirstOrDefaultAsync(r => r.BattleId == id);
                if (prev == null && b.Status != "active") throw ApiException.Conflict("battle_finished");
                return ToDto(b, st, prev == null ? new() : JsonSerializer.Deserialize<List<BattleEvent>>(prev.EventsJson, J)!,
                    res == null ? null : JsonSerializer.Deserialize<BattleRewardsDto>(res.RewardsJson, J));
            }
            if (req.Round != st.Round) throw ApiException.Conflict("round_mismatch");
            var engine = new BattleEngine(data.Catalog);
            var stage = b.Mode == "pve" ? data.Pve.Stages.First(s => s.Id == b.StageId) : null;
            var aiPlan = new BattleAi(data.Catalog).Plan(st, 1, stage?.Difficulty ?? 2);
            int round = st.Round;
            var events = engine.ResolveRound(st, req.Actions, aiPlan);
            db.BattleRounds.Add(new BattleRound
            {
                BattleId = id, Round = round, ActionsAJson = JsonSerializer.Serialize(req.Actions, J), ActionsBJson = JsonSerializer.Serialize(aiPlan, J),
                EventsJson = JsonSerializer.Serialize(events, J), ResolvedAt = DateTimeOffset.UtcNow,
            });
            BattleRewardsDto? rewards = null;
            ApplyState(b, st);
            if (st.Finished) rewards = stage != null ? await FinishPve(db, eco, data, b, st, stage, sid, mid, ach) : await RankedEndpoints.Finish(db, eco, ach, b, st, sid, mid);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return ToDto(b, st, events, rewards);
        });

        g.MapPost("/battles/{id:guid}/surrender", async (Guid id, HttpContext ctx, PetStarDb db) =>
        {
            var (_, mid) = ctx.User.Player();
            await using var tx = await db.Database.BeginTransactionAsync();
            var b = await db.Battles.FromSqlRaw("SELECT * FROM battles WHERE id = {0} AND player_a_membership_id = {1} FOR UPDATE", id, mid)
                .FirstOrDefaultAsync() ?? throw ApiException.NotFound("battle_not_found");
            if (b.Status != "active") throw ApiException.Conflict("battle_finished");
            var st = JsonSerializer.Deserialize<BattleState>(b.StateJson, J)!;
            var events = new List<BattleEvent>();
            BattleEngine.Forfeit(st, 0, "surrender", events);
            ApplyState(b, st);
            db.BattleResults.Add(new BattleResult { BattleId = id, WinnerMembershipId = null, RewardsJson = "{}", At = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return ToDto(b, st, events, null);
        });
    }

    private static void ApplyState(Battle b, BattleState st)
    {
        b.StateJson = JsonSerializer.Serialize(st, J);
        b.Round = st.Round;
        b.UpdatedAt = DateTimeOffset.UtcNow;
        if (st.Finished)
        {
            b.Status = "finished"; b.WinnerSide = st.Winner; b.EndReason = st.EndReason; b.FinishedAt = DateTimeOffset.UtcNow;
        }
    }

    /// <summary>Grants rewards once (battle id is the ledger reference). Daily cap per stage limits farming.</summary>
    private static async Task<BattleRewardsDto> FinishPve(PetStarDb db, EconomyService eco, GameData data, Battle b, BattleState st,
        PveStage stage, int sid, long mid, AchievementService? ach = null)
    {
        var rewards = new BattleRewardsDto();
        if (st.Winner == 0)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var p = await db.PveProgress.FirstOrDefaultAsync(x => x.MembershipId == mid && x.StageId == stage.Id);
            if (p == null) { p = new PveProgress { MembershipId = mid, StageId = stage.Id, DailyDate = today }; db.PveProgress.Add(p); }
            if (p.DailyDate != today) { p.DailyDate = today; p.DailyRewardedClears = 0; }
            bool first = p.ClearCount == 0;
            p.ClearCount++;
            p.FirstClearedAt ??= DateTimeOffset.UtcNow;
            p.BestRounds = p.BestRounds == 0 ? st.Round : Math.Min(p.BestRounds, st.Round);
            string refId = "battle:" + b.Id.ToString("N");
            var grants = new List<PveRewards>();
            if (p.DailyRewardedClears < data.Pve.DailyRewardedClearsPerStage) { grants.Add(stage.Rewards); p.DailyRewardedClears++; }
            else rewards.DailyCapReached = true;
            if (first) { grants.Add(stage.FirstClearRewards); rewards.FirstClear = true; }
            foreach (var (gr, idx) in grants.Select((g, i) => (g, i)))
            {
                string r = refId + ":" + idx;
                foreach (var (cur, amt) in gr.Currencies) { await eco.Currency(mid, sid, cur, amt, "pve_reward", r); rewards.Currencies[cur] = rewards.Currencies.GetValueOrDefault(cur) + amt; }
                foreach (var (item, amt) in gr.Items) { await eco.Item(mid, sid, item, amt, "pve_reward", r); rewards.Items[item] = rewards.Items.GetValueOrDefault(item) + amt; }
                foreach (var (pack, amt) in gr.Packs) { await eco.Packs(mid, sid, pack, amt, "pve_reward", r); rewards.Packs[pack] = rewards.Packs.GetValueOrDefault(pack) + amt; }
                rewards.CardExp += gr.CardExp;
            }
            if (rewards.CardExp > 0)
            {
                var ids = st.Side(0).Select(u => u.CardId).ToList();
                var cards = await db.Cards.Where(c => ids.Contains(c.Id) && c.OwnerMembershipId == mid).ToListAsync();
                foreach (var c in cards)
                {
                    int before = c.Level;
                    (c.Level, c.Exp) = StatCalculator.AddExp(c.Level, c.Exp, rewards.CardExp);
                    c.UpdatedAt = DateTimeOffset.UtcNow;
                    if (c.Level > before)
                        db.CardProgress.Add(new CardProgressEvent { CardId = c.Id, Type = "level_up", DataJson = JsonSerializer.Serialize(new { from = before, to = c.Level, source = "battle:" + b.Id.ToString("N") }), At = DateTimeOffset.UtcNow });
                }
            }
        }
        db.BattleResults.Add(new BattleResult { BattleId = b.Id, WinnerMembershipId = st.Winner == 0 ? mid : null, RewardsJson = JsonSerializer.Serialize(rewards, J), At = DateTimeOffset.UtcNow });
        if (ach != null && st.Winner == 0) { await db.SaveChangesAsync(); await ach.Evaluate(mid, sid, "duel_win"); }
        return rewards;
    }

    public static BattleDto ToDto(Battle b, BattleState st, List<BattleEvent> events, BattleRewardsDto? rewards) => new()
    {
        Id = b.Id, Mode = b.Mode, StageId = b.StageId, Round = st.Round, MySide = 0, MyEnergy = st.Energy[0], OpponentEnergy = st.Energy[1],
        Status = b.Status, WinnerSide = st.Winner, EndReason = st.EndReason, RoundDeadline = b.RoundDeadline, LastEvents = events, Rewards = rewards,
        Units = st.Units.Select(u => new BattleUnitDto
        {
            Side = u.Side, Index = u.Index, Position = u.Position, CardId = u.CardId, FormId = u.FormId, Element = u.Element, Rarity = u.Rarity,
            Level = u.Level, MaxHp = u.MaxHp, Hp = u.Hp, Shield = u.Shield, Atk = u.Atk, Def = u.Def, Spd = u.Spd,
            SkillId = u.SkillId, UltimateId = u.UltimateId, PassiveId = u.PassiveId,
            Statuses = u.Statuses.Where(s => s.Remaining > 0).Select(s => s.Type).ToList(),
        }).ToList(),
    };
}
