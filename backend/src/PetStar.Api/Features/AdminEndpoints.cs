using Microsoft.EntityFrameworkCore;
using PetStar.Api.Auth;
using PetStar.Api.Data;
using PetStar.Api.Infrastructure;
using PetStar.Contracts;

namespace PetStar.Api.Features;

/// <summary>Admin API (RBAC). Served under /api/admin and intended to be exposed only on the internal admin
/// hostname by Nginx (see deployment/nginx). Every change is written to admin_audit_logs.</summary>
public static class AdminEndpoints
{
    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/admin").RequireAuthorization("ServerAdmin").RequireRateLimiting("user");

        g.MapGet("/servers", async (PetStarDb db, IPresence presence) =>
        {
            var since = DateTimeOffset.UtcNow.AddHours(-24);
            var servers = await db.Servers.AsNoTracking().OrderBy(s => s.Id).ToListAsync();
            var members = await db.Memberships.AsNoTracking().GroupBy(m => m.ServerId)
                .Select(x => new { x.Key, N = x.Count(), Active = x.Count(m => m.LastSeenAt > since) }).ToDictionaryAsync(x => x.Key);
            var list = new List<AdminServerDto>();
            foreach (var s in servers)
                list.Add(new AdminServerDto
                {
                    Id = s.Id, Code = s.Code, Name = s.Name, Status = s.Status, RegistrationOpen = s.RegistrationOpen,
                    Members = members.TryGetValue(s.Id, out var m) ? m.N : 0, ActiveLast24h = m?.Active ?? 0, Online = await presence.OnlineCount(s.Id),
                });
            return list;
        });

        g.MapPost("/servers", async (AdminServerRequest req, HttpContext ctx, PetStarDb db, AuditService audit) =>
        {
            if (req.Id <= 0 || !System.Text.RegularExpressions.Regex.IsMatch(req.Code, "^PS-[0-9]{1,4}$")) throw ApiException.BadRequest("invalid_server");
            if (!ServerStatus.All.Contains(req.Status) || req.Status == ServerStatus.Merged) throw ApiException.BadRequest("invalid_status");
            if (await db.Servers.AnyAsync(s => s.Id == req.Id || s.Code == req.Code)) throw ApiException.Conflict("server_exists");
            var s = new GameServer { Id = req.Id, Code = req.Code, Name = req.Name, Status = req.Status, RegistrationOpen = req.RegistrationOpen, CreatedAt = DateTimeOffset.UtcNow, OpenedAt = req.Status == ServerStatus.Online ? DateTimeOffset.UtcNow : null };
            db.Servers.Add(s);
            audit.Add("admin.server.create", ctx.User.AccountId(), "server", s.Id.ToString(), s.Id, req);
            await db.SaveChangesAsync();
            return Results.Ok(s);
        });

        g.MapPatch("/servers/{id:int}", async (int id, AdminServerPatch req, HttpContext ctx, PetStarDb db, AuditService audit) =>
        {
            var s = await db.Servers.FirstOrDefaultAsync(x => x.Id == id) ?? throw ApiException.NotFound("server_not_found");
            var before = new { s.Status, s.RegistrationOpen, s.Name };
            if (req.Status != null)
            {
                // "merged" is only set by the audited merge tool, never directly
                if (!ServerStatus.All.Contains(req.Status) || req.Status == ServerStatus.Merged) throw ApiException.BadRequest("invalid_status");
                if (req.Status == ServerStatus.Online && s.OpenedAt == null) s.OpenedAt = DateTimeOffset.UtcNow;
                s.Status = req.Status;
            }
            if (req.RegistrationOpen != null) s.RegistrationOpen = req.RegistrationOpen.Value;
            if (req.Name != null) s.Name = req.Name.Trim();
            audit.Add("admin.server.update", ctx.User.AccountId(), "server", id.ToString(), id, new { before, after = new { s.Status, s.RegistrationOpen, s.Name } });
            await db.SaveChangesAsync();
            ServerGate.Forget(id);
            return Results.Ok(s);
        });

        g.MapGet("/audit", async (int? limit, string? action, PetStarDb db) =>
        {
            var q = db.AuditLogs.AsNoTracking();
            if (!string.IsNullOrEmpty(action)) q = q.Where(a => a.Action.StartsWith(action));
            return await q.OrderByDescending(a => a.Id).Take(Math.Clamp(limit ?? 100, 1, 500)).ToListAsync();
        });
    }
}
