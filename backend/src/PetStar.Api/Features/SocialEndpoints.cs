using Microsoft.EntityFrameworkCore;
using PetStar.Api.Data;
using PetStar.Api.Infrastructure;

namespace PetStar.Api.Features;

public sealed class ReportRequest { public string TargetType { get; set; } = ""; public long TargetId { get; set; } public string Reason { get; set; } = ""; public string? Details { get; set; } }
public sealed class ShowcaseRequest { public List<long> CardIds { get; set; } = new(); }
public sealed class FriendRequestBody { public long Uid { get; set; } }

public static class SocialEndpoints
{
    private static readonly string[] ReportReasons = { "spam", "harassment", "inappropriate", "scam", "cheating", "other" };

    /// <summary>Resolves a public UID to the membership on the caller's server (no cross-server lookups).</summary>
    public static async Task<ServerMembership> Member(PetStarDb db, long uid, int sid) =>
        await db.Memberships.AsNoTracking().FirstOrDefaultAsync(m => m.AccountId == uid && m.ServerId == sid) ?? throw ApiException.NotFound("player_not_found");

    public static void Map(WebApplication app)
    {
        var g = ServerEndpoints.GameGroup(app);

        g.MapGet("/players/search", async (string q, HttpContext ctx, PetStarDb db) =>
        {
            var (sid, mid) = ctx.User.Player();
            q = (q ?? "").Trim();
            if (q.Length < 2) throw ApiException.BadRequest("query_too_short");
            var blocked = await MarketEndpoints.BlockedIds(db, mid);
            var query = db.Memberships.AsNoTracking().Where(m => m.ServerId == sid && !blocked.Contains(m.Id));
            query = long.TryParse(q, out var uid) ? query.Where(m => m.AccountId == uid) : query.Where(m => EF.Functions.ILike(m.Nickname, q.Replace("%", "") + "%"));
            return await query.OrderBy(m => m.Nickname).Take(20).Select(m => new { uid = m.AccountId, m.Nickname, m.Level }).ToListAsync();
        });

        // Public profile: no email, IP or private data.
        g.MapGet("/players/{uid:long}", async (long uid, HttpContext ctx, PetStarDb db, GameData data) =>
        {
            var (sid, mid) = ctx.User.Player();
            var m = await Member(db, uid, sid);
            if (await db.Blocks.AnyAsync(b => b.BlockerMembershipId == m.Id && b.BlockedMembershipId == mid)) throw ApiException.NotFound("player_not_found");
            var acc = await db.Accounts.AsNoTracking().FirstAsync(a => a.Id == uid);
            var showcase = await db.Showcase.AsNoTracking().Where(s => s.MembershipId == m.Id).OrderBy(s => s.Slot)
                .Join(db.Cards.AsNoTracking(), s => s.CardId, c => c.Id, (s, c) => c).ToListAsync();
            var fr = await db.Friendships.AsNoTracking().FirstOrDefaultAsync(f => (f.RequesterMembershipId == mid && f.AddresseeMembershipId == m.Id) || (f.RequesterMembershipId == m.Id && f.AddresseeMembershipId == mid));
            return new
            {
                uid, m.Nickname, m.Level, acc.AvatarId, acc.AvatarFrameId, acc.TitleId,
                codexDiscovered = await db.Codex.CountAsync(c => c.MembershipId == m.Id), codexTotal = data.Catalog.Forms.Count,
                achievements = await db.Achievements.CountAsync(a => a.MembershipId == m.Id),
                followers = await db.Follows.CountAsync(f => f.FolloweeMembershipId == m.Id),
                following = await db.Follows.CountAsync(f => f.FollowerMembershipId == m.Id),
                isFollowing = await db.Follows.AnyAsync(f => f.FollowerMembershipId == mid && f.FolloweeMembershipId == m.Id),
                friendship = fr == null ? "none" : fr.Status == "accepted" ? "friends" : fr.RequesterMembershipId == mid ? "request_sent" : "request_received",
                blockedByMe = await db.Blocks.AnyAsync(b => b.BlockerMembershipId == mid && b.BlockedMembershipId == m.Id),
                showcase = showcase.Select(c => { var d = data.ToDto(c); d.Id = 0; return d; }),
                isMe = m.Id == mid,
            };
        });

        g.MapPut("/profile/showcase", async (ShowcaseRequest req, HttpContext ctx, PetStarDb db) =>
        {
            var (sid, mid) = ctx.User.Player();
            if (req.CardIds.Count > 5 || req.CardIds.Distinct().Count() != req.CardIds.Count) throw ApiException.BadRequest("showcase_invalid");
            if (await db.Cards.CountAsync(c => req.CardIds.Contains(c.Id) && c.OwnerMembershipId == mid && c.ServerId == sid) != req.CardIds.Count)
                throw ApiException.BadRequest("card_not_owned");
            await using var tx = await db.Database.BeginTransactionAsync();
            await db.Showcase.Where(s => s.MembershipId == mid).ExecuteDeleteAsync();
            for (int i = 0; i < req.CardIds.Count; i++) db.Showcase.Add(new ShowcaseSlot { MembershipId = mid, Slot = i, CardId = req.CardIds[i] });
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return Results.Ok(new { saved = req.CardIds.Count });
        });

        g.MapPost("/players/{uid:long}/follow", async (long uid, HttpContext ctx, PetStarDb db, NotificationService notes, ContentGuard guard) =>
        {
            var (sid, mid) = ctx.User.Player();
            await guard.EnsureAllowed(ctx.User.AccountId(), "social");
            var m = await Member(db, uid, sid);
            if (m.Id == mid) throw ApiException.BadRequest("cannot_follow_self");
            if (await MarketEndpoints.IsBlocked(db, mid, m.Id)) throw ApiException.Forbidden("user_blocked");
            int n = await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO user_follows (follower_membership_id, followee_membership_id, server_id, at) VALUES ({0}, {1}, {2}, now()) ON CONFLICT DO NOTHING", mid, m.Id, sid);
            if (n == 1) await notes.Notify(m.Id, "new_follower", new { uid = ctx.User.AccountId() }, $"follow:{mid}");
            return Results.Ok(new { following = true });
        });

        g.MapDelete("/players/{uid:long}/follow", async (long uid, HttpContext ctx, PetStarDb db) =>
        {
            var (sid, mid) = ctx.User.Player();
            var m = await Member(db, uid, sid);
            await db.Follows.Where(f => f.FollowerMembershipId == mid && f.FolloweeMembershipId == m.Id).ExecuteDeleteAsync();
            return Results.Ok(new { following = false });
        });

        g.MapGet("/friends", async (HttpContext ctx, PetStarDb db) =>
        {
            var (sid, mid) = ctx.User.Player();
            var rows = await db.Friendships.AsNoTracking().Where(f => f.RequesterMembershipId == mid || f.AddresseeMembershipId == mid).ToListAsync();
            var others = rows.Select(f => f.RequesterMembershipId == mid ? f.AddresseeMembershipId : f.RequesterMembershipId).ToList();
            var members = await db.Memberships.AsNoTracking().Where(m => others.Contains(m.Id)).ToDictionaryAsync(m => m.Id);
            return rows.Select(f =>
            {
                var o = members[f.RequesterMembershipId == mid ? f.AddresseeMembershipId : f.RequesterMembershipId];
                return new { requestId = f.Id, uid = o.AccountId, o.Nickname, o.Level, status = f.Status == "accepted" ? "friends" : f.RequesterMembershipId == mid ? "outgoing" : "incoming" };
            });
        });

        g.MapPost("/friends/requests", async (FriendRequestBody req, HttpContext ctx, PetStarDb db, NotificationService notes, ContentGuard guard) =>
        {
            var (sid, mid) = ctx.User.Player();
            await guard.EnsureAllowed(ctx.User.AccountId(), "social");
            var m = await Member(db, req.Uid, sid);
            if (m.Id == mid) throw ApiException.BadRequest("cannot_friend_self");
            if (await MarketEndpoints.IsBlocked(db, mid, m.Id)) throw ApiException.Forbidden("user_blocked");
            if (await db.Friendships.AnyAsync(f => (f.RequesterMembershipId == mid && f.AddresseeMembershipId == m.Id) || (f.RequesterMembershipId == m.Id && f.AddresseeMembershipId == mid)))
                throw ApiException.Conflict("friend_request_exists");
            if (await db.Friendships.CountAsync(f => f.RequesterMembershipId == mid && f.Status == "pending") >= 50) throw ApiException.Conflict("too_many_pending_requests");
            var f = new Friendship { ServerId = sid, RequesterMembershipId = mid, AddresseeMembershipId = m.Id, CreatedAt = DateTimeOffset.UtcNow };
            db.Friendships.Add(f);
            try { await db.SaveChangesAsync(); } catch (DbUpdateException) { throw ApiException.Conflict("friend_request_exists"); }
            await notes.Notify(m.Id, "friend_request", new { uid = ctx.User.AccountId(), requestId = f.Id }, "friend_req:" + f.Id);
            return Results.Ok(new { requestId = f.Id });
        });

        g.MapPost("/friends/requests/{id:long}/accept", async (long id, HttpContext ctx, PetStarDb db, NotificationService notes) =>
        {
            var (_, mid) = ctx.User.Player();
            var f = await db.Friendships.FirstOrDefaultAsync(x => x.Id == id && x.AddresseeMembershipId == mid && x.Status == "pending") ?? throw ApiException.NotFound();
            f.Status = "accepted"; f.AcceptedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            await notes.Notify(f.RequesterMembershipId, "friend_accepted", new { uid = ctx.User.AccountId() }, "friend_ok:" + f.Id);
            return Results.Ok(new { friends = true });
        });

        g.MapDelete("/friends/{uid:long}", async (long uid, HttpContext ctx, PetStarDb db) =>
        {
            var (sid, mid) = ctx.User.Player();
            var m = await Member(db, uid, sid);
            await db.Friendships.Where(f => (f.RequesterMembershipId == mid && f.AddresseeMembershipId == m.Id) || (f.RequesterMembershipId == m.Id && f.AddresseeMembershipId == mid)).ExecuteDeleteAsync();
            return Results.Ok(new { removed = true });
        });

        g.MapPost("/players/{uid:long}/block", async (long uid, HttpContext ctx, PetStarDb db) =>
        {
            var (sid, mid) = ctx.User.Player();
            var m = await Member(db, uid, sid);
            if (m.Id == mid) throw ApiException.BadRequest("cannot_block_self");
            await using var tx = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlRawAsync("INSERT INTO user_blocks (blocker_membership_id, blocked_membership_id, at) VALUES ({0}, {1}, now()) ON CONFLICT DO NOTHING", mid, m.Id);
            await db.Follows.Where(f => (f.FollowerMembershipId == mid && f.FolloweeMembershipId == m.Id) || (f.FollowerMembershipId == m.Id && f.FolloweeMembershipId == mid)).ExecuteDeleteAsync();
            await db.Friendships.Where(f => (f.RequesterMembershipId == mid && f.AddresseeMembershipId == m.Id) || (f.RequesterMembershipId == m.Id && f.AddresseeMembershipId == mid)).ExecuteDeleteAsync();
            await tx.CommitAsync();
            return Results.Ok(new { blocked = true });
        });

        g.MapDelete("/players/{uid:long}/block", async (long uid, HttpContext ctx, PetStarDb db) =>
        {
            var (sid, mid) = ctx.User.Player();
            var m = await Member(db, uid, sid);
            await db.Blocks.Where(b => b.BlockerMembershipId == mid && b.BlockedMembershipId == m.Id).ExecuteDeleteAsync();
            return Results.Ok(new { blocked = false });
        });

        g.MapPost("/players/{uid:long}/mute", async (long uid, HttpContext ctx, PetStarDb db) =>
        {
            var (sid, mid) = ctx.User.Player();
            var m = await Member(db, uid, sid);
            await db.Database.ExecuteSqlRawAsync("INSERT INTO user_mutes (muter_membership_id, muted_membership_id, at) VALUES ({0}, {1}, now()) ON CONFLICT DO NOTHING", mid, m.Id);
            return Results.Ok(new { muted = true });
        });

        g.MapDelete("/players/{uid:long}/mute", async (long uid, HttpContext ctx, PetStarDb db) =>
        {
            var (sid, mid) = ctx.User.Player();
            var m = await Member(db, uid, sid);
            await db.Mutes.Where(x => x.MuterMembershipId == mid && x.MutedMembershipId == m.Id).ExecuteDeleteAsync();
            return Results.Ok(new { muted = false });
        });

        g.MapGet("/players/blocklist", async (HttpContext ctx, PetStarDb db) =>
        {
            var (_, mid) = ctx.User.Player();
            return await db.Blocks.AsNoTracking().Where(b => b.BlockerMembershipId == mid)
                .Join(db.Memberships, b => b.BlockedMembershipId, m => m.Id, (b, m) => new { uid = m.AccountId, m.Nickname, b.At }).ToListAsync();
        });

        g.MapPost("/reports", async (ReportRequest req, HttpContext ctx, PetStarDb db) =>
        {
            var (sid, mid) = ctx.User.Player();
            if (!new[] { "post", "comment", "user", "listing", "auction", "chat" }.Contains(req.TargetType)) throw ApiException.BadRequest("invalid_report_target");
            if (!ReportReasons.Contains(req.Reason)) throw ApiException.BadRequest("invalid_report_reason");
            var details = req.Details == null ? null : req.Details.Trim()[..Math.Min(500, req.Details.Trim().Length)];
            bool exists = req.TargetType switch
            {
                "post" => await db.Posts.AnyAsync(p => p.Id == req.TargetId && p.ServerId == sid),
                "comment" => await db.Comments.Join(db.Posts, c => c.PostId, p => p.Id, (c, p) => new { c.Id, p.ServerId }).AnyAsync(x => x.Id == req.TargetId && x.ServerId == sid),
                "user" => await db.Memberships.AnyAsync(m => m.AccountId == req.TargetId && m.ServerId == sid),
                "listing" => await db.Listings.AnyAsync(l => l.Id == req.TargetId && l.ServerId == sid),
                "chat" => await db.ChatMessages.AnyAsync(m => m.Id == req.TargetId && m.ServerId == sid),
                _ => await db.Auctions.AnyAsync(a => a.Id == req.TargetId && a.ServerId == sid),
            };
            if (!exists) throw ApiException.NotFound("report_target_not_found");
            if (await db.Reports.CountAsync(r => r.ReporterMembershipId == mid && r.CreatedAt > DateTimeOffset.UtcNow.AddHours(-1)) >= 20) throw new ApiException(429, "too_many_reports");
            db.Reports.Add(new UserReport { ServerId = sid, ReporterMembershipId = mid, TargetType = req.TargetType, TargetId = req.TargetId, Reason = req.Reason, Details = details, CreatedAt = DateTimeOffset.UtcNow });
            try { await db.SaveChangesAsync(); } catch (DbUpdateException) { throw ApiException.Conflict("already_reported"); }
            await SocialModerationEndpoints.OnReport(db, sid, req.TargetType, req.TargetId);
            return Results.Ok(new { reported = true });
        });
    }
}
