using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PetStar.Api.Data;
using PetStar.Api.Infrastructure;
using PetStar.Contracts;

namespace PetStar.Api.Features;

public sealed class CreatePostRequest
{
    public string Type { get; set; } = "text";
    public string? Caption { get; set; }
    public List<long>? CardIds { get; set; }
    public string? RefId { get; set; }
    /// <summary>public | friends | guild | private (default from the author's social settings).</summary>
    public string? Visibility { get; set; }
    /// <summary>For Type = showcase: rare | new_pack | evolved | companion | team | element | theme.</summary>
    public string? ShowcaseKind { get; set; }
}
public sealed class CommentRequest { public string Text { get; set; } = ""; public long? ParentId { get; set; } }
public sealed class EditPostRequest { public string Caption { get; set; } = ""; }
public sealed class ShareRequest { public string Target { get; set; } = "feed"; public long? Uid { get; set; } public string? Caption { get; set; } }
public sealed class SocialSettingsRequest { public string? DefaultVisibility { get; set; } public string? ProfileVisibility { get; set; } public string? DefaultFeedTab { get; set; } }

public sealed record FeedCardView(int Position, string Serial, CardDto Snapshot, string CurrentOwnerNickname, long CurrentOwnerUid, bool OwnerChanged);
public sealed record SharedPostView(long Id, bool Available, string? Type, string? Caption, long? AuthorUid, string? AuthorNickname, string? ShowcaseKind, List<FeedCardView>? Cards);
public sealed record FeedPostView(long Id, string Type, string Caption, long AuthorUid, string AuthorNickname, DateTimeOffset CreatedAt, int Likes, int Comments,
    bool LikedByMe, bool SavedByMe, bool Featured, bool IsMine, List<FeedCardView> Cards, string DataJson,
    string Visibility = "public", string Status = "visible", string? ShowcaseKind = null, int Shares = 0, bool Pinned = false, DateTimeOffset? EditedAt = null,
    SocialAccess.Badge? Author = null, SharedPostView? SharedFrom = null);

public static class FeedEndpoints
{
    private static readonly string[] Types = { "text", "card", "pack_opening", "evolution", "duel", "battle", "achievement", "album", "share", "showcase" };
    private static readonly string[] ShowcaseKinds = { "rare", "new_pack", "evolved", "companion", "team", "element", "theme" };
    public const int RareRank = 3;            // SSR and above count as "rare" (rank = order in rarities.json)
    private const int MaxCards = 5;

    public static void Map(WebApplication app)
    {
        var g = ServerEndpoints.GameGroup(app);

        g.MapPost("/feed/posts", async (CreatePostRequest req, HttpContext ctx, PetStarDb db, GameData data, ContentGuard guard, AchievementService ach, GuildService guilds) =>
        {
            var (sid, mid) = ctx.User.Player();
            var post = await CreatePost(req, ctx, db, data, guard, sid, mid, null);
            await using (var tx = await db.Database.BeginTransactionAsync()) { await ach.Evaluate(mid, sid, "feed_post"); await tx.CommitAsync(); }
            if (post.Status == "visible") { await guilds.Progress(mid, "post", 1); if (post.ShowcaseKind != null) await guilds.Progress(mid, "showcase", 1); }
            return (await Views(db, new List<FeedPost> { post }, mid)).Single();
        });

        g.MapGet("/feed", async (string? tab, long? before, int? offset, int? limit, HttpContext ctx, PetStarDb db) =>
        {
            var (sid, mid) = ctx.User.Player();
            int take = Math.Clamp(limit ?? 20, 1, 50);
            var hidden = await Hidden(db, mid);
            var pub = db.Posts.AsNoTracking().Where(p => p.ServerId == sid && p.Status == "visible" && p.GuildId == null && p.Visibility == "public" && !hidden.Contains(p.AuthorMembershipId));
            IQueryable<FeedPost> q;
            bool ranked = false;
            switch (tab ?? "latest")
            {
                case "latest": case "global":
                    q = pub; break;
                case "explore":
                    q = pub.Where(p => p.CreatedAt > DateTimeOffset.UtcNow.AddDays(-14)); ranked = true; break;
                case "popular":
                    q = pub.Where(p => p.CreatedAt > DateTimeOffset.UtcNow.AddDays(-7) && p.LikeCount > 0); ranked = true; break;
                case "showcase":
                    q = pub.Where(p => p.ShowcaseKind != null); break;
                case "rare":
                    q = pub.Where(p => p.MaxRarity >= RareRank); break;
                case "featured":
                    var since = DateTimeOffset.UtcNow.AddDays(-2);
                    q = pub.Where(p => p.Featured || (p.CreatedAt > since && p.LikeCount >= 3)); ranked = true; break;
                case "following":
                    q = pub.Where(p => db.Follows.Any(f => f.FollowerMembershipId == mid && f.FolloweeMembershipId == p.AuthorMembershipId)); break;
                case "friends":
                {
                    var friends = await SocialAccess.FriendIds(db, mid);
                    q = db.Posts.AsNoTracking().Where(p => p.ServerId == sid && p.Status == "visible" && p.GuildId == null && friends.Contains(p.AuthorMembershipId)
                        && (p.Visibility == "public" || p.Visibility == "friends") && !hidden.Contains(p.AuthorMembershipId));
                    break;
                }
                case "guild":
                {
                    var gid = await SocialAccess.GuildOf(db, mid);
                    if (gid == null) return new { items = new List<FeedPostView>(), note = (string?)"not_in_guild" };
                    var mates = await db.GuildMembers.AsNoTracking().Where(m => m.GuildId == gid).Select(m => m.MembershipId).ToListAsync();
                    q = db.Posts.AsNoTracking().Where(p => p.ServerId == sid && p.Status == "visible" && !hidden.Contains(p.AuthorMembershipId)
                        && (p.GuildId == gid || (p.GuildId == null && p.Visibility == "public" && mates.Contains(p.AuthorMembershipId))));
                    break;
                }
                case "mine":
                    q = db.Posts.AsNoTracking().Where(p => p.AuthorMembershipId == mid && p.Status != "deleted"); break;
                case "saved":
                    q = pub.Where(p => db.Saves.Any(s => s.PostId == p.Id && s.MembershipId == mid)); break;
                default: throw ApiException.BadRequest("invalid_feed_tab");
            }
            List<FeedPost> posts;
            if (ranked)
            {
                // engagement score over a bounded recent window; paging by offset
                var window = await q.OrderByDescending(p => p.Id).Take(300).ToListAsync();
                var now = DateTimeOffset.UtcNow;
                posts = window.OrderByDescending(p => (p.Featured ? 50 : 0) + p.LikeCount * 3 + p.CommentCount * 2 + p.ShareCount * 4 - (now - p.CreatedAt).TotalHours / 12)
                    .ThenByDescending(p => p.Id).Skip(Math.Max(0, offset ?? 0)).Take(take).ToList();
            }
            else
            {
                if (before.HasValue) q = q.Where(p => p.Id < before);
                posts = await q.OrderByDescending(p => p.Id).Take(take).ToListAsync();
            }
            return new { items = await Views(db, posts, mid), note = (string?)null };
        });

        // Weekly Pet Showcase spotlight: valid interactions only (distinct accounts, author excluded, hidden/blocked excluded).
        g.MapGet("/feed/featured-week", async (HttpContext ctx, PetStarDb db) =>
        {
            var (sid, mid) = ctx.User.Player();
            var since = DateTimeOffset.UtcNow.AddDays(-7);
            var hidden = await Hidden(db, mid);
            var cands = await db.Posts.AsNoTracking().Where(p => p.ServerId == sid && p.Status == "visible" && p.Visibility == "public" && p.GuildId == null
                && p.ShowcaseKind != null && p.CreatedAt > since && !hidden.Contains(p.AuthorMembershipId)).Select(p => p.Id).ToListAsync();
            var scored = await WeeklyScores(db, cands);
            var top = scored.Where(x => x.Value > 0).OrderByDescending(x => x.Value).ThenBy(x => x.Key).Take(5).ToList();
            var posts = await db.Posts.AsNoTracking().Where(p => top.Select(t => t.Key).Contains(p.Id)).ToListAsync();
            var views = await Views(db, top.Select(t => posts.First(p => p.Id == t.Key)).ToList(), mid);
            return new { items = views.Select((v, i) => new { post = v, score = top[i].Value }), empty = top.Count == 0 };
        });

        g.MapGet("/feed/posts/{id:long}", async (long id, HttpContext ctx, PetStarDb db) =>
        {
            var (sid, mid) = ctx.User.Player();
            var p = await db.Posts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.ServerId == sid) ?? throw ApiException.NotFound("post_not_found");
            if (!await SocialAccess.CanView(db, p, mid)) throw ApiException.NotFound("post_not_found");
            var hidden = await MarketEndpoints.BlockedIds(db, mid);
            var rows = await db.Comments.AsNoTracking().Where(c => c.PostId == id && (c.Status == "visible" || (c.Status == "pending" && c.AuthorMembershipId == mid)) && !hidden.Contains(c.AuthorMembershipId))
                .OrderBy(c => c.Id).Take(300).ToListAsync();
            var cids = rows.Select(c => c.Id).ToList();
            var likedC = (await db.CommentLikes.AsNoTracking().Where(l => cids.Contains(l.CommentId) && l.MembershipId == mid).Select(l => l.CommentId).ToListAsync()).ToHashSet();
            var badges = await SocialAccess.Badges(db, rows.Select(c => c.AuthorMembershipId));
            var comments = rows.Select(c =>
            {
                var b = badges[c.AuthorMembershipId];
                return new { c.Id, c.Text, c.CreatedAt, c.ParentId, likes = c.LikeCount, likedByMe = likedC.Contains(c.Id), status = c.Status, authorUid = b.Uid, authorNickname = b.Nickname, author = b, mine = c.AuthorMembershipId == mid };
            }).ToList();
            return new { post = (await Views(db, new List<FeedPost> { p }, mid)).Single(), comments };
        });

        g.MapPut("/feed/posts/{id:long}", async (long id, EditPostRequest req, HttpContext ctx, PetStarDb db, GameData data, ContentGuard guard) =>
        {
            var (sid, mid) = ctx.User.Player();
            await guard.EnsureAllowed(ctx.User.AccountId(), "social");
            var p = await db.Posts.FirstOrDefaultAsync(x => x.Id == id && x.AuthorMembershipId == mid && x.Status != "deleted") ?? throw ApiException.NotFound("post_not_found");
            // edit policy: caption only, within 24h, re-moderated
            if (p.CreatedAt < DateTimeOffset.UtcNow.AddHours(-24)) throw ApiException.Conflict("edit_window_closed");
            if (p.Status == "hidden") throw ApiException.Conflict("post_under_review");
            var caption = ContentGuard.Clean(req.Caption, (int)data.Market.L("social", "captionMax"), p.Type == "text");
            p.Caption = caption; p.EditedAt = DateTimeOffset.UtcNow;
            var flags = ContentGuard.Assess(caption);
            if (flags.Count > 0) p.Status = "pending";
            await db.SaveChangesAsync();
            if (flags.Count > 0) await SocialAccess.Hold(db, sid, "post", p.Id, mid, flags);
            return (await Views(db, new List<FeedPost> { p }, mid)).Single();
        });

        g.MapDelete("/feed/posts/{id:long}", async (long id, HttpContext ctx, PetStarDb db) =>
        {
            var (_, mid) = ctx.User.Player();
            int n = await db.Posts.Where(p => p.Id == id && p.AuthorMembershipId == mid && p.Status != "deleted").ExecuteUpdateAsync(u => u.SetProperty(p => p.Status, "deleted").SetProperty(p => p.Pinned, false));
            return n == 0 ? throw ApiException.NotFound("post_not_found") : Results.Ok(new { deleted = true });
        });

        g.MapPost("/feed/posts/{id:long}/pin", async (long id, HttpContext ctx, PetStarDb db) =>
        {
            var (_, mid) = ctx.User.Player();
            await using var tx = await db.Database.BeginTransactionAsync();
            if (!await db.Posts.AnyAsync(p => p.Id == id && p.AuthorMembershipId == mid && p.Status == "visible")) throw ApiException.NotFound("post_not_found");
            await db.Posts.Where(p => p.AuthorMembershipId == mid && p.Pinned).ExecuteUpdateAsync(u => u.SetProperty(p => p.Pinned, false));
            await db.Posts.Where(p => p.Id == id).ExecuteUpdateAsync(u => u.SetProperty(p => p.Pinned, true));
            await tx.CommitAsync();
            return Results.Ok(new { pinned = true });
        });

        g.MapDelete("/feed/posts/{id:long}/pin", async (long id, HttpContext ctx, PetStarDb db) =>
        {
            var (_, mid) = ctx.User.Player();
            await db.Posts.Where(p => p.Id == id && p.AuthorMembershipId == mid).ExecuteUpdateAsync(u => u.SetProperty(p => p.Pinned, false));
            return Results.Ok(new { pinned = false });
        });

        g.MapPost("/feed/posts/{id:long}/like", async (long id, HttpContext ctx, PetStarDb db, NotificationService notes, RealtimeHub hub) =>
        {
            var (sid, mid) = ctx.User.Player();
            var p = await VisiblePost(db, id, sid, mid);
            await using var tx = await db.Database.BeginTransactionAsync();
            // (post, member) primary key: at most one active like per account, the count only moves when a row is inserted
            int n = await db.Database.ExecuteSqlRawAsync("INSERT INTO feed_likes (post_id, membership_id, at) VALUES ({0}, {1}, now()) ON CONFLICT DO NOTHING", id, mid);
            if (n == 1)
            {
                await db.Posts.Where(x => x.Id == id).ExecuteUpdateAsync(u => u.SetProperty(x => x.LikeCount, x => x.LikeCount + 1));
                if (p.AuthorMembershipId != mid) await notes.Notify(p.AuthorMembershipId, "post_liked", new { postId = id, uid = ctx.User.AccountId() }, $"like:{id}:{mid}");
            }
            await tx.CommitAsync();
            int likes = await db.Posts.Where(x => x.Id == id).Select(x => x.LikeCount).FirstAsync();
            await hub.ToServer(sid, "feed_counts", new { postId = id, likes });
            return Results.Ok(new { liked = true, likes });
        });

        g.MapDelete("/feed/posts/{id:long}/like", async (long id, HttpContext ctx, PetStarDb db, RealtimeHub hub) =>
        {
            var (sid, mid) = ctx.User.Player();
            await using var tx = await db.Database.BeginTransactionAsync();
            int n = await db.Likes.Where(l => l.PostId == id && l.MembershipId == mid).ExecuteDeleteAsync();
            if (n == 1) await db.Posts.Where(x => x.Id == id).ExecuteUpdateAsync(u => u.SetProperty(x => x.LikeCount, x => x.LikeCount - 1));
            await tx.CommitAsync();
            int likes = await db.Posts.Where(x => x.Id == id).Select(x => x.LikeCount).FirstOrDefaultAsync();
            if (n == 1) await hub.ToServer(sid, "feed_counts", new { postId = id, likes });
            return Results.Ok(new { liked = false, likes });
        });

        g.MapPost("/feed/posts/{id:long}/save", async (long id, HttpContext ctx, PetStarDb db) =>
        {
            var (sid, mid) = ctx.User.Player();
            await VisiblePost(db, id, sid, mid);
            await db.Database.ExecuteSqlRawAsync("INSERT INTO feed_saves (post_id, membership_id, at) VALUES ({0}, {1}, now()) ON CONFLICT DO NOTHING", id, mid);
            return Results.Ok(new { saved = true });
        });

        g.MapDelete("/feed/posts/{id:long}/save", async (long id, HttpContext ctx, PetStarDb db) =>
        {
            var (_, mid) = ctx.User.Player();
            await db.Saves.Where(s => s.PostId == id && s.MembershipId == mid).ExecuteDeleteAsync();
            return Results.Ok(new { saved = false });
        });

        // Share: to your own feed (a "share" post that keeps the source) or to a friend in game (DM with an internal link).
        // Only public posts can be re-shared publicly; guild posts only inside the same guild; private/friends posts never.
        g.MapPost("/feed/posts/{id:long}/share", async (long id, ShareRequest req, HttpContext ctx, PetStarDb db, GameData data, ContentGuard guard, NotificationService notes, RealtimeHub hub) =>
        {
            var (sid, mid) = ctx.User.Player();
            await guard.EnsureAllowed(ctx.User.AccountId(), "social");
            var orig = await VisiblePost(db, id, sid, mid);
            if (orig.Type == "share" && orig.SharedFromPostId is long root) orig = await VisiblePost(db, root, sid, mid);
            if (orig.Visibility is "private" or "friends") throw ApiException.Forbidden("post_not_shareable");
            if (req.Target == "friend")
            {
                if (req.Uid is not long uid) throw ApiException.BadRequest("uid_required");
                var other = await SocialEndpoints.Member(db, uid, sid);
                if (orig.GuildId != null && !await db.GuildMembers.AnyAsync(m => m.GuildId == orig.GuildId && m.MembershipId == other.Id)) throw ApiException.Forbidden("post_not_shareable");
                if (!await SocialAccess.AreFriends(db, mid, other.Id)) throw ApiException.Forbidden("dm_friends_only");
                if (await MarketEndpoints.IsBlocked(db, mid, other.Id)) throw ApiException.Forbidden("user_blocked");
                long a = Math.Min(mid, other.Id), b = Math.Max(mid, other.Id);
                var msg = new ChatMessage { ServerId = sid, Channel = $"dm:{a}:{b}", AuthorMembershipId = mid, Text = $"[post:{orig.Id}]", CreatedAt = DateTimeOffset.UtcNow };
                db.ChatMessages.Add(msg);
                await db.SaveChangesAsync();
                await db.Posts.Where(x => x.Id == orig.Id).ExecuteUpdateAsync(u => u.SetProperty(x => x.ShareCount, x => x.ShareCount + 1));
                var me = await db.Memberships.AsNoTracking().FirstAsync(m => m.Id == mid);
                await hub.ToMembers(new[] { mid, other.Id }, "chat", new { channel = msg.Channel, id = msg.Id, text = msg.Text, createdAt = msg.CreatedAt, authorUid = me.AccountId, authorNickname = me.Nickname });
                if (orig.AuthorMembershipId != mid) await notes.Notify(orig.AuthorMembershipId, "post_shared", new { postId = orig.Id, uid = ctx.User.AccountId() }, $"share:{orig.Id}:{mid}:friend:{other.Id}");
                return Results.Ok(new { shared = "friend", messageId = msg.Id });
            }
            if (req.Target != "feed") throw ApiException.BadRequest("invalid_share_target");
            var post = await CreatePost(new CreatePostRequest { Type = "share", RefId = orig.Id.ToString(), Caption = req.Caption, Visibility = orig.GuildId != null ? "guild" : "public" },
                ctx, db, data, guard, sid, mid, orig);
            await db.Posts.Where(x => x.Id == orig.Id).ExecuteUpdateAsync(u => u.SetProperty(x => x.ShareCount, x => x.ShareCount + 1));
            if (orig.AuthorMembershipId != mid) await notes.Notify(orig.AuthorMembershipId, "post_shared", new { postId = orig.Id, sharePostId = post.Id, uid = ctx.User.AccountId() }, $"share:{orig.Id}:{mid}:feed");
            return Results.Ok((await Views(db, new List<FeedPost> { post }, mid)).Single());
        });

        g.MapPost("/feed/posts/{id:long}/comments", async (long id, CommentRequest req, HttpContext ctx, PetStarDb db, GameData data, ContentGuard guard, NotificationService notes, RealtimeHub hub) =>
        {
            var (sid, mid) = ctx.User.Player();
            await guard.EnsureAllowed(ctx.User.AccountId(), "social");
            var p = await VisiblePost(db, id, sid, mid);
            var text = ContentGuard.Clean(req.Text, (int)data.Market.L("social", "commentMax"), true);
            if (await db.Comments.CountAsync(c => c.AuthorMembershipId == mid && c.CreatedAt > DateTimeOffset.UtcNow.AddHours(-1)) >= data.Market.L("social", "commentsPerHour"))
                throw new ApiException(429, "commenting_too_fast");
            if (await db.Comments.AnyAsync(c => c.AuthorMembershipId == mid && c.PostId == id && c.Text == text && c.CreatedAt > DateTimeOffset.UtcNow.AddMinutes(-5)))
                throw ApiException.Conflict("duplicate_comment");
            FeedComment? parent = null;
            if (req.ParentId is long pid)
            {
                parent = await db.Comments.AsNoTracking().FirstOrDefaultAsync(c => c.Id == pid && c.PostId == id && c.Status == "visible") ?? throw ApiException.NotFound("comment_not_found");
                if (parent.ParentId != null) throw ApiException.BadRequest("reply_depth_limit");   // one reply level
                if (await MarketEndpoints.IsBlocked(db, mid, parent.AuthorMembershipId)) throw ApiException.Forbidden("user_blocked");
            }
            var flags = ContentGuard.Assess(text);
            await using var tx = await db.Database.BeginTransactionAsync();
            var c = new FeedComment { PostId = id, AuthorMembershipId = mid, Text = text, ParentId = parent?.Id, Status = flags.Count > 0 ? "pending" : "visible", CreatedAt = DateTimeOffset.UtcNow };
            db.Comments.Add(c);
            await db.SaveChangesAsync();
            if (flags.Count > 0) await SocialAccess.Hold(db, sid, "comment", c.Id, mid, flags);
            else
            {
                await db.Posts.Where(x => x.Id == id).ExecuteUpdateAsync(u => u.SetProperty(x => x.CommentCount, x => x.CommentCount + 1));
                if (p.AuthorMembershipId != mid) await notes.Notify(p.AuthorMembershipId, "post_commented", new { postId = id, commentId = c.Id, uid = ctx.User.AccountId() }, "comment:" + c.Id);
                if (parent != null && parent.AuthorMembershipId != mid && parent.AuthorMembershipId != p.AuthorMembershipId)
                    await notes.Notify(parent.AuthorMembershipId, "comment_replied", new { postId = id, commentId = c.Id, parentId = parent.Id, uid = ctx.User.AccountId() }, "reply:" + c.Id);
            }
            await tx.CommitAsync();
            if (flags.Count == 0) await hub.ToServer(sid, "feed_counts", new { postId = id, comments = await db.Posts.Where(x => x.Id == id).Select(x => x.CommentCount).FirstAsync() });
            return Results.Ok(new { commentId = c.Id, status = c.Status });
        });

        g.MapPost("/feed/comments/{id:long}/like", async (long id, HttpContext ctx, PetStarDb db, NotificationService notes) =>
        {
            var (sid, mid) = ctx.User.Player();
            var c = await db.Comments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.Status == "visible") ?? throw ApiException.NotFound("comment_not_found");
            await VisiblePost(db, c.PostId, sid, mid);
            if (await MarketEndpoints.IsBlocked(db, mid, c.AuthorMembershipId)) throw ApiException.NotFound("comment_not_found");
            await using var tx = await db.Database.BeginTransactionAsync();
            int n = await db.Database.ExecuteSqlRawAsync("INSERT INTO feed_comment_likes (comment_id, membership_id, at) VALUES ({0}, {1}, now()) ON CONFLICT DO NOTHING", id, mid);
            if (n == 1)
            {
                await db.Comments.Where(x => x.Id == id).ExecuteUpdateAsync(u => u.SetProperty(x => x.LikeCount, x => x.LikeCount + 1));
                if (c.AuthorMembershipId != mid) await notes.Notify(c.AuthorMembershipId, "comment_liked", new { postId = c.PostId, commentId = id, uid = ctx.User.AccountId() }, $"clike:{id}:{mid}");
            }
            await tx.CommitAsync();
            return Results.Ok(new { liked = true, likes = await db.Comments.Where(x => x.Id == id).Select(x => x.LikeCount).FirstAsync() });
        });

        g.MapDelete("/feed/comments/{id:long}/like", async (long id, HttpContext ctx, PetStarDb db) =>
        {
            var (_, mid) = ctx.User.Player();
            await using var tx = await db.Database.BeginTransactionAsync();
            int n = await db.CommentLikes.Where(l => l.CommentId == id && l.MembershipId == mid).ExecuteDeleteAsync();
            if (n == 1) await db.Comments.Where(x => x.Id == id).ExecuteUpdateAsync(u => u.SetProperty(x => x.LikeCount, x => x.LikeCount - 1));
            await tx.CommitAsync();
            return Results.Ok(new { liked = false });
        });

        g.MapDelete("/feed/comments/{id:long}", async (long id, HttpContext ctx, PetStarDb db) =>
        {
            var (_, mid) = ctx.User.Player();
            var c = await db.Comments.FirstOrDefaultAsync(x => x.Id == id && x.AuthorMembershipId == mid && (x.Status == "visible" || x.Status == "pending")) ?? throw ApiException.NotFound();
            bool counted = c.Status == "visible";
            c.Status = "deleted";
            await db.SaveChangesAsync();
            if (counted) await db.Posts.Where(x => x.Id == c.PostId).ExecuteUpdateAsync(u => u.SetProperty(x => x.CommentCount, x => x.CommentCount - 1));
            return Results.Ok(new { deleted = true });
        });

        // Profile "Posts" tab: honours the owner's profile privacy and each post's visibility.
        g.MapGet("/players/{uid:long}/posts", async (long uid, long? before, HttpContext ctx, PetStarDb db) =>
        {
            var (sid, mid) = ctx.User.Player();
            var owner = await SocialEndpoints.Member(db, uid, sid);
            bool self = owner.Id == mid;
            if (!self && await MarketEndpoints.IsBlocked(db, mid, owner.Id)) throw ApiException.NotFound("player_not_found");
            var settings = await db.SocialSettings.AsNoTracking().FirstOrDefaultAsync(s => s.MembershipId == owner.Id);
            bool friends = !self && await SocialAccess.AreFriends(db, mid, owner.Id);
            var pv = settings?.ProfileVisibility ?? "public";
            if (!self && (pv == "private" || (pv == "friends" && !friends))) return Results.Ok(new { restricted = true, items = new List<FeedPostView>() });
            var myGuild = await SocialAccess.GuildOf(db, mid);
            var q = db.Posts.AsNoTracking().Where(p => p.AuthorMembershipId == owner.Id && p.Status != "deleted");
            if (!self) q = q.Where(p => p.Status == "visible" && (p.Visibility == "public" || (p.Visibility == "friends" && friends) || (p.Visibility == "guild" && p.GuildId == myGuild)));
            if (before.HasValue) q = q.Where(p => p.Id < before);
            var posts = await q.OrderByDescending(p => p.Pinned).ThenByDescending(p => p.Id).Take(20).ToListAsync();
            var totals = await db.Posts.AsNoTracking().Where(p => p.AuthorMembershipId == owner.Id && p.Status == "visible")
                .GroupBy(_ => 1).Select(x => new { likes = x.Sum(p => p.LikeCount), posts = x.Count(), showcases = x.Count(p => p.ShowcaseKind != null) }).FirstOrDefaultAsync();
            return Results.Ok(new { restricted = false, likesReceived = totals?.likes ?? 0, postCount = totals?.posts ?? 0, showcaseCount = totals?.showcases ?? 0, items = await Views(db, posts, mid) });
        });

        g.MapGet("/social/settings", async (HttpContext ctx, PetStarDb db) =>
        {
            var (_, mid) = ctx.User.Player();
            var s = await db.SocialSettings.AsNoTracking().FirstOrDefaultAsync(x => x.MembershipId == mid);
            return new { defaultVisibility = s?.DefaultVisibility ?? "public", profileVisibility = s?.ProfileVisibility ?? "public", defaultFeedTab = s?.DefaultFeedTab ?? "explore" };
        });

        g.MapPut("/social/settings", async (SocialSettingsRequest req, HttpContext ctx, PetStarDb db) =>
        {
            var (sid, mid) = ctx.User.Player();
            var s = await db.SocialSettings.FirstOrDefaultAsync(x => x.MembershipId == mid) ?? db.SocialSettings.Add(new SocialSettings { MembershipId = mid, ServerId = sid }).Entity;
            if (req.DefaultVisibility != null) s.DefaultVisibility = SocialAccess.Visibilities.Contains(req.DefaultVisibility) ? req.DefaultVisibility : throw ApiException.BadRequest("invalid_visibility");
            if (req.ProfileVisibility != null) s.ProfileVisibility = req.ProfileVisibility is "public" or "friends" or "private" ? req.ProfileVisibility : throw ApiException.BadRequest("invalid_visibility");
            if (req.DefaultFeedTab != null) s.DefaultFeedTab = req.DefaultFeedTab is "explore" or "latest" or "friends" or "guild" or "showcase" or "rare" or "popular" ? req.DefaultFeedTab : throw ApiException.BadRequest("invalid_feed_tab");
            s.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(new { s.DefaultVisibility, s.ProfileVisibility, s.DefaultFeedTab });
        });
    }

    /// <summary>Shared post creation (public feed, guild feed, shares). Everything attached is resolved on the server from
    /// the author's own data: cards must be owned, rarity/element/evolution come from the database, never from the client.</summary>
    public static async Task<FeedPost> CreatePost(CreatePostRequest req, HttpContext ctx, PetStarDb db, GameData data, ContentGuard guard, int sid, long mid, FeedPost? shareOf, bool guildFeed = false)
    {
        await guard.EnsureAllowed(ctx.User.AccountId(), "social");
        var cfg = data.Market;
        if (!Types.Contains(req.Type)) throw ApiException.BadRequest("invalid_post_type");
        var caption = ContentGuard.Clean(req.Caption, (int)cfg.L("social", "captionMax"), req.Type == "text");
        var now = DateTimeOffset.UtcNow;
        if (await db.Posts.CountAsync(p => p.AuthorMembershipId == mid && p.CreatedAt > now.AddHours(-1)) >= cfg.L("social", "postsPerHour"))
            throw new ApiException(429, "posting_too_fast");
        if (caption.Length > 0 && await db.Posts.AnyAsync(p => p.AuthorMembershipId == mid && p.Caption == caption && p.Status != "deleted" && p.CreatedAt > now.AddMinutes(-cfg.L("social", "duplicateWindowMinutes"))))
            throw ApiException.Conflict("duplicate_post");

        var settings = await db.SocialSettings.AsNoTracking().FirstOrDefaultAsync(s => s.MembershipId == mid);
        var visibility = guildFeed ? "guild" : req.Visibility ?? settings?.DefaultVisibility ?? "public";
        if (!SocialAccess.Visibilities.Contains(visibility)) throw ApiException.BadRequest("invalid_visibility");
        long? guildId = null;
        if (visibility == "guild") guildId = await SocialAccess.GuildOf(db, mid) ?? throw ApiException.Forbidden("not_in_guild");

        var post = new FeedPost { ServerId = sid, AuthorMembershipId = mid, Type = req.Type, Caption = caption, CreatedAt = now, RefId = req.RefId, Visibility = visibility, GuildId = guildId };
        var cardIds = (req.CardIds ?? new List<long>()).Distinct().ToList();
        bool mustOwn = false;
        object extra = new { };
        switch (req.Type)
        {
            case "card":
                if (cardIds.Count is < 1 or > MaxCards) throw ApiException.BadRequest("post_cards_1_to_5");
                mustOwn = true;
                break;
            case "evolution":
                if (cardIds.Count != 1 || !await db.CardEvolutions.AnyAsync(e => e.CardId == cardIds[0] && e.MembershipId == mid)) throw ApiException.BadRequest("no_evolution_to_share");
                var evo = await db.CardEvolutions.AsNoTracking().Where(e => e.CardId == cardIds[0]).OrderByDescending(e => e.At).FirstAsync();
                extra = new { evo.FromFormId, evo.ToFormId, evo.At };
                mustOwn = true;
                break;
            case "pack_opening":
                var opening = Guid.TryParse(req.RefId, out var oid) ? await db.PackOpenings.AsNoTracking().Include(o => o.Results).FirstOrDefaultAsync(o => o.Id == oid && o.MembershipId == mid) : null;
                if (opening == null) throw ApiException.BadRequest("opening_not_found");
                cardIds = opening.Results.OrderBy(r => r.Slot).Select(r => r.CardId).ToList();
                extra = new { opening.PackTypeId, opening.CreatedAt };
                break;
            case "duel":
                var battle = Guid.TryParse(req.RefId, out var bid) ? await db.Battles.AsNoTracking().FirstOrDefaultAsync(b => b.Id == bid && b.PlayerAMembershipId == mid && b.Status == "finished") : null;
                if (battle == null) throw ApiException.BadRequest("battle_not_found");
                extra = new { battle.Mode, battle.StageId, won = battle.WinnerSide == 0, battle.Round };
                break;
            case "battle":
            {
                // Battle 3v3 result: the player's side, result, opponent and their own 3 Pets, from the stored match
                var mtc = Guid.TryParse(req.RefId, out var mtid) ? await db.ArenaMatches.AsNoTracking().FirstOrDefaultAsync(x => x.Id == mtid && x.Status == "finished" && x.ServerId == sid) : null;
                var me = mtc == null ? null : await db.ArenaMatchPlayers.AsNoTracking().FirstOrDefaultAsync(x => x.MatchId == mtc.Id && x.MembershipId == mid);
                if (mtc == null || me == null) throw ApiException.BadRequest("battle_not_found");
                var opp = await db.ArenaMatchPlayers.AsNoTracking().FirstAsync(x => x.MatchId == mtc.Id && x.Side != me.Side);
                var team = JsonSerializer.Deserialize<TeamService.ReplayTeam>(me.DeckJson, IdempotencyService.Json);
                cardIds = team?.Units.Where(u => u.CardInstanceId != 0).Select(u => u.CardInstanceId).ToList() ?? new();
                extra = new { mode = mtc.Mode, result = mtc.WinnerSide == null ? "draw" : mtc.WinnerSide == me.Side ? "win" : "loss", opponent = opp.Name, mtc.EndReason, matchId = mtc.Id };
                break;
            }
            case "achievement":
                if (req.RefId == null || !await db.Achievements.AnyAsync(a => a.MembershipId == mid && a.AchievementId == req.RefId)) throw ApiException.BadRequest("achievement_not_unlocked");
                break;
            case "album":
                var col = long.TryParse(req.RefId, out var cid) ? await db.Collections.AsNoTracking().Include(c => c.Items).FirstOrDefaultAsync(c => c.Id == cid && c.MembershipId == mid) : null;
                if (col == null) throw ApiException.BadRequest("album_not_found");
                cardIds = col.Items.OrderBy(i => i.Position).Take(MaxCards).Select(i => i.CardId).ToList();
                extra = new { albumName = col.Name, total = col.Items.Count };
                break;
            case "share":
                var orig = shareOf ?? throw ApiException.BadRequest("use_share_endpoint");
                post.SharedFromPostId = orig.Id;
                extra = new { originalPostId = orig.Id };
                break;
            case "showcase":
                (cardIds, extra, mustOwn) = await Showcase(req, db, data, sid, mid, cardIds);
                post.ShowcaseKind = req.ShowcaseKind;
                break;
        }
        post.DataJson = JsonSerializer.Serialize(extra, IdempotencyService.Json);
        var cards = await db.Cards.AsNoTracking().Where(c => cardIds.Contains(c.Id) && c.ServerId == sid).ToListAsync();
        if (mustOwn && cards.Count(c => c.OwnerMembershipId == mid) != cardIds.Count) throw ApiException.BadRequest("card_not_owned");
        var rank = data.Catalog.Rarities.ToDictionary(r => r.Id, r => r.Order);
        post.MaxRarity = cards.Count == 0 ? 0 : cards.Max(c => rank.GetValueOrDefault(c.Rarity));

        var flags = ContentGuard.Assess(caption);
        if (flags.Count > 0) post.Status = "pending";   // held until reviewed; only the author sees it
        await using var tx = await db.Database.BeginTransactionAsync();
        db.Posts.Add(post);
        await db.SaveChangesAsync();
        int pos = 0;
        foreach (var id in cardIds)
        {
            var c = cards.FirstOrDefault(x => x.Id == id);
            if (c == null) continue;
            var snap = data.ToDto(c); snap.Id = 0;
            db.PostCards.Add(new FeedPostCard { PostId = post.Id, CardId = c.Id, Position = pos++, Serial = c.Serial, OwnerAtPostMembershipId = c.OwnerMembershipId, SnapshotJson = JsonSerializer.Serialize(snap, IdempotencyService.Json) });
        }
        await db.SaveChangesAsync();
        if (flags.Count > 0) await SocialAccess.Hold(db, sid, "post", post.Id, mid, flags);
        await tx.CommitAsync();
        return post;
    }

    private static async Task<(List<long>, object, bool)> Showcase(CreatePostRequest req, PetStarDb db, GameData data, int sid, long mid, List<long> cardIds)
    {
        if (!ShowcaseKinds.Contains(req.ShowcaseKind ?? "")) throw ApiException.BadRequest("invalid_showcase_kind");
        var rank = data.Catalog.Rarities.ToDictionary(r => r.Id, r => r.Order);
        switch (req.ShowcaseKind)
        {
            case "rare":
            {
                if (cardIds.Count != 1) throw ApiException.BadRequest("showcase_one_card");
                var c = await db.Cards.AsNoTracking().FirstOrDefaultAsync(x => x.Id == cardIds[0] && x.OwnerMembershipId == mid) ?? throw ApiException.BadRequest("card_not_owned");
                if (rank.GetValueOrDefault(c.Rarity) < RareRank) throw ApiException.BadRequest("card_not_rare");
                return (cardIds, new { rarity = c.Rarity }, true);
            }
            case "new_pack":
            {
                var o = Guid.TryParse(req.RefId, out var oid) ? await db.PackOpenings.AsNoTracking().Include(x => x.Results).FirstOrDefaultAsync(x => x.Id == oid && x.MembershipId == mid) : null;
                if (o == null) throw ApiException.BadRequest("opening_not_found");
                var ids = o.Results.OrderBy(r => r.Slot).Select(r => r.CardId).ToList();
                if (cardIds.Count > 0) { if (!cardIds.All(ids.Contains)) throw ApiException.BadRequest("card_not_in_opening"); ids = cardIds; }
                return (ids.Take(MaxCards).ToList(), new { o.PackTypeId, openedAt = o.CreatedAt }, false);
            }
            case "evolved":
            {
                if (cardIds.Count != 1) throw ApiException.BadRequest("showcase_one_card");
                var evo = await db.CardEvolutions.AsNoTracking().Where(e => e.CardId == cardIds[0] && e.MembershipId == mid).OrderByDescending(e => e.At).FirstOrDefaultAsync()
                          ?? throw ApiException.BadRequest("no_evolution_to_share");
                return (cardIds, new { evo.FromFormId, evo.ToFormId, evo.At }, true);
            }
            case "companion":
            {
                var t = await db.TrainerProfiles.AsNoTracking().FirstOrDefaultAsync(x => x.MembershipId == mid);
                if (t?.CompanionCardId is not long comp) throw ApiException.BadRequest("no_companion");
                return (new List<long> { comp }, new { trainer = t.CharacterId }, true);
            }
            case "team":
            {
                var l = long.TryParse(req.RefId, out var lid) ? await db.TeamLineups.AsNoTracking().FirstOrDefaultAsync(x => x.Id == lid && x.MembershipId == mid)
                    : await db.TeamLineups.AsNoTracking().Where(x => x.MembershipId == mid).OrderByDescending(x => x.IsDefault).FirstOrDefaultAsync();
                if (l == null) throw ApiException.BadRequest("no_team");
                return (new List<long> { l.Card1Id, l.Card2Id, l.Card3Id }, new { lineup = l.Name }, true);
            }
            case "element":
            {
                var element = req.RefId ?? throw ApiException.BadRequest("element_required");
                var owned = await db.Cards.AsNoTracking().Where(c => c.OwnerMembershipId == mid && c.ServerId == sid).OrderByDescending(c => c.Id).Take(1000).ToListAsync();
                var dtos = owned.Select(c => (c, d: data.ToDto(c))).Where(x => x.d.Element == element).ToList();
                if (dtos.Count == 0) throw ApiException.BadRequest("no_cards_of_element");
                var top = dtos.OrderByDescending(x => x.d.Power).Take(MaxCards).Select(x => x.c.Id).ToList();
                return (top, new { element, total = dtos.Count, distinctForms = dtos.Select(x => x.d.FormId).Distinct().Count() }, true);
            }
            default: // theme = one of the player's albums
            {
                var col = long.TryParse(req.RefId, out var cid) ? await db.Collections.AsNoTracking().Include(c => c.Items).FirstOrDefaultAsync(c => c.Id == cid && c.MembershipId == mid) : null;
                if (col == null) throw ApiException.BadRequest("album_not_found");
                return (col.Items.OrderBy(i => i.Position).Take(MaxCards).Select(i => i.CardId).ToList(), new { albumName = col.Name, total = col.Items.Count }, false);
            }
        }
    }

    private static async Task<List<long>> Hidden(PetStarDb db, long mid)
    {
        var hidden = await MarketEndpoints.BlockedIds(db, mid);
        hidden.AddRange(await db.Mutes.AsNoTracking().Where(m => m.MuterMembershipId == mid).Select(m => m.MutedMembershipId).ToListAsync());
        return hidden;
    }

    /// <summary>Valid interaction score: distinct likers (author excluded) ×3 + distinct commenters (author excluded) ×2.</summary>
    public static async Task<Dictionary<long, int>> WeeklyScores(PetStarDb db, List<long> postIds)
    {
        var authors = await db.Posts.AsNoTracking().Where(p => postIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.AuthorMembershipId);
        var likes = await db.Likes.AsNoTracking().Where(l => postIds.Contains(l.PostId)).Select(l => new { l.PostId, l.MembershipId }).ToListAsync();
        var comments = await db.Comments.AsNoTracking().Where(c => postIds.Contains(c.PostId) && c.Status == "visible").Select(c => new { c.PostId, c.AuthorMembershipId }).Distinct().ToListAsync();
        return postIds.ToDictionary(id => id, id =>
            likes.Count(l => l.PostId == id && l.MembershipId != authors[id]) * 3 +
            comments.Where(c => c.PostId == id && c.AuthorMembershipId != authors[id]).Select(c => c.AuthorMembershipId).Distinct().Count() * 2);
    }

    private static async Task<FeedPost> VisiblePost(PetStarDb db, long id, int sid, long mid)
    {
        var p = await db.Posts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.ServerId == sid && x.Status == "visible") ?? throw ApiException.NotFound("post_not_found");
        if (!await SocialAccess.CanView(db, p, mid)) throw ApiException.NotFound("post_not_found");
        return p;
    }

    /// <summary>Card snapshots are shown as posted, but the owner shown is always the CURRENT owner of the live card.</summary>
    public static async Task<List<FeedPostView>> Views(PetStarDb db, List<FeedPost> posts, long viewerMid)
    {
        var sharedIds = posts.Where(p => p.SharedFromPostId != null).Select(p => p.SharedFromPostId!.Value).Distinct().ToList();
        var shared = await db.Posts.AsNoTracking().Where(p => sharedIds.Contains(p.Id)).ToListAsync();
        var all = posts.Concat(shared).ToList();
        var ids = all.Select(p => p.Id).ToList();
        var pcs = await db.PostCards.AsNoTracking().Where(c => ids.Contains(c.PostId)).ToListAsync();
        var cardIds = pcs.Select(c => c.CardId).Distinct().ToList();
        var owners = await db.Cards.AsNoTracking().Where(c => cardIds.Contains(c.Id)).Select(c => new { c.Id, c.OwnerMembershipId }).ToDictionaryAsync(c => c.Id, c => c.OwnerMembershipId);
        var memberIds = all.Select(p => p.AuthorMembershipId).Concat(owners.Values).Distinct().ToList();
        var members = await db.Memberships.AsNoTracking().Where(m => memberIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id);
        var badges = await SocialAccess.Badges(db, posts.Select(p => p.AuthorMembershipId));
        var postIds = posts.Select(p => p.Id).ToList();
        var liked = (await db.Likes.AsNoTracking().Where(l => postIds.Contains(l.PostId) && l.MembershipId == viewerMid).Select(l => l.PostId).ToListAsync()).ToHashSet();
        var saved = (await db.Saves.AsNoTracking().Where(l => postIds.Contains(l.PostId) && l.MembershipId == viewerMid).Select(l => l.PostId).ToListAsync()).ToHashSet();
        var visibleShared = new HashSet<long>();
        foreach (var s in shared) if (await SocialAccess.CanView(db, s, viewerMid)) visibleShared.Add(s.Id);

        List<FeedCardView> Cards(long postId) => pcs.Where(c => c.PostId == postId).OrderBy(c => c.Position).Select(c =>
        {
            var owner = members[owners[c.CardId]];
            var snap = JsonSerializer.Deserialize<CardDto>(c.SnapshotJson, IdempotencyService.Json)!;
            return new FeedCardView(c.Position, c.Serial, snap, owner.Nickname, owner.AccountId, owner.Id != c.OwnerAtPostMembershipId);
        }).ToList();

        return posts.Select(p =>
        {
            var author = members[p.AuthorMembershipId];
            SharedPostView? sv = null;
            if (p.SharedFromPostId is long sid2)
            {
                var o = shared.FirstOrDefault(x => x.Id == sid2);
                sv = o != null && visibleShared.Contains(o.Id)
                    ? new SharedPostView(o.Id, true, o.Type, o.Caption, members[o.AuthorMembershipId].AccountId, members[o.AuthorMembershipId].Nickname, o.ShowcaseKind, Cards(o.Id))
                    : new SharedPostView(sid2, false, null, null, null, null, null, null);   // source removed or not visible to this viewer
            }
            return new FeedPostView(p.Id, p.Type, p.Caption, author.AccountId, author.Nickname, p.CreatedAt, p.LikeCount, p.CommentCount, liked.Contains(p.Id),
                saved.Contains(p.Id), p.Featured, p.AuthorMembershipId == viewerMid, Cards(p.Id), p.DataJson,
                p.Visibility, p.Status, p.ShowcaseKind, p.ShareCount, p.Pinned, p.EditedAt, badges.GetValueOrDefault(p.AuthorMembershipId), sv);
        }).ToList();
    }
}
