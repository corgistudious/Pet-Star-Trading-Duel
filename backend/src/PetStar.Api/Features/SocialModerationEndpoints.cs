using Microsoft.EntityFrameworkCore;
using PetStar.Api.Auth;
using PetStar.Api.Data;
using PetStar.Api.Infrastructure;

namespace PetStar.Api.Features;

public sealed class QueueDecisionRequest { public string? Note { get; set; } }

/// <summary>Moderation API for the future admin panel (no UI in this release): the review queue fed by auto-moderation
/// and by reports, decisions with a log, and the comment/interaction lock. Moderation role required (server-checked).</summary>
public static class SocialModerationEndpoints
{
    public const int AutoHideReports = 3;

    public static void Map(WebApplication app)
    {
        var mod = app.MapGroup("/api/admin/social").RequireAuthorization("Moderation").RequireRateLimiting("user");

        mod.MapGet("/queue", async (string? status, int? serverId, PetStarDb db) =>
        {
            var q = db.ModerationQueue.AsNoTracking().Where(x => x.Status == (status ?? "pending"));
            if (serverId != null) q = q.Where(x => x.ServerId == serverId);
            var rows = await q.OrderBy(x => x.Id).Take(100).ToListAsync();
            var postIds = rows.Where(r => r.TargetType == "post").Select(r => r.TargetId).ToList();
            var commentIds = rows.Where(r => r.TargetType == "comment").Select(r => r.TargetId).ToList();
            var chatIds = rows.Where(r => r.TargetType == "chat").Select(r => r.TargetId).ToList();
            var posts = await db.Posts.AsNoTracking().Where(p => postIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Caption);
            var comments = await db.Comments.AsNoTracking().Where(c => commentIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Text);
            var chats = await db.ChatMessages.AsNoTracking().Where(c => chatIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Text);
            return rows.Select(r => new
            {
                r.Id, r.ServerId, r.TargetType, r.TargetId, r.AuthorMembershipId, flags = r.Flags.Split(',', StringSplitOptions.RemoveEmptyEntries), r.Status, r.CreatedAt, r.Note,
                text = r.TargetType switch { "post" => posts.GetValueOrDefault(r.TargetId), "comment" => comments.GetValueOrDefault(r.TargetId), "chat" => chats.GetValueOrDefault(r.TargetId), _ => null },
            });
        });

        mod.MapPost("/queue/{id:long}/{decision}", async (long id, string decision, QueueDecisionRequest req, HttpContext ctx, PetStarDb db, AuditService audit, NotificationService notes, RealtimeHub hub) =>
        {
            if (decision is not ("approve" or "reject")) throw ApiException.BadRequest("invalid_decision");
            await using var tx = await db.Database.BeginTransactionAsync();
            var item = await db.ModerationQueue.FromSqlRaw("SELECT * FROM moderation_queue WHERE id = {0} FOR UPDATE", id).FirstOrDefaultAsync() ?? throw ApiException.NotFound();
            if (item.Status != "pending") throw ApiException.Conflict("already_reviewed");
            bool ok = decision == "approve";
            string status = ok ? "visible" : "hidden";
            switch (item.TargetType)
            {
                case "post":
                    await db.Posts.Where(p => p.Id == item.TargetId && p.Status != "deleted").ExecuteUpdateAsync(u => u.SetProperty(p => p.Status, status));
                    break;
                case "comment":
                {
                    var c = await db.Comments.FirstOrDefaultAsync(x => x.Id == item.TargetId);
                    if (c != null && c.Status != "deleted")
                    {
                        bool wasCounted = c.Status == "visible";
                        c.Status = status;
                        await db.SaveChangesAsync();
                        int delta = (ok ? 1 : 0) - (wasCounted ? 1 : 0);
                        if (delta != 0) await db.Posts.Where(p => p.Id == c.PostId).ExecuteUpdateAsync(u => u.SetProperty(p => p.CommentCount, p => p.CommentCount + delta));
                    }
                    break;
                }
                case "chat":
                    await db.ChatMessages.Where(m => m.Id == item.TargetId).ExecuteUpdateAsync(u => u.SetProperty(m => m.Status, status));
                    break;
            }
            item.Status = ok ? "approved" : "rejected"; item.ReviewedByAccountId = ctx.User.AccountId(); item.ReviewedAt = DateTimeOffset.UtcNow; item.Note = req.Note;
            db.ModerationActions.Add(new ModerationAction { ServerId = item.ServerId, ModeratorAccountId = ctx.User.AccountId(), Action = (ok ? "approve_" : "reject_") + item.TargetType, TargetType = item.TargetType, TargetId = item.TargetId, Reason = req.Note ?? item.Flags, At = DateTimeOffset.UtcNow });
            audit.Add("social.queue." + decision, ctx.User.AccountId(), item.TargetType, item.TargetId.ToString(), item.ServerId);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            await notes.Notify(item.AuthorMembershipId, ok ? "content_approved" : "content_rejected", new { type = item.TargetType, id = item.TargetId }, $"modq:{item.Id}");
            return Results.Ok(new { item.Status });
        });

        mod.MapGet("/log", async (int? serverId, PetStarDb db) =>
            await db.ModerationActions.AsNoTracking().Where(a => serverId == null || a.ServerId == serverId).OrderByDescending(a => a.Id).Take(200).ToListAsync());
    }

    /// <summary>Called after a report is stored: enough distinct reports hide the target and queue it for review.</summary>
    public static async Task OnReport(PetStarDb db, int sid, string type, long id)
    {
        if (type is not ("post" or "comment" or "chat")) return;
        int open = await db.Reports.CountAsync(r => r.TargetType == type && r.TargetId == id && r.Status == "open");
        if (open < AutoHideReports) return;
        long author;
        switch (type)
        {
            case "post":
                author = await db.Posts.Where(p => p.Id == id).Select(p => p.AuthorMembershipId).FirstAsync();
                await db.Posts.Where(p => p.Id == id && p.Status == "visible").ExecuteUpdateAsync(u => u.SetProperty(p => p.Status, "hidden"));
                break;
            case "comment":
            {
                var c = await db.Comments.FirstAsync(x => x.Id == id);
                author = c.AuthorMembershipId;
                if (c.Status == "visible") { c.Status = "hidden"; await db.SaveChangesAsync(); await db.Posts.Where(p => p.Id == c.PostId).ExecuteUpdateAsync(u => u.SetProperty(p => p.CommentCount, p => p.CommentCount - 1)); }
                break;
            }
            default:
                author = await db.ChatMessages.Where(m => m.Id == id).Select(m => m.AuthorMembershipId).FirstAsync();
                await db.ChatMessages.Where(m => m.Id == id).ExecuteUpdateAsync(u => u.SetProperty(m => m.Status, "hidden"));
                break;
        }
        await SocialAccess.Hold(db, sid, type, id, author, new[] { "report" });
    }
}
