using Microsoft.EntityFrameworkCore;
using PetStar.Api.Auth;
using PetStar.Api.Data;
using PetStar.Api.Infrastructure;

namespace PetStar.Api.Features;

public sealed class CreateGuildRequest { public string Name { get; set; } = ""; public string Description { get; set; } = ""; public string JoinPolicy { get; set; } = "open"; }
public sealed class GuildTextRequest { public string Text { get; set; } = ""; }
public sealed class GuildRoleRequest { public string Role { get; set; } = "member"; }
public sealed class ChatSendRequest { public string Text { get; set; } = ""; }

public static class GuildChatEndpoints
{
    public static async Task<(Guild guild, GuildMember me)?> MyGuild(PetStarDb db, long mid)
    {
        var me = await db.GuildMembers.AsNoTracking().FirstOrDefaultAsync(m => m.MembershipId == mid);
        if (me == null) return null;
        return (await db.Guilds.AsNoTracking().FirstAsync(g => g.Id == me.GuildId), me);
    }

    private static int Rank(string role) => role switch { "leader" => 3, "officer" => 2, _ => 1 };

    public static void Map(WebApplication app)
    {
        var g = ServerEndpoints.GameGroup(app);

        // ---------------- Guilds ----------------
        g.MapGet("/guilds", async (string? q, HttpContext ctx, PetStarDb db) =>
        {
            var (sid, _) = ctx.User.Player();
            var query = db.Guilds.AsNoTracking().Where(x => x.ServerId == sid);
            if (!string.IsNullOrWhiteSpace(q)) query = query.Where(x => EF.Functions.ILike(x.Name, "%" + q.Trim().Replace("%", "") + "%"));
            var list = await query.OrderBy(x => x.Name).Take(50).ToListAsync();
            var counts = await db.GuildMembers.AsNoTracking().Where(m => list.Select(l => l.Id).Contains(m.GuildId)).GroupBy(m => m.GuildId)
                .Select(x => new { x.Key, N = x.Count() }).ToDictionaryAsync(x => x.Key, x => x.N);
            return list.Select(x => new { x.Id, x.Name, x.Description, x.JoinPolicy, x.MaxMembers, members = counts.GetValueOrDefault(x.Id) });
        });

        g.MapGet("/guilds/mine", async (HttpContext ctx, PetStarDb db) =>
        {
            var (_, mid) = ctx.User.Player();
            var mg = await MyGuild(db, mid);
            if (mg == null) return Results.Ok(new { guild = (object?)null });
            var (guild, me) = mg.Value;
            var members = await db.GuildMembers.AsNoTracking().Where(m => m.GuildId == guild.Id)
                .Join(db.Memberships, m => m.MembershipId, s => s.Id, (m, s) => new { uid = s.AccountId, s.Nickname, s.Level, m.Role, m.JoinedAt }).ToListAsync();
            var ann = await db.GuildAnnouncements.AsNoTracking().Where(a => a.GuildId == guild.Id).OrderByDescending(a => a.Id).Take(20)
                .Join(db.Memberships, a => a.AuthorMembershipId, s => s.Id, (a, s) => new { a.Id, a.Text, a.CreatedAt, author = s.Nickname }).ToListAsync();
            var requests = Rank(me.Role) >= 2
                ? await db.GuildJoinRequests.AsNoTracking().Where(r => r.GuildId == guild.Id && r.Status == "pending")
                    .Join(db.Memberships, r => r.MembershipId, s => s.Id, (r, s) => new { r.Id, uid = s.AccountId, s.Nickname, s.Level }).Cast<object>().ToListAsync()
                : new List<object>();
            return Results.Ok(new { guild = new { guild.Id, guild.Name, guild.Description, guild.JoinPolicy, guild.MaxMembers }, myRole = me.Role, members, announcements = ann, requests });
        });

        g.MapPost("/guilds", async (CreateGuildRequest req, HttpContext ctx, PetStarDb db, ContentGuard guard, AuditService audit) =>
        {
            var (sid, mid) = ctx.User.Player();
            await guard.EnsureAllowed(ctx.User.AccountId(), "social");
            var name = ContentGuard.Clean(req.Name, 24, true);
            if (name.Length < 3) throw ApiException.BadRequest("guild_name_too_short");
            var desc = ContentGuard.Clean(req.Description, 300, false);
            if (req.JoinPolicy is not ("open" or "approval")) throw ApiException.BadRequest("invalid_join_policy");
            if (await db.GuildMembers.AnyAsync(m => m.MembershipId == mid)) throw ApiException.Conflict("already_in_guild");
            await using var tx = await db.Database.BeginTransactionAsync();
            var guild = new Guild { ServerId = sid, Name = name, NameNormalized = name.ToUpperInvariant(), Description = desc, JoinPolicy = req.JoinPolicy, LeaderMembershipId = mid, CreatedAt = DateTimeOffset.UtcNow };
            db.Guilds.Add(guild);
            try { await db.SaveChangesAsync(); } catch (DbUpdateException) { throw ApiException.Conflict("guild_name_taken"); }
            db.GuildMembers.Add(new GuildMember { GuildId = guild.Id, MembershipId = mid, Role = "leader", JoinedAt = DateTimeOffset.UtcNow });
            try { await db.SaveChangesAsync(); } catch (DbUpdateException) { throw ApiException.Conflict("already_in_guild"); }
            audit.Add("guild.create", ctx.User.AccountId(), "guild", guild.Id.ToString(), sid);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return Results.Ok(new { guild.Id, guild.Name });
        });

        g.MapPost("/guilds/{id:long}/join", async (long id, HttpContext ctx, PetStarDb db, NotificationService notes, RealtimeHub hub) =>
        {
            var (sid, mid) = ctx.User.Player();
            await using var tx = await db.Database.BeginTransactionAsync();
            var guild = await db.Guilds.FromSqlRaw("SELECT * FROM guilds WHERE id = {0} AND server_id = {1} FOR UPDATE", id, sid).FirstOrDefaultAsync() ?? throw ApiException.NotFound("guild_not_found");
            if (await db.GuildMembers.AnyAsync(m => m.MembershipId == mid)) throw ApiException.Conflict("already_in_guild");
            if (guild.JoinPolicy == "approval")
            {
                db.GuildJoinRequests.Add(new GuildJoinRequest { GuildId = id, MembershipId = mid, CreatedAt = DateTimeOffset.UtcNow });
                try { await db.SaveChangesAsync(); } catch (DbUpdateException) { throw ApiException.Conflict("join_request_exists"); }
                var officers = await db.GuildMembers.Where(m => m.GuildId == id && m.Role != "member").Select(m => m.MembershipId).ToListAsync();
                foreach (var o in officers) await notes.Notify(o, "guild_join_request", new { guildId = id, uid = ctx.User.AccountId() }, $"guild_req:{id}:{mid}:{DateTime.UtcNow:yyyyMMddHH}");
                await tx.CommitAsync();
                return Results.Ok(new { status = "requested" });
            }
            if (await db.GuildMembers.CountAsync(m => m.GuildId == id) >= guild.MaxMembers) throw ApiException.Conflict("guild_full");
            db.GuildMembers.Add(new GuildMember { GuildId = id, MembershipId = mid, JoinedAt = DateTimeOffset.UtcNow });
            try { await db.SaveChangesAsync(); } catch (DbUpdateException) { throw ApiException.Conflict("already_in_guild"); }
            await tx.CommitAsync();
            return Results.Ok(new { status = "joined" });
        });

        g.MapPost("/guilds/requests/{id:long}/{decision}", async (long id, string decision, HttpContext ctx, PetStarDb db, NotificationService notes) =>
        {
            var (_, mid) = ctx.User.Player();
            var mg = await MyGuild(db, mid) ?? throw ApiException.Forbidden("not_in_guild");
            if (Rank(mg.me.Role) < 2) throw ApiException.Forbidden("guild_officer_required");
            await using var tx = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlRawAsync("SELECT id FROM guilds WHERE id = {0} FOR UPDATE", mg.guild.Id);
            var r = await db.GuildJoinRequests.FirstOrDefaultAsync(x => x.Id == id && x.GuildId == mg.guild.Id && x.Status == "pending") ?? throw ApiException.NotFound();
            if (decision == "accept")
            {
                if (await db.GuildMembers.CountAsync(m => m.GuildId == mg.guild.Id) >= mg.guild.MaxMembers) throw ApiException.Conflict("guild_full");
                if (await db.GuildMembers.AnyAsync(m => m.MembershipId == r.MembershipId)) { r.Status = "rejected"; await db.SaveChangesAsync(); await tx.CommitAsync(); throw ApiException.Conflict("already_in_guild"); }
                db.GuildMembers.Add(new GuildMember { GuildId = mg.guild.Id, MembershipId = r.MembershipId, JoinedAt = DateTimeOffset.UtcNow });
                r.Status = "accepted";
            }
            else if (decision == "reject") r.Status = "rejected";
            else throw ApiException.BadRequest("invalid_decision");
            await db.SaveChangesAsync();
            await notes.Notify(r.MembershipId, "guild_request_" + r.Status, new { guildId = mg.guild.Id, name = mg.guild.Name }, "guild_req_result:" + r.Id);
            await tx.CommitAsync();
            return Results.Ok(new { r.Status });
        });

        g.MapPost("/guilds/leave", async (HttpContext ctx, PetStarDb db) =>
        {
            var (_, mid) = ctx.User.Player();
            var mg = await MyGuild(db, mid) ?? throw ApiException.Conflict("not_in_guild");
            await using var tx = await db.Database.BeginTransactionAsync();
            await db.GuildMembers.Where(m => m.MembershipId == mid).ExecuteDeleteAsync();
            if (mg.me.Role == "leader")
            {
                // leadership passes to the most senior officer, then the most senior member; empty guild is dissolved
                var next = await db.GuildMembers.Where(m => m.GuildId == mg.guild.Id).OrderBy(m => m.Role == "officer" ? 0 : 1).ThenBy(m => m.JoinedAt).FirstOrDefaultAsync();
                if (next == null) await db.Guilds.Where(x => x.Id == mg.guild.Id).ExecuteDeleteAsync();
                else
                {
                    next.Role = "leader";
                    await db.Guilds.Where(x => x.Id == mg.guild.Id).ExecuteUpdateAsync(u => u.SetProperty(x => x.LeaderMembershipId, next.MembershipId));
                    await db.SaveChangesAsync();
                }
            }
            await tx.CommitAsync();
            return Results.Ok(new { left = true });
        });

        g.MapPost("/guilds/members/{uid:long}/role", async (long uid, GuildRoleRequest req, HttpContext ctx, PetStarDb db) =>
        {
            var (sid, mid) = ctx.User.Player();
            var mg = await MyGuild(db, mid) ?? throw ApiException.Forbidden("not_in_guild");
            if (mg.me.Role != "leader") throw ApiException.Forbidden("guild_leader_required");
            if (req.Role is not ("officer" or "member")) throw ApiException.BadRequest("invalid_role");
            var target = await SocialEndpoints.Member(db, uid, sid);
            var gm = await db.GuildMembers.FirstOrDefaultAsync(m => m.GuildId == mg.guild.Id && m.MembershipId == target.Id) ?? throw ApiException.NotFound("member_not_found");
            if (gm.Role == "leader") throw ApiException.BadRequest("cannot_change_leader");
            gm.Role = req.Role;
            await db.SaveChangesAsync();
            return Results.Ok(new { gm.Role });
        });

        g.MapPost("/guilds/members/{uid:long}/kick", async (long uid, HttpContext ctx, PetStarDb db, NotificationService notes) =>
        {
            var (sid, mid) = ctx.User.Player();
            var mg = await MyGuild(db, mid) ?? throw ApiException.Forbidden("not_in_guild");
            var target = await SocialEndpoints.Member(db, uid, sid);
            var gm = await db.GuildMembers.FirstOrDefaultAsync(m => m.GuildId == mg.guild.Id && m.MembershipId == target.Id) ?? throw ApiException.NotFound("member_not_found");
            if (Rank(mg.me.Role) <= Rank(gm.Role)) throw ApiException.Forbidden("guild_rank_too_low");
            db.GuildMembers.Remove(gm);
            await db.SaveChangesAsync();
            await notes.Notify(target.Id, "guild_kicked", new { guildId = mg.guild.Id, name = mg.guild.Name }, $"guild_kick:{mg.guild.Id}:{target.Id}:{DateTime.UtcNow:yyyyMMddHHmm}");
            return Results.Ok(new { kicked = true });
        });

        g.MapPost("/guilds/announcements", async (GuildTextRequest req, HttpContext ctx, PetStarDb db, ContentGuard guard, NotificationService notes) =>
        {
            var (_, mid) = ctx.User.Player();
            await guard.EnsureAllowed(ctx.User.AccountId(), "social");
            var mg = await MyGuild(db, mid) ?? throw ApiException.Forbidden("not_in_guild");
            if (Rank(mg.me.Role) < 2) throw ApiException.Forbidden("guild_officer_required");
            var a = new GuildAnnouncement { GuildId = mg.guild.Id, AuthorMembershipId = mid, Text = ContentGuard.Clean(req.Text, 500, true), CreatedAt = DateTimeOffset.UtcNow };
            db.GuildAnnouncements.Add(a);
            await db.SaveChangesAsync();
            foreach (var m in await db.GuildMembers.Where(x => x.GuildId == mg.guild.Id && x.MembershipId != mid).Select(x => x.MembershipId).ToListAsync())
                await notes.Notify(m, "guild_announcement", new { guildId = mg.guild.Id, announcementId = a.Id }, "guild_ann:" + a.Id);
            return Results.Ok(new { a.Id });
        });

        // Guild feed: posts made with guildId are visible only to members of that guild
        g.MapGet("/guilds/feed", async (long? before, HttpContext ctx, PetStarDb db) =>
        {
            var (_, mid) = ctx.User.Player();
            var mg = await MyGuild(db, mid) ?? throw ApiException.Forbidden("not_in_guild");
            var q = db.Posts.AsNoTracking().Where(p => p.GuildId == mg.guild.Id && p.Status == "visible");
            if (before.HasValue) q = q.Where(p => p.Id < before);
            var posts = await q.OrderByDescending(p => p.Id).Take(20).ToListAsync();
            return new { items = await FeedEndpoints.Views(db, posts, mid) };
        });

        g.MapPost("/guilds/feed", async (CreatePostRequest req, HttpContext ctx, PetStarDb db, GameData data, ContentGuard guard) =>
        {
            var (sid, mid) = ctx.User.Player();
            await guard.EnsureAllowed(ctx.User.AccountId(), "social");
            var mg = await MyGuild(db, mid) ?? throw ApiException.Forbidden("not_in_guild");
            var caption = ContentGuard.Clean(req.Caption, (int)data.Market.L("social", "captionMax"), true);
            if (await db.Posts.CountAsync(p => p.AuthorMembershipId == mid && p.CreatedAt > DateTimeOffset.UtcNow.AddHours(-1)) >= data.Market.L("social", "postsPerHour"))
                throw new ApiException(429, "posting_too_fast");
            var post = new FeedPost { ServerId = sid, AuthorMembershipId = mid, GuildId = mg.guild.Id, Type = "text", Caption = caption, CreatedAt = DateTimeOffset.UtcNow };
            db.Posts.Add(post);
            await db.SaveChangesAsync();
            return (await FeedEndpoints.Views(db, new List<FeedPost> { post }, mid)).Single();
        });

        // ---------------- Chat ----------------
        g.MapGet("/chat/{channel}", async (string channel, long? before, HttpContext ctx, PetStarDb db) =>
        {
            var (sid, mid) = ctx.User.Player();
            var (key, _) = await ResolveChannel(db, channel, sid, mid);
            var hidden = await MarketEndpoints.BlockedIds(db, mid);
            hidden.AddRange(await db.Mutes.AsNoTracking().Where(m => m.MuterMembershipId == mid).Select(m => m.MutedMembershipId).ToListAsync());
            var q = db.ChatMessages.AsNoTracking().Where(m => m.ServerId == sid && m.Channel == key && m.Status == "visible" && !hidden.Contains(m.AuthorMembershipId));
            if (before.HasValue) q = q.Where(m => m.Id < before);
            var rows = await q.OrderByDescending(m => m.Id).Take(50)
                .Join(db.Memberships, m => m.AuthorMembershipId, s => s.Id, (m, s) => new { m.Id, m.Text, m.CreatedAt, authorUid = s.AccountId, authorNickname = s.Nickname, mine = m.AuthorMembershipId == mid })
                .ToListAsync();
            rows.Reverse();
            return rows;
        });

        g.MapPost("/chat/{channel}", async (string channel, ChatSendRequest req, HttpContext ctx, PetStarDb db, ContentGuard guard, RealtimeHub hub) =>
        {
            var (sid, mid) = ctx.User.Player();
            await guard.EnsureAllowed(ctx.User.AccountId(), "social");
            var (key, recipients) = await ResolveChannel(db, channel, sid, mid);
            var text = ContentGuard.Clean(req.Text, 300, true);
            var now = DateTimeOffset.UtcNow;
            var recent = await db.ChatMessages.AsNoTracking().Where(m => m.AuthorMembershipId == mid && m.CreatedAt > now.AddSeconds(-30)).ToListAsync();
            if (recent.Count >= 8 || recent.Any(m => m.CreatedAt > now.AddSeconds(-1))) throw new ApiException(429, "chat_too_fast");
            if (recent.Any(m => m.Text == text && m.Channel == key)) throw ApiException.Conflict("duplicate_message");
            var msg = new ChatMessage { ServerId = sid, Channel = key, AuthorMembershipId = mid, Text = text, CreatedAt = now };
            db.ChatMessages.Add(msg);
            await db.SaveChangesAsync();
            var me = await db.Memberships.AsNoTracking().FirstAsync(m => m.Id == mid);
            var payload = new { channel = key, id = msg.Id, text, createdAt = now, authorUid = me.AccountId, authorNickname = me.Nickname };
            if (recipients == null) await hub.ToServer(sid, "chat", payload);
            else await hub.ToMembers(recipients, "chat", payload);
            return Results.Ok(new { msg.Id });
        });
    }

    /// <summary>world | guild | dm:{uid}. Returns the storage key and explicit recipients (null = whole server).</summary>
    private static async Task<(string key, List<long>? recipients)> ResolveChannel(PetStarDb db, string channel, int sid, long mid)
    {
        if (channel == "world") return ("world", null);
        if (channel == "guild")
        {
            var mg = await MyGuild(db, mid) ?? throw ApiException.Forbidden("not_in_guild");
            return ("guild:" + mg.guild.Id, await db.GuildMembers.Where(m => m.GuildId == mg.guild.Id).Select(m => m.MembershipId).ToListAsync());
        }
        if (channel.StartsWith("dm:") && long.TryParse(channel[3..], out var uid))
        {
            var other = await SocialEndpoints.Member(db, uid, sid);
            if (other.Id == mid) throw ApiException.BadRequest("cannot_dm_self");
            if (await MarketEndpoints.IsBlocked(db, mid, other.Id)) throw ApiException.Forbidden("user_blocked");
            bool friends = await db.Friendships.AnyAsync(f => f.Status == "accepted" &&
                ((f.RequesterMembershipId == mid && f.AddresseeMembershipId == other.Id) || (f.AddresseeMembershipId == mid && f.RequesterMembershipId == other.Id)));
            if (!friends) throw ApiException.Forbidden("dm_friends_only");
            long a = Math.Min(mid, other.Id), b = Math.Max(mid, other.Id);
            return ($"dm:{a}:{b}", new List<long> { mid, other.Id });
        }
        throw ApiException.BadRequest("invalid_channel");
    }
}
