using Microsoft.EntityFrameworkCore;
using PetStar.Api.Auth;
using PetStar.Api.Data;
using PetStar.Api.Infrastructure;
using PetStar.Contracts;

namespace PetStar.Api.Features;

public static class ServerEndpoints
{
    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api").RequireAuthorization().RequireRateLimiting("user");

        g.MapGet("/servers", async (HttpContext ctx, PetStarDb db) =>
        {
            var uid = ctx.User.AccountId();
            var servers = await db.Servers.AsNoTracking().Where(s => s.Status != ServerStatus.Merged).OrderBy(s => s.Id).ToListAsync();
            var mine = await db.Memberships.AsNoTracking().Where(m => m.AccountId == uid).ToListAsync();
            return servers.Select(s =>
            {
                var m = mine.FirstOrDefault(x => x.ServerId == s.Id);
                return new ServerDto { Id = s.Id, Code = s.Code, Name = s.Name, Status = s.Status, RegistrationOpen = s.RegistrationOpen, HasProgress = m != null, PlayerLevel = m?.Level, Nickname = m?.Nickname };
            });
        });

        // Selecting a server issues an access token bound to (server, membership). Every gameplay query is
        // scoped by these claims, never by client-supplied ids, which prevents cross-server data access.
        g.MapPost("/servers/{id:int}/enter", async (int id, EnterServerRequest? req, HttpContext ctx, PetStarDb db,
            TokenService tokens, EconomyService eco, GameData data, AuditService audit) =>
        {
            var uid = ctx.User.AccountId();
            var server = await db.Servers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id) ?? throw ApiException.NotFound("server_not_found");
            var acc = await db.Accounts.Include(a => a.Roles).AsNoTracking().FirstAsync(a => a.Id == uid);
            bool staff = acc.Roles.Any(r => r.Role is "owner" or "admin");
            var m = await db.Memberships.FirstOrDefaultAsync(x => x.AccountId == uid && x.ServerId == id);
            if (server.Status == ServerStatus.Merged) throw new ApiException(410, "server_merged");
            if (server.Status == ServerStatus.ComingSoon && !staff) throw new ApiException(503, "server_coming_soon");
            if (m == null)
            {
                if (!server.RegistrationOpen && !staff) throw ApiException.Forbidden("server_registration_closed");
                var nick = AuthEndpoints.ValidateDisplayName(req?.Nickname ?? acc.DisplayName);
                await using var tx = await db.Database.BeginTransactionAsync();
                m = new ServerMembership { AccountId = uid, ServerId = id, Nickname = nick, OriginServerId = id, CreatedAt = DateTimeOffset.UtcNow, LastSeenAt = DateTimeOffset.UtcNow };
                db.Memberships.Add(m);
                try { await db.SaveChangesAsync(); }
                catch (DbUpdateException) { throw ApiException.Conflict("nickname_taken"); }
                string refId = "membership:" + m.Id;
                foreach (var (cur, amt) in data.Economy.StarterCurrencies.Where(x => x.Value > 0))
                    await eco.Currency(m.Id, id, cur, amt, "starter_grant", refId);
                foreach (var (pack, qty) in data.Economy.StarterPacks.Where(x => x.Value > 0))
                    await eco.Packs(m.Id, id, pack, qty, "starter_grant", refId);
                audit.Add("server.join", uid, "server", id.ToString(), id);
                await db.SaveChangesAsync();
                await tx.CommitAsync();
            }
            else
            {
                m.LastSeenAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync();
            }
            var (token, exp) = tokens.CreateAccessToken(acc, ctx.User.SessionId(), m);
            return Results.Ok(new AuthResponse { Uid = uid, AccessToken = token, AccessTokenExpiresAt = exp, EmailVerified = acc.EmailVerified, ServerId = id });
        });

        var game = GameGroup(app);

        game.MapGet("/player", async (HttpContext ctx, PetStarDb db, EconomyService eco, GameData data) =>
        {
            var (sid, mid) = ctx.User.Player();
            var m = await db.Memberships.AsNoTracking().FirstAsync(x => x.Id == mid);
            var server = await db.Servers.AsNoTracking().FirstAsync(x => x.Id == sid);
            return new PlayerDto
            {
                Uid = m.AccountId, ServerId = sid, ServerCode = server.Code, Nickname = m.Nickname, Level = m.Level, Exp = m.Exp,
                Currencies = await eco.Balances(mid),
                Packs = await db.PackInventory.AsNoTracking().Where(p => p.MembershipId == mid).ToDictionaryAsync(p => p.PackTypeId, p => p.Quantity),
                Items = await db.Items.AsNoTracking().Where(p => p.MembershipId == mid).ToDictionaryAsync(p => p.ItemId, p => p.Quantity),
                CardCount = await db.Cards.CountAsync(c => c.OwnerMembershipId == mid && c.ServerId == sid),
                CodexDiscovered = await db.Codex.CountAsync(c => c.MembershipId == mid),
                CodexTotal = data.Catalog.Forms.Count,
            };
        });

        game.MapGet("/economy/ledger", async (string? currency, int? limit, HttpContext ctx, PetStarDb db) =>
        {
            var (sid, mid) = ctx.User.Player();
            var q = db.Ledger.AsNoTracking().Where(l => l.MembershipId == mid && l.ServerId == sid);
            if (!string.IsNullOrEmpty(currency)) q = q.Where(l => l.Currency == currency);
            var rows = await q.OrderByDescending(l => l.Id).Take(Math.Clamp(limit ?? 50, 1, 200)).ToListAsync();
            return rows.Select(l => new LedgerEntryDto { Id = l.Id, Currency = l.Currency, Delta = l.Delta, BalanceAfter = l.BalanceAfter, Reason = l.Reason, At = l.At });
        });
    }

    /// <summary>Routes that require a selected server that is currently online.</summary>
    public static RouteGroupBuilder GameGroup(WebApplication app) =>
        app.MapGroup("/api").RequireAuthorization("InServer").RequireRateLimiting("user").AddEndpointFilter<ServerGateFilter>();
}
