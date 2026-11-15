using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PetStar.Api.Auth;
using PetStar.Api.Data;
using PetStar.Api.Infrastructure;
using PetStar.Rules;

namespace PetStar.Api.Features;

public sealed class RedeemRequest { public string Code { get; set; } = ""; }
public sealed class RewardCard { public string FormId { get; set; } = ""; public string Rarity { get; set; } = "N"; public string Quality { get; set; } = "B"; public string Finish { get; set; } = "Normal"; }
public sealed class RewardBundle
{
    public Dictionary<string, long> Currencies { get; set; } = new();
    public Dictionary<string, long> Items { get; set; } = new();
    public Dictionary<string, int> Packs { get; set; } = new();
    public List<RewardCard> Cards { get; set; } = new();
}
public sealed class CreateGiftcodeRequest { public string? Code { get; set; } public int? ServerId { get; set; } public RewardBundle Rewards { get; set; } = new(); public int MaxUses { get; set; } public DateTimeOffset? StartsAt { get; set; } public DateTimeOffset? EndsAt { get; set; } }
public sealed class CreateEventRequest { public string Key { get; set; } = ""; public int? ServerId { get; set; } public RewardBundle Rewards { get; set; } = new(); public DateTimeOffset StartsAt { get; set; } public DateTimeOffset EndsAt { get; set; } }
public sealed class BanRequest { public string Reason { get; set; } = ""; public int? Hours { get; set; } }
public sealed class RoleRequest { public string Role { get; set; } = ""; }

public static class RewardsEndpoints
{
    // Daily login cycle (7 days). Granted at most once per membership per UTC day (PK + ledger ref login:{day}).
    public static readonly RewardBundle[] LoginCycle =
    {
        new() { Currencies = { [Currency.MiniGreenBean] = 50 } },
        new() { Currencies = { [Currency.MiniGreenBean] = 60 } },
        new() { Currencies = { [Currency.MiniGreenBean] = 80 } },
        new() { Items = { [TrainingRules.ScrollItemId] = 1 } },
        new() { Packs = { ["basic_pack"] = 1 } },
        new() { Currencies = { [Currency.MiniGreenBean] = 150 } },
        new() { Currencies = { [Currency.RainbowStar] = 5 } },
    };

    private static readonly Regex CodeFormat = new("^[A-Z0-9-]{4,32}$");

    /// <summary>Grants a bundle through the ledger (idempotent per refId). Cards only when explicitly configured.</summary>
    public static async Task Grant(EconomyService eco, CardFactory factory, GameData data, long mid, int sid, RewardBundle r, string reason, string refId)
    {
        foreach (var (k, v) in r.Currencies) await eco.Currency(mid, sid, k, v, reason, refId);
        foreach (var (k, v) in r.Items) await eco.Item(mid, sid, k, v, reason, refId);
        foreach (var (k, v) in r.Packs) await eco.Packs(mid, sid, k, v, reason, refId);
        if (r.Cards.Count > 0)
            await factory.Create(sid, mid, r.Cards.Select(c => new NewCard(c.FormId, c.Rarity, c.Quality, c.Finish)).ToList(), reason == "giftcode" ? "giftcode" : "event", refId);
    }

    private static void Validate(GameData data, RewardBundle r)
    {
        if (r.Currencies.Keys.Any(k => !Currency.All.Contains(k)) || r.Currencies.Values.Any(v => v <= 0 || v > 1_000_000)) throw ApiException.BadRequest("invalid_reward_currency");
        if (r.Items.Values.Any(v => v <= 0 || v > 10_000) || r.Packs.Values.Any(v => v <= 0 || v > 100)) throw ApiException.BadRequest("invalid_reward_amount");
        if (r.Packs.Keys.Any(k => data.Catalog.PackType(k) == null)) throw ApiException.BadRequest("invalid_reward_pack");
        foreach (var c in r.Cards)
        {
            if (!data.Catalog.Forms.ContainsKey(c.FormId) || data.Catalog.Rarities.All(x => x.Id != c.Rarity) || data.Catalog.Qualities.All(x => x.Id != c.Quality) || !data.Catalog.Finishes.Contains(c.Finish))
                throw ApiException.BadRequest("invalid_reward_card");
        }
        if (r.Cards.Count > 5) throw ApiException.BadRequest("too_many_reward_cards");
    }

    public static void Map(WebApplication app)
    {
        var g = ServerEndpoints.GameGroup(app);

        g.MapPost("/giftcodes/redeem", async (RedeemRequest req, HttpContext ctx, PetStarDb db, GameData data, EconomyService eco, CardFactory factory, NotificationService notes) =>
        {
            var (sid, mid) = ctx.User.Player();
            var code = (req.Code ?? "").Trim().ToUpperInvariant();
            if (!CodeFormat.IsMatch(code)) throw ApiException.BadRequest("invalid_giftcode");
            await using var tx = await db.Database.BeginTransactionAsync();
            var gc = await db.Giftcodes.FromSqlRaw("SELECT * FROM giftcodes WHERE code = {0} FOR UPDATE", code).FirstOrDefaultAsync() ?? throw ApiException.NotFound("invalid_giftcode");
            var now = DateTimeOffset.UtcNow;
            if (!gc.Active || now < gc.StartsAt || now > gc.EndsAt) throw ApiException.Conflict("giftcode_expired");
            if (gc.ServerId != null && gc.ServerId != sid) throw ApiException.Conflict("giftcode_wrong_server");
            if (gc.MaxUses > 0 && gc.Uses >= gc.MaxUses) throw ApiException.Conflict("giftcode_used_up");
            int n = await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO giftcode_redemptions (giftcode_id, account_id, server_id, membership_id, at) VALUES ({0},{1},{2},{3},now()) ON CONFLICT DO NOTHING",
                gc.Id, ctx.User.AccountId(), sid, mid);
            if (n == 0) throw ApiException.Conflict("giftcode_already_redeemed");
            gc.Uses++;
            await db.SaveChangesAsync();
            var rewards = JsonSerializer.Deserialize<RewardBundle>(gc.RewardsJson, IdempotencyService.Json)!;
            await Grant(eco, factory, data, mid, sid, rewards, "giftcode", "giftcode:" + gc.Id);
            await notes.Notify(mid, "giftcode_redeemed", new { code, rewards }, "giftcode:" + gc.Id);
            await tx.CommitAsync();
            return Results.Ok(new { redeemed = true, rewards, currencies = await eco.Balances(mid) });
        }).RequireRateLimiting("giftcode");

        g.MapGet("/rewards/login", async (HttpContext ctx, PetStarDb db) =>
        {
            var (_, mid) = ctx.User.Player();
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var last = await db.LoginRewards.AsNoTracking().Where(l => l.MembershipId == mid).OrderByDescending(l => l.Day).FirstOrDefaultAsync();
            bool claimed = last?.Day == today;
            int nextDay = last == null || last.Day < today.AddDays(-1) ? 1 : claimed ? last.StreakDay : last.StreakDay % LoginCycle.Length + 1;
            return new { claimedToday = claimed, streakDay = claimed ? last!.StreakDay : nextDay, cycle = LoginCycle };
        });

        g.MapPost("/rewards/login/claim", async (HttpContext ctx, PetStarDb db, GameData data, EconomyService eco, CardFactory factory) =>
        {
            var (sid, mid) = ctx.User.Player();
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            await using var tx = await db.Database.BeginTransactionAsync();
            var last = await db.LoginRewards.AsNoTracking().Where(l => l.MembershipId == mid).OrderByDescending(l => l.Day).FirstOrDefaultAsync();
            int day = last == null || last.Day < today.AddDays(-1) ? 1 : last.StreakDay % LoginCycle.Length + 1;
            int n = await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO login_reward_claims (membership_id, day, streak_day, at) VALUES ({0},{1},{2},now()) ON CONFLICT DO NOTHING", mid, today, day);
            if (n == 0) throw ApiException.Conflict("login_reward_already_claimed");
            await Grant(eco, factory, data, mid, sid, LoginCycle[day - 1], "login_reward", "login:" + today.ToString("yyyy-MM-dd"));
            await tx.CommitAsync();
            return Results.Ok(new { streakDay = day, reward = LoginCycle[day - 1], currencies = await eco.Balances(mid) });
        });

        g.MapGet("/events", async (HttpContext ctx, PetStarDb db) =>
        {
            var (sid, mid) = ctx.User.Player();
            var now = DateTimeOffset.UtcNow;
            var list = await db.Events.AsNoTracking().Where(e => e.Active && (e.ServerId == null || e.ServerId == sid) && e.EndsAt > now).OrderBy(e => e.EndsAt).ToListAsync();
            var claimed = (await db.EventClaims.AsNoTracking().Where(c => c.MembershipId == mid).Select(c => c.EventId).ToListAsync()).ToHashSet();
            return list.Select(e => new { e.Id, e.Key, e.StartsAt, e.EndsAt, started = e.StartsAt <= now, rewards = JsonNode.Parse(e.RewardsJson), claimed = claimed.Contains(e.Id) });
        });

        g.MapPost("/events/{id:long}/claim", async (long id, HttpContext ctx, PetStarDb db, GameData data, EconomyService eco, CardFactory factory) =>
        {
            var (sid, mid) = ctx.User.Player();
            var now = DateTimeOffset.UtcNow;
            await using var tx = await db.Database.BeginTransactionAsync();
            var e = await db.Events.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.Active && (x.ServerId == null || x.ServerId == sid)) ?? throw ApiException.NotFound("event_not_found");
            if (now < e.StartsAt || now > e.EndsAt) throw ApiException.Conflict("event_not_active");
            int n = await db.Database.ExecuteSqlRawAsync("INSERT INTO event_claims (event_id, membership_id, at) VALUES ({0},{1},now()) ON CONFLICT DO NOTHING", id, mid);
            if (n == 0) throw ApiException.Conflict("event_already_claimed");
            await Grant(eco, factory, data, mid, sid, JsonSerializer.Deserialize<RewardBundle>(e.RewardsJson, IdempotencyService.Json)!, "event_reward", "event:" + id);
            await tx.CommitAsync();
            return Results.Ok(new { claimed = true, currencies = await eco.Balances(mid) });
        });

        // ---------------- realtime ----------------
        g.MapPost("/realtime/ticket", async (HttpContext ctx, TicketStore tickets) =>
        {
            var (sid, mid) = ctx.User.Player();
            return Results.Ok(new { ticket = await tickets.Issue(ctx.User.AccountId(), sid, mid), expiresIn = 60 });
        });
        var wsOrigins = (app.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>()).Where(o => !string.IsNullOrWhiteSpace(o)).ToArray();
        app.Map("/ws", async (HttpContext ctx, TicketStore tickets, RealtimeHub hub) =>
        {
            if (!ctx.WebSockets.IsWebSocketRequest) { ctx.Response.StatusCode = 400; return; }
            var origin = ctx.Request.Headers.Origin.ToString();
            if (!string.IsNullOrEmpty(origin) && !wsOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase)) { ctx.Response.StatusCode = 403; return; }
            var t = await tickets.Consume(ctx.Request.Query["ticket"].ToString());
            if (t == null) { ctx.Response.StatusCode = 401; return; }
            using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
            await hub.Run(ws, t, ctx.RequestAborted);
        });

        // ---------------- admin: giftcodes, events, accounts ----------------
        var admin = app.MapGroup("/api/admin").RequireAuthorization("ServerAdmin").RequireRateLimiting("user");

        admin.MapPost("/giftcodes", async (CreateGiftcodeRequest req, HttpContext ctx, PetStarDb db, GameData data, AuditService audit) =>
        {
            Validate(data, req.Rewards);
            if (req.Rewards.Cards.Count > 0 && !ctx.User.IsInRole("owner")) throw ApiException.Forbidden("owner_required_for_card_rewards");
            if (req.MaxUses < 0 || req.MaxUses > 1_000_000) throw ApiException.BadRequest("invalid_max_uses");
            var code = string.IsNullOrWhiteSpace(req.Code) ? "PS-" + SerialGenerator.Next()[..10] : req.Code.Trim().ToUpperInvariant();
            if (!CodeFormat.IsMatch(code)) throw ApiException.BadRequest("invalid_giftcode");
            var gc = new Giftcode
            {
                Code = code, ServerId = req.ServerId, RewardsJson = JsonSerializer.Serialize(req.Rewards, IdempotencyService.Json), MaxUses = req.MaxUses,
                StartsAt = req.StartsAt ?? DateTimeOffset.UtcNow, EndsAt = req.EndsAt ?? DateTimeOffset.UtcNow.AddDays(30), CreatedBy = ctx.User.AccountId(), CreatedAt = DateTimeOffset.UtcNow,
            };
            db.Giftcodes.Add(gc);
            audit.Add("admin.giftcode.create", ctx.User.AccountId(), "giftcode", code, req.ServerId, req);
            try { await db.SaveChangesAsync(); } catch (DbUpdateException) { throw ApiException.Conflict("giftcode_exists"); }
            return Results.Ok(new { gc.Id, gc.Code, gc.StartsAt, gc.EndsAt, gc.MaxUses });
        });

        admin.MapGet("/giftcodes", async (PetStarDb db) =>
            (await db.Giftcodes.AsNoTracking().OrderByDescending(x => x.Id).Take(200).ToListAsync())
                .Select(x => new { x.Id, x.Code, x.ServerId, rewards = JsonNode.Parse(x.RewardsJson), x.MaxUses, x.Uses, x.StartsAt, x.EndsAt, x.Active }));

        admin.MapPost("/giftcodes/{id:long}/deactivate", async (long id, HttpContext ctx, PetStarDb db, AuditService audit) =>
        {
            int n = await db.Giftcodes.Where(x => x.Id == id).ExecuteUpdateAsync(u => u.SetProperty(x => x.Active, false));
            audit.Add("admin.giftcode.deactivate", ctx.User.AccountId(), "giftcode", id.ToString());
            await db.SaveChangesAsync();
            return n == 0 ? throw ApiException.NotFound() : Results.Ok(new { active = false });
        });

        admin.MapPost("/events", async (CreateEventRequest req, HttpContext ctx, PetStarDb db, GameData data, AuditService audit) =>
        {
            Validate(data, req.Rewards);
            if (req.Rewards.Cards.Count > 0 && !ctx.User.IsInRole("owner")) throw ApiException.Forbidden("owner_required_for_card_rewards");
            if (!Regex.IsMatch(req.Key ?? "", "^[a-z0-9_]{3,40}$") || req.EndsAt <= req.StartsAt) throw ApiException.BadRequest("invalid_event");
            var e = new GameEvent { Key = req.Key!, ServerId = req.ServerId, RewardsJson = JsonSerializer.Serialize(req.Rewards, IdempotencyService.Json), StartsAt = req.StartsAt, EndsAt = req.EndsAt, CreatedBy = ctx.User.AccountId(), CreatedAt = DateTimeOffset.UtcNow };
            db.Events.Add(e);
            audit.Add("admin.event.create", ctx.User.AccountId(), "event", req.Key, req.ServerId, req);
            await db.SaveChangesAsync();
            return Results.Ok(new { e.Id });
        });

        admin.MapGet("/events", async (PetStarDb db) =>
            (await db.Events.AsNoTracking().OrderByDescending(x => x.Id).Take(200).ToListAsync())
                .Select(e => new { e.Id, e.Key, e.ServerId, e.StartsAt, e.EndsAt, e.Active, rewards = JsonNode.Parse(e.RewardsJson), claims = db.EventClaims.Count(c => c.EventId == e.Id) }));

        admin.MapPost("/events/{id:long}/deactivate", async (long id, HttpContext ctx, PetStarDb db, AuditService audit) =>
        {
            int n = await db.Events.Where(x => x.Id == id).ExecuteUpdateAsync(u => u.SetProperty(x => x.Active, false));
            audit.Add("admin.event.deactivate", ctx.User.AccountId(), "event", id.ToString());
            await db.SaveChangesAsync();
            return n == 0 ? throw ApiException.NotFound() : Results.Ok(new { active = false });
        });

        admin.MapGet("/accounts", async (string? q, PetStarDb db) =>
        {
            var query = db.Accounts.AsNoTracking().Include(a => a.Roles).AsQueryable();
            if (!string.IsNullOrWhiteSpace(q))
            {
                q = q.Trim();
                query = long.TryParse(q, out var uid) ? query.Where(a => a.Id == uid)
                    : query.Where(a => a.EmailNormalized.StartsWith(q.ToUpperInvariant()) || EF.Functions.ILike(a.DisplayName, q.Replace("%", "") + "%"));
            }
            return (await query.OrderByDescending(a => a.Id).Take(50).ToListAsync())
                .Select(a => new { uid = a.Id, a.Email, a.DisplayName, a.Status, a.BanReason, a.BannedUntil, a.EmailVerified, a.CreatedAt, a.LastLoginAt, roles = a.Roles.Select(r => r.Role) });
        });

        admin.MapPost("/accounts/{uid:long}/ban", async (long uid, BanRequest req, HttpContext ctx, PetStarDb db, AuditService audit) =>
        {
            var acc = await db.Accounts.Include(a => a.Roles).FirstOrDefaultAsync(a => a.Id == uid) ?? throw ApiException.NotFound("player_not_found");
            if (acc.Roles.Any(r => r.Role == "owner")) throw ApiException.Forbidden("cannot_ban_owner");
            if (string.IsNullOrWhiteSpace(req.Reason)) throw ApiException.BadRequest("reason_required");
            acc.Status = AccountStatus.Banned; acc.BanReason = req.Reason.Trim(); acc.BannedUntil = req.Hours is int h ? DateTimeOffset.UtcNow.AddHours(h) : null;
            await db.Sessions.Where(s => s.AccountId == uid && s.RevokedAt == null).ExecuteUpdateAsync(u => u.SetProperty(s => s.RevokedAt, DateTimeOffset.UtcNow).SetProperty(s => s.RevokeReason, "banned"));
            SessionValidator.ForgetAccount(uid);
            audit.Add("admin.account.ban", ctx.User.AccountId(), "account", uid.ToString(), null, req);
            await db.SaveChangesAsync();
            return Results.Ok(new { banned = true, acc.BannedUntil });
        });

        admin.MapPost("/accounts/{uid:long}/unban", async (long uid, HttpContext ctx, PetStarDb db, AuditService audit) =>
        {
            var acc = await db.Accounts.FirstOrDefaultAsync(a => a.Id == uid) ?? throw ApiException.NotFound("player_not_found");
            acc.Status = AccountStatus.Active; acc.BanReason = null; acc.BannedUntil = null;
            audit.Add("admin.account.unban", ctx.User.AccountId(), "account", uid.ToString());
            await db.SaveChangesAsync();
            return Results.Ok(new { banned = false });
        });

        var owner = app.MapGroup("/api/admin").RequireAuthorization("Owner").RequireRateLimiting("user");
        owner.MapPost("/accounts/{uid:long}/roles", async (long uid, RoleRequest req, HttpContext ctx, PetStarDb db, AuditService audit) =>
        {
            if (req.Role is not ("admin" or "gm" or "moderator")) throw ApiException.BadRequest("invalid_role");
            if (!await db.Accounts.AnyAsync(a => a.Id == uid)) throw ApiException.NotFound("player_not_found");
            await db.Database.ExecuteSqlRawAsync("INSERT INTO account_roles (account_id, role, granted_at, granted_by) VALUES ({0},{1},now(),{2}) ON CONFLICT DO NOTHING", uid, req.Role, ctx.User.AccountId());
            audit.Add("owner.role.grant", ctx.User.AccountId(), "account", uid.ToString(), null, req);
            await db.SaveChangesAsync();
            return Results.Ok(new { granted = req.Role });
        });
        owner.MapDelete("/accounts/{uid:long}/roles/{role}", async (long uid, string role, HttpContext ctx, PetStarDb db, AuditService audit) =>
        {
            if (role == "owner") throw ApiException.BadRequest("cannot_revoke_owner");
            await db.AccountRoles.Where(r => r.AccountId == uid && r.Role == role).ExecuteDeleteAsync();
            audit.Add("owner.role.revoke", ctx.User.AccountId(), "account", uid.ToString(), null, new { role });
            await db.SaveChangesAsync();
            return Results.Ok(new { revoked = role });
        });

        var mod = app.MapGroup("/api/admin").RequireAuthorization("Moderation").RequireRateLimiting("user");
        mod.MapPost("/chat/{id:long}/hide", async (long id, HttpContext ctx, PetStarDb db, AuditService audit) =>
        {
            var m = await db.ChatMessages.FirstOrDefaultAsync(x => x.Id == id) ?? throw ApiException.NotFound();
            m.Status = "hidden";
            db.ModerationActions.Add(new ModerationAction { ServerId = m.ServerId, ModeratorAccountId = ctx.User.AccountId(), Action = "hide_chat", TargetType = "chat", TargetId = id, At = DateTimeOffset.UtcNow });
            audit.Add("moderation.chat.hide", ctx.User.AccountId(), "chat", id.ToString(), m.ServerId);
            await db.SaveChangesAsync();
            return Results.Ok(new { hidden = true });
        });
    }
}
