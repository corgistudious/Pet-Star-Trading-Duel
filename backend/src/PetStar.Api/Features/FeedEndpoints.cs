using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PetStar.Api.Data;
using PetStar.Api.Infrastructure;
using PetStar.Contracts;

namespace PetStar.Api.Features;

public sealed class CreatePostRequest { public string Type { get; set; } = "text"; public string? Caption { get; set; } public List<long>? CardIds { get; set; } public string? RefId { get; set; } }
public sealed class CommentRequest { public string Text { get; set; } = ""; }

public sealed record FeedCardView(int Position, string Serial, CardDto Snapshot, string CurrentOwnerNickname, long CurrentOwnerUid, bool OwnerChanged);
public sealed record FeedPostView(long Id, string Type, string Caption, long AuthorUid, string AuthorNickname, DateTimeOffset CreatedAt, int Likes, int Comments,
    bool LikedByMe, bool SavedByMe, bool Featured, bool IsMine, List<FeedCardView> Cards, string DataJson);

public static class FeedEndpoints
{
    private static readonly string[] Types = { "text", "card", "pack_opening", "evolution", "duel", "achievement", "album", "share" };

    public static void Map(WebApplication app)
    {
        var g = ServerEndpoints.GameGroup(app);

        g.MapPost("/feed/posts", async (CreatePostRequest req, HttpContext ctx, PetStarDb db, GameData data, ContentGuard guard, AchievementService ach) =>
        {
            var (sid, mid) = ctx.User.Player();
            await guard.EnsureAllowed(ctx.User.AccountId(), "social");
            var cfg = data.Market;
            if (!Types.Contains(req.Type)) throw ApiException.BadRequest("invalid_post_type");
            var caption = ContentGuard.Clean(req.Caption, (int)cfg.L("social", "captionMax"), req.Type == "text");
            var now = DateTimeOffset.UtcNow;
            if (await db.Posts.CountAsync(p => p.AuthorMembershipId == mid && p.CreatedAt > now.AddHours(-1)) >= cfg.L("social", "postsPerHour"))
                throw new ApiException(429, "posting_too_fast");
            if (caption.Length > 0 && await db.Posts.AnyAsync(p => p.AuthorMembershipId == mid && p.Caption == caption && p.CreatedAt > now.AddMinutes(-cfg.L("social", "duplicateWindowMinutes"))))
                throw ApiException.Conflict("duplicate_post");

            var post = new FeedPost { ServerId = sid, AuthorMembershipId = mid, Type = req.Type, Caption = caption, CreatedAt = now, RefId = req.RefId };
            var cardIds = req.CardIds ?? new List<long>();
            object extra = new { };
            switch (req.Type)
            {
                case "card":
                    if (cardIds.Count is < 1 or > 5) throw ApiException.BadRequest("post_cards_1_to_5");
                    break;
                case "evolution":
                    if (cardIds.Count != 1 || !await db.CardEvolutions.AnyAsync(e => e.CardId == cardIds[0] && e.MembershipId == mid)) throw ApiException.BadRequest("no_evolution_to_share");
                    var evo = await db.CardEvolutions.AsNoTracking().Where(e => e.CardId == cardIds[0]).OrderByDescending(e => e.At).FirstAsync();
                    extra = new { evo.FromFormId, evo.ToFormId, evo.At };
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
                case "achievement":
                    if (req.RefId == null || !await db.Achievements.AnyAsync(a => a.MembershipId == mid && a.AchievementId == req.RefId)) throw ApiException.BadRequest("achievement_not_unlocked");
                    break;
                case "album":
                    var col = long.TryParse(req.RefId, out var cid) ? await db.Collections.AsNoTracking().Include(c => c.Items).FirstOrDefaultAsync(c => c.Id == cid && c.MembershipId == mid) : null;
                    if (col == null) throw ApiException.BadRequest("album_not_found");
                    cardIds = col.Items.OrderBy(i => i.Position).Take(5).Select(i => i.CardId).ToList();
                    extra = new { albumName = col.Name, total = col.Items.Count };
                    break;
                case "share":
                    var orig = long.TryParse(req.RefId, out var pid) ? await db.Posts.AsNoTracking().FirstOrDefaultAsync(p => p.Id == pid && p.ServerId == sid && p.Status == "visible") : null;
                    if (orig == null) throw ApiException.BadRequest("post_not_found");
                    extra = new { originalPostId = orig.Id };
                    break;
            }
            post.DataJson = JsonSerializer.Serialize(extra, IdempotencyService.Json);
            // cards must be real and owned by the author at posting time (snapshot is taken server-side)
            var cards = await db.Cards.AsNoTracking().Where(c => cardIds.Contains(c.Id) && c.ServerId == sid).ToListAsync();
            if (req.Type is "card" or "evolution" && cards.Count(c => c.OwnerMembershipId == mid) != cardIds.Distinct().Count()) throw ApiException.BadRequest("card_not_owned");
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
            await ach.Evaluate(mid, sid, "feed_post");
            await tx.CommitAsync();
            return (await Views(db, new List<FeedPost> { post }, mid)).Single();
        });

        g.MapGet("/feed", async (string? tab, long? before, int? limit, HttpContext ctx, PetStarDb db) =>
        {
            var (sid, mid) = ctx.User.Player();
            var hidden = await MarketEndpoints.BlockedIds(db, mid);
            hidden.AddRange(await db.Mutes.AsNoTracking().Where(m => m.MuterMembershipId == mid).Select(m => m.MutedMembershipId).ToListAsync());
            var q = db.Posts.AsNoTracking().Where(p => p.ServerId == sid && p.Status == "visible" && !hidden.Contains(p.AuthorMembershipId));
            switch (tab ?? "global")
            {
                case "following":
                    q = q.Where(p => db.Follows.Any(f => f.FollowerMembershipId == mid && f.FolloweeMembershipId == p.AuthorMembershipId));
                    break;
                case "friends":
                    q = q.Where(p => db.Friendships.Any(f => f.Status == "accepted" &&
                        ((f.RequesterMembershipId == mid && f.AddresseeMembershipId == p.AuthorMembershipId) || (f.AddresseeMembershipId == mid && f.RequesterMembershipId == p.AuthorMembershipId))));
                    break;
                case "featured":
                    var since = DateTimeOffset.UtcNow.AddDays(-2);
                    q = q.Where(p => p.Featured || (p.CreatedAt > since && p.LikeCount >= 3));
                    break;
                case "mine":
                    q = db.Posts.AsNoTracking().Where(p => p.AuthorMembershipId == mid && p.Status != "deleted");
                    break;
                case "saved":
                    q = q.Where(p => db.Saves.Any(s => s.PostId == p.Id && s.MembershipId == mid));
                    break;
                case "guild":
                    return new { items = new List<FeedPostView>(), note = "guild_feed_next_milestone" };
            }
            if (before.HasValue) q = q.Where(p => p.Id < before);
            var posts = await (tab == "featured" ? q.OrderByDescending(p => p.LikeCount).ThenByDescending(p => p.Id) : q.OrderByDescending(p => p.Id))
                .Take(Math.Clamp(limit ?? 20, 1, 50)).ToListAsync();
            return new { items = await Views(db, posts, mid), note = (string?)null };
        });

        g.MapGet("/feed/posts/{id:long}", async (long id, HttpContext ctx, PetStarDb db) =>
        {
            var (sid, mid) = ctx.User.Player();
            var p = await db.Posts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.ServerId == sid && (x.Status == "visible" || x.AuthorMembershipId == mid))
                    ?? throw ApiException.NotFound("post_not_found");
            if (await MarketEndpoints.IsBlocked(db, mid, p.AuthorMembershipId)) throw ApiException.NotFound("post_not_found");
            var hidden = await MarketEndpoints.BlockedIds(db, mid);
            var comments = await db.Comments.AsNoTracking().Where(c => c.PostId == id && c.Status == "visible" && !hidden.Contains(c.AuthorMembershipId))
                .OrderBy(c => c.Id).Take(200)
                .Join(db.Memberships, c => c.AuthorMembershipId, m => m.Id, (c, m) => new { c.Id, c.Text, c.CreatedAt, authorUid = m.AccountId, authorNickname = m.Nickname, mine = c.AuthorMembershipId == mid })
                .ToListAsync();
            return new { post = (await Views(db, new List<FeedPost> { p }, mid)).Single(), comments };
        });

        g.MapDelete("/feed/posts/{id:long}", async (long id, HttpContext ctx, PetStarDb db) =>
        {
            var (_, mid) = ctx.User.Player();
            int n = await db.Posts.Where(p => p.Id == id && p.AuthorMembershipId == mid && p.Status != "deleted").ExecuteUpdateAsync(u => u.SetProperty(p => p.Status, "deleted"));
            return n == 0 ? throw ApiException.NotFound("post_not_found") : Results.Ok(new { deleted = true });
        });

        g.MapPost("/feed/posts/{id:long}/like", async (long id, HttpContext ctx, PetStarDb db, NotificationService notes) =>
        {
            var (sid, mid) = ctx.User.Player();
            var p = await VisiblePost(db, id, sid, mid);
            await using var tx = await db.Database.BeginTransactionAsync();
            int n = await db.Database.ExecuteSqlRawAsync("INSERT INTO feed_likes (post_id, membership_id, at) VALUES ({0}, {1}, now()) ON CONFLICT DO NOTHING", id, mid);
            if (n == 1)
            {
                await db.Posts.Where(x => x.Id == id).ExecuteUpdateAsync(u => u.SetProperty(x => x.LikeCount, x => x.LikeCount + 1));
                if (p.AuthorMembershipId != mid) await notes.Notify(p.AuthorMembershipId, "post_liked", new { postId = id, uid = ctx.User.AccountId() }, $"like:{id}:{mid}");
            }
            await tx.CommitAsync();
            return Results.Ok(new { liked = true });
        });

        g.MapDelete("/feed/posts/{id:long}/like", async (long id, HttpContext ctx, PetStarDb db) =>
        {
            var (_, mid) = ctx.User.Player();
            await using var tx = await db.Database.BeginTransactionAsync();
            int n = await db.Likes.Where(l => l.PostId == id && l.MembershipId == mid).ExecuteDeleteAsync();
            if (n == 1) await db.Posts.Where(x => x.Id == id).ExecuteUpdateAsync(u => u.SetProperty(x => x.LikeCount, x => x.LikeCount - 1));
            await tx.CommitAsync();
            return Results.Ok(new { liked = false });
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

        g.MapPost("/feed/posts/{id:long}/comments", async (long id, CommentRequest req, HttpContext ctx, PetStarDb db, GameData data, ContentGuard guard, NotificationService notes) =>
        {
            var (sid, mid) = ctx.User.Player();
            await guard.EnsureAllowed(ctx.User.AccountId(), "social");
            var p = await VisiblePost(db, id, sid, mid);
            var text = ContentGuard.Clean(req.Text, (int)data.Market.L("social", "commentMax"), true);
            if (await db.Comments.CountAsync(c => c.AuthorMembershipId == mid && c.CreatedAt > DateTimeOffset.UtcNow.AddHours(-1)) >= data.Market.L("social", "commentsPerHour"))
                throw new ApiException(429, "commenting_too_fast");
            await using var tx = await db.Database.BeginTransactionAsync();
            var c = new FeedComment { PostId = id, AuthorMembershipId = mid, Text = text, CreatedAt = DateTimeOffset.UtcNow };
            db.Comments.Add(c);
            await db.SaveChangesAsync();
            await db.Posts.Where(x => x.Id == id).ExecuteUpdateAsync(u => u.SetProperty(x => x.CommentCount, x => x.CommentCount + 1));
            if (p.AuthorMembershipId != mid) await notes.Notify(p.AuthorMembershipId, "post_commented", new { postId = id, commentId = c.Id, uid = ctx.User.AccountId() }, "comment:" + c.Id);
            await tx.CommitAsync();
            return Results.Ok(new { commentId = c.Id });
        });

        g.MapDelete("/feed/comments/{id:long}", async (long id, HttpContext ctx, PetStarDb db) =>
        {
            var (_, mid) = ctx.User.Player();
            var c = await db.Comments.FirstOrDefaultAsync(x => x.Id == id && x.AuthorMembershipId == mid && x.Status == "visible") ?? throw ApiException.NotFound();
            c.Status = "deleted";
            await db.SaveChangesAsync();
            await db.Posts.Where(x => x.Id == c.PostId).ExecuteUpdateAsync(u => u.SetProperty(x => x.CommentCount, x => x.CommentCount - 1));
            return Results.Ok(new { deleted = true });
        });
    }

    private static async Task<FeedPost> VisiblePost(PetStarDb db, long id, int sid, long mid)
    {
        var p = await db.Posts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.ServerId == sid && x.Status == "visible") ?? throw ApiException.NotFound("post_not_found");
        if (await MarketEndpoints.IsBlocked(db, mid, p.AuthorMembershipId)) throw ApiException.NotFound("post_not_found");
        return p;
    }

    /// <summary>Card snapshots are shown as posted, but the owner shown is always the CURRENT owner of the live card.</summary>
    public static async Task<List<FeedPostView>> Views(PetStarDb db, List<FeedPost> posts, long viewerMid)
    {
        var ids = posts.Select(p => p.Id).ToList();
        var pcs = await db.PostCards.AsNoTracking().Where(c => ids.Contains(c.PostId)).ToListAsync();
        var cardIds = pcs.Select(c => c.CardId).Distinct().ToList();
        var owners = await db.Cards.AsNoTracking().Where(c => cardIds.Contains(c.Id)).Select(c => new { c.Id, c.OwnerMembershipId }).ToDictionaryAsync(c => c.Id, c => c.OwnerMembershipId);
        var memberIds = posts.Select(p => p.AuthorMembershipId).Concat(owners.Values).Distinct().ToList();
        var members = await db.Memberships.AsNoTracking().Where(m => memberIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id);
        var liked = (await db.Likes.AsNoTracking().Where(l => ids.Contains(l.PostId) && l.MembershipId == viewerMid).Select(l => l.PostId).ToListAsync()).ToHashSet();
        var saved = (await db.Saves.AsNoTracking().Where(l => ids.Contains(l.PostId) && l.MembershipId == viewerMid).Select(l => l.PostId).ToListAsync()).ToHashSet();
        return posts.Select(p =>
        {
            var author = members[p.AuthorMembershipId];
            var cards = pcs.Where(c => c.PostId == p.Id).OrderBy(c => c.Position).Select(c =>
            {
                var owner = members[owners[c.CardId]];
                var snap = JsonSerializer.Deserialize<CardDto>(c.SnapshotJson, IdempotencyService.Json)!;
                return new FeedCardView(c.Position, c.Serial, snap, owner.Nickname, owner.AccountId, owner.Id != c.OwnerAtPostMembershipId);
            }).ToList();
            return new FeedPostView(p.Id, p.Type, p.Caption, author.AccountId, author.Nickname, p.CreatedAt, p.LikeCount, p.CommentCount, liked.Contains(p.Id),
                saved.Contains(p.Id), p.Featured, p.AuthorMembershipId == viewerMid, cards, p.DataJson);
        }).ToList();
    }
}
