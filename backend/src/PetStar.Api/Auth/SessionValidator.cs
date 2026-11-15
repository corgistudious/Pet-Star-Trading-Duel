using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using PetStar.Api.Data;
using PetStar.Api.Infrastructure;

namespace PetStar.Api.Auth;

/// <summary>Rejects access tokens whose session was revoked or whose account is banned (checked with a short cache),
/// and rejects gameplay on servers that are not online.</summary>
public static class SessionValidator
{
    private static readonly ConcurrentDictionary<Guid, (bool ok, DateTimeOffset until, long account)> Cache = new();
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(20);

    public static void Forget(Guid sid) => Cache.TryRemove(sid, out _);

    /// <summary>Drops cached session checks of an account (password reset, ban, token-theft detection).</summary>
    public static void ForgetAccount(long accountId)
    {
        foreach (var kv in Cache.Where(kv => kv.Value.account == accountId).ToList()) Cache.TryRemove(kv.Key, out _);
    }

    public static async Task OnTokenValidated(TokenValidatedContext ctx)
    {
        var sidStr = ctx.Principal?.FindFirst(ClaimsExtensions.SessionClaim)?.Value;
        if (!Guid.TryParse(sidStr, out var sid)) { ctx.Fail("no_session"); return; }
        var now = DateTimeOffset.UtcNow;
        if (Cache.TryGetValue(sid, out var c) && c.until > now)
        {
            if (!c.ok) ctx.Fail("session_revoked");
            return;
        }
        var db = ctx.HttpContext.RequestServices.GetRequiredService<PetStarDb>();
        var row = await db.Sessions.AsNoTracking().Where(s => s.Id == sid)
            .Join(db.Accounts, s => s.AccountId, a => a.Id, (s, a) => new { s.RevokedAt, s.ExpiresAt, a.Status, a.BannedUntil, a.Id })
            .FirstOrDefaultAsync();
        bool ok = row != null && row.RevokedAt == null && row.ExpiresAt > now &&
                  !(row.Status == AccountStatus.Banned && (row.BannedUntil == null || row.BannedUntil > now));
        Cache[sid] = (ok, now + Ttl, row?.Id ?? 0);
        if (!ok) ctx.Fail("session_revoked");
    }
}

/// <summary>Server status gate for gameplay routes (cached a few seconds). Admins bypass maintenance.</summary>
public sealed class ServerGate(PetStarDb db)
{
    private static readonly ConcurrentDictionary<int, (string status, DateTimeOffset until)> Cache = new();
    public static void Forget(int id) => Cache.TryRemove(id, out _);

    public async Task EnsureOnline(HttpContext ctx)
    {
        var (serverId, _) = ctx.User.Player();
        var now = DateTimeOffset.UtcNow;
        if (!Cache.TryGetValue(serverId, out var c) || c.until < now)
        {
            var s = await db.Servers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == serverId) ?? throw ApiException.NotFound("server_not_found");
            c = (s.Status, now.AddSeconds(5));
            Cache[serverId] = c;
        }
        if (c.status == ServerStatus.Online) return;
        if (c.status == ServerStatus.Maintenance && (ctx.User.IsInRole("owner") || ctx.User.IsInRole("admin"))) return;
        throw new ApiException(503, c.status == ServerStatus.Maintenance ? "server_maintenance" : "server_unavailable");
    }
}

public sealed class ServerGateFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var sp = ctx.HttpContext.RequestServices;
        await sp.GetRequiredService<ServerGate>().EnsureOnline(ctx.HttpContext);
        var (serverId, membershipId) = ctx.HttpContext.User.Player();
        await sp.GetRequiredService<IPresence>().Touch(serverId, membershipId);
        return await next(ctx);
    }
}
