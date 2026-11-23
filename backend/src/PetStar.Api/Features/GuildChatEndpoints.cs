using Microsoft.EntityFrameworkCore;
using PetStar.Api.Auth;
using PetStar.Api.Data;
using PetStar.Api.Infrastructure;

namespace PetStar.Api.Features;

public sealed class CreateGuildRequest
{
    public string Name { get; set; } = ""; public string Description { get; set; } = ""; public string JoinPolicy { get; set; } = "open";
    public string? Tag { get; set; } public string? EmblemId { get; set; } public string? BannerId { get; set; } public string? Visibility { get; set; } public int? MinLevel { get; set; }
}
public sealed class UpdateGuildRequest { public string? Description { get; set; } public string? JoinPolicy { get; set; } public string? EmblemId { get; set; } public string? BannerId { get; set; } public string? Visibility { get; set; } public int? MinLevel { get; set; } }
public sealed class GuildTextRequest { public string Text { get; set; } = ""; }
public sealed class GuildRoleRequest { public string Role { get; set; } = "member"; }
public sealed class GuildInviteRequest { public long Uid { get; set; } }
public sealed class GuildDissolveRequest { public string Confirm { get; set; } = ""; }
public sealed class ChatSendRequest { public string Text { get; set; } = ""; }

public static class GuildChatEndpoints
{
    public static async Task<(Guild guild, GuildMember me)?> MyGuild(PetStarDb db, long mid)
    {
        var me = await db.GuildMembers.AsNoTracking().FirstOrDefaultAsync(m => m.MembershipId == mid);
        if (me == null) return null;
        return (await db.Guilds.AsNoTracking().FirstAsync(g => g.Id == me.GuildId), me);
    }

    private static readonly string[] Policies = { "open", "approval" };

    public static void Map(WebApplication app)
    {
        var g = ServerEndpoints.GameGroup(app);

        // ---------------- Guilds ----------------
        g.MapGet("/guilds", async (string? q, HttpContext ctx, PetStarDb db) =>
        {
            var (sid, _) = ctx.User.Player();
            var query = db.Guilds.AsNoTracking().Where(x => x.ServerId == sid && x.Visibility == "public");   // private guilds are invite-only and not listed
            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = "%" + q.Trim().Replace("%", "").Replace("_", "") + "%";
                query = query.Where(x => EF.Functions.ILike(x.Name, term) || EF.Functions.ILike(x.Tag, term));
            }
            var list = await query.OrderByDescending(x => x.Level).ThenBy(x => x.Name).Take(50).ToListAsync();
            var counts = await db.GuildMembers.AsNoTracking().Where(m => list.Select(l => l.Id).Contains(m.GuildId)).GroupBy(m => m.GuildId)
                .Select(x => new { x.Key, N = x.Count() }).ToDictionaryAsync(x => x.Key, x => x.N);
            return list.Select(x => new { x.Id, x.Name, x.Tag, x.Description, x.JoinPolicy, x.MaxMembers, x.Level, x.EmblemId, x.BannerId, x.MinLevel, members = counts.GetValueOrDefault(x.Id) });
        });

        // Guild Hall
        g.MapGet("/guilds/mine", async (HttpContext ctx, PetStarDb db, GuildConfig cfg) =>
        {
            var (_, mid) = ctx.User.Player();
            var mg = await MyGuild(db, mid);
            if (mg == null)
            {
                var invites = await db.GuildInvites.AsNoTracking().Where(i => i.MembershipId == mid && i.Status == "pending")
                    .Join(db.Guilds, i => i.GuildId, x => x.Id, (i, x) => new { i.Id, guildId = x.Id, x.Name, x.Tag, x.Level }).ToListAsync();
                return Results.Ok(new { guild = (object?)null, invites });
            }
            var (guild, me) = mg.Value;
            var week = GuildService.Week(DateTimeOffset.UtcNow);
            var onlineSince = DateTimeOffset.UtcNow.AddMinutes(-5);
            var contrib = await db.GuildContributions.AsNoTracking().Where(c => c.GuildId == guild.Id && (c.PeriodKey == week || c.PeriodKey == "all")).ToListAsync();
            var members = (await db.GuildMembers.AsNoTracking().Where(m => m.GuildId == guild.Id)
                .Join(db.Memberships, m => m.MembershipId, s => s.Id, (m, s) => new { s.Id, uid = s.AccountId, s.Nickname, s.Level, m.Role, m.JoinedAt, online = s.LastSeenAt > onlineSince }).ToListAsync())
                .Select(m => new { m.uid, m.Nickname, m.Level, m.Role, m.JoinedAt, m.online,
                    weekPoints = contrib.FirstOrDefault(c => c.MembershipId == m.Id && c.PeriodKey == week)?.Points ?? 0,
                    totalPoints = contrib.FirstOrDefault(c => c.MembershipId == m.Id && c.PeriodKey == "all")?.Points ?? 0 })
                .OrderByDescending(m => cfg.Rank(m.Role)).ThenByDescending(m => m.weekPoints).ToList();
            var ann = await db.GuildAnnouncements.AsNoTracking().Where(a => a.GuildId == guild.Id).OrderByDescending(a => a.Id).Take(20)
                .Join(db.Memberships, a => a.AuthorMembershipId, s => s.Id, (a, s) => new { a.Id, a.Text, a.CreatedAt, author = s.Nickname }).ToListAsync();
            var requests = cfg.Rank(me.Role) >= cfg.Rank("officer")
                ? await db.GuildJoinRequests.AsNoTracking().Where(r => r.GuildId == guild.Id && r.Status == "pending")
                    .Join(db.Memberships, r => r.MembershipId, s => s.Id, (r, s) => new { r.Id, uid = s.AccountId, s.Nickname, s.Level }).Cast<object>().ToListAsync()
                : new List<object>();
            var logs = await db.GuildLogs.AsNoTracking().Where(l => l.GuildId == guild.Id).OrderByDescending(l => l.Id).Take(30).Select(l => new { l.Action, l.DataJson, l.At }).ToListAsync();
            long nextExp = guild.Level < cfg.Levels.Count ? cfg.Levels[guild.Level] : guild.Exp;
            return Results.Ok(new
            {
                guild = new { guild.Id, guild.Name, guild.Tag, guild.Description, guild.JoinPolicy, guild.MaxMembers, guild.Level, guild.Exp, nextExp, guild.EmblemId, guild.BannerId, guild.Visibility, guild.MinLevel },
                myRole = me.Role, members, online = members.Count(m => m.online), announcements = ann, requests, logs,
            });
        });

        g.MapPost("/guilds", async (CreateGuildRequest req, HttpContext ctx, PetStarDb db, ContentGuard guard, AuditService audit, GuildConfig cfg, GuildService gs) =>
        {
            var (sid, mid) = ctx.User.Player();
            await guard.EnsureAllowed(ctx.User.AccountId(), "social");
            var name = ContentGuard.Clean(req.Name, 24, true);
            if (name.Length < 3) throw ApiException.BadRequest("guild_name_too_short");
            if (ContentGuard.Assess(name).Count > 0) throw ApiException.BadRequest("text_inappropriate");
            var desc = ContentGuard.Clean(req.Description, 300, false);
            if (!Policies.Contains(req.JoinPolicy)) throw ApiException.BadRequest("invalid_join_policy");
            var tag = (req.Tag ?? "").Trim().ToUpperInvariant();
            if (tag.Length > 0 && (tag.Length is < 2 or > 5 || !tag.All(char.IsLetterOrDigit))) throw ApiException.BadRequest("invalid_guild_tag");
            var emblem = req.EmblemId ?? cfg.Emblems[0]; var banner = req.BannerId ?? cfg.Banners[0];
            if (!cfg.Emblems.Contains(emblem) || !cfg.Banners.Contains(banner)) throw ApiException.BadRequest("invalid_guild_art");
            var vis = req.Visibility ?? "public";
            if (vis is not ("public" or "private")) throw ApiException.BadRequest("invalid_visibility");
            if (await db.GuildMembers.AnyAsync(m => m.MembershipId == mid)) throw ApiException.Conflict("already_in_guild");
            await using var tx = await db.Database.BeginTransactionAsync();
            var guild = new Guild { ServerId = sid, Name = name, NameNormalized = name.ToUpperInvariant(), Description = desc, JoinPolicy = req.JoinPolicy, LeaderMembershipId = mid, CreatedAt = DateTimeOffset.UtcNow,
                Tag = tag, EmblemId = emblem, BannerId = banner, Visibility = vis, MinLevel = Math.Clamp(req.MinLevel ?? 1, 1, 100), MaxMembers = cfg.MaxMembers(1) };
            db.Guilds.Add(guild);
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateException e) { throw ApiException.Conflict(e.InnerException?.Message.Contains("tag") == true ? "guild_tag_taken" : "guild_name_taken"); }
            db.GuildMembers.Add(new GuildMember { GuildId = guild.Id, MembershipId = mid, Role = "leader", JoinedAt = DateTimeOffset.UtcNow });
            try { await db.SaveChangesAsync(); } catch (DbUpdateException) { throw ApiException.Conflict("already_in_guild"); }
            audit.Add("guild.create", ctx.User.AccountId(), "guild", guild.Id.ToString(), sid);
            await db.SaveChangesAsync();
            await gs.Log(guild.Id, mid, "created", new { name, tag });
            await tx.CommitAsync();
            return Results.Ok(new { guild.Id, guild.Name, guild.Tag });
        });

        g.MapPut("/guilds/mine", async (UpdateGuildRequest req, HttpContext ctx, PetStarDb db, GuildConfig cfg, GuildService gs) =>
        {
            var (_, mid) = ctx.User.Player();
            var mg = await MyGuild(db, mid) ?? throw ApiException.Forbidden("not_in_guild");
            if (cfg.Rank(mg.me.Role) < cfg.Rank("vice")) throw ApiException.Forbidden("guild_vice_required");
            var gu = await db.Guilds.FirstAsync(x => x.Id == mg.guild.Id);
            if (req.Description != null) gu.Description = ContentGuard.Clean(req.Description, 300, false);
            if (req.JoinPolicy != null) gu.JoinPolicy = Policies.Contains(req.JoinPolicy) ? req.JoinPolicy : throw ApiException.BadRequest("invalid_join_policy");
            if (req.EmblemId != null) gu.EmblemId = cfg.Emblems.Contains(req.EmblemId) ? req.EmblemId : throw ApiException.BadRequest("invalid_guild_art");
            if (req.BannerId != null) gu.BannerId = cfg.Banners.Contains(req.BannerId) ? req.BannerId : throw ApiException.BadRequest("invalid_guild_art");
            if (req.Visibility != null) gu.Visibility = req.Visibility is "public" or "private" ? req.Visibility : throw ApiException.BadRequest("invalid_visibility");
            if (req.MinLevel != null) gu.MinLevel = Math.Clamp(req.MinLevel.Value, 1, 100);
            await db.SaveChangesAsync();
            await gs.Log(gu.Id, mid, "settings_changed");
            return Results.Ok(new { updated = true });
        });

        g.MapPost("/guilds/{id:long}/join", async (long id, HttpContext ctx, PetStarDb db, NotificationService notes, GuildConfig cfg, GuildService gs) =>
        {
            var (sid, mid) = ctx.User.Player();
            await using var tx = await db.Database.BeginTransactionAsync();
            var guild = await db.Guilds.FromSqlRaw("SELECT * FROM guilds WHERE id = {0} AND server_id = {1} FOR UPDATE", id, sid).FirstOrDefaultAsync() ?? throw ApiException.NotFound("guild_not_found");
            if (await db.GuildMembers.AnyAsync(m => m.MembershipId == mid)) throw ApiException.Conflict("already_in_guild");
            var invite = await db.GuildInvites.FirstOrDefaultAsync(i => i.GuildId == id && i.MembershipId == mid && i.Status == "pending");
            if (invite == null)
            {
                if (guild.Visibility == "private") throw ApiException.Forbidden("guild_invite_only");
                var me = await db.Memberships.AsNoTracking().FirstAsync(m => m.Id == mid);
                if (me.Level < guild.MinLevel) throw ApiException.Forbidden("guild_level_too_low");
            }
            if (invite == null && guild.JoinPolicy == "approval")
            {
                db.GuildJoinRequests.Add(new GuildJoinRequest { GuildId = id, MembershipId = mid, CreatedAt = DateTimeOffset.UtcNow });
                try { await db.SaveChangesAsync(); } catch (DbUpdateException) { throw ApiException.Conflict("join_request_exists"); }
                var officers = await db.GuildMembers.Where(m => m.GuildId == id && m.Role != "member" && m.Role != "elite").Select(m => m.MembershipId).ToListAsync();
                foreach (var o in officers) await notes.Notify(o, "guild_join_request", new { guildId = id, uid = ctx.User.AccountId() }, $"guild_req:{id}:{mid}:{DateTime.UtcNow:yyyyMMddHH}");
                await tx.CommitAsync();
                return Results.Ok(new { status = "requested" });
            }
            if (await db.GuildMembers.CountAsync(m => m.GuildId == id) >= guild.MaxMembers) throw ApiException.Conflict("guild_full");
            db.GuildMembers.Add(new GuildMember { GuildId = id, MembershipId = mid, JoinedAt = DateTimeOffset.UtcNow });
            if (invite != null) invite.Status = "accepted";
            try { await db.SaveChangesAsync(); } catch (DbUpdateException) { throw ApiException.Conflict("already_in_guild"); }
            await gs.Log(id, mid, "joined");
            await tx.CommitAsync();
            return Results.Ok(new { status = "joined" });
        });

        g.MapPost("/guilds/invites", async (GuildInviteRequest req, HttpContext ctx, PetStarDb db, NotificationService notes, GuildConfig cfg, GuildService gs) =>
        {
            var (sid, mid) = ctx.User.Player();
            var mg = await MyGuild(db, mid) ?? throw ApiException.Forbidden("not_in_guild");
            if (cfg.Rank(mg.me.Role) < cfg.Rank("officer")) throw ApiException.Forbidden("guild_officer_required");
            var target = await SocialEndpoints.Member(db, req.Uid, sid);
            if (await MarketEndpoints.IsBlocked(db, mid, target.Id)) throw ApiException.Forbidden("user_blocked");
            if (await db.GuildMembers.AnyAsync(m => m.MembershipId == target.Id)) throw ApiException.Conflict("already_in_guild");
            if (await db.GuildInvites.CountAsync(i => i.InviterMembershipId == mid && i.CreatedAt > DateTimeOffset.UtcNow.AddHours(-1)) >= 20) throw new ApiException(429, "too_many_invites");
            var inv = new GuildInvite { GuildId = mg.guild.Id, MembershipId = target.Id, InviterMembershipId = mid, CreatedAt = DateTimeOffset.UtcNow };
            db.GuildInvites.Add(inv);
            try { await db.SaveChangesAsync(); } catch (DbUpdateException) { throw ApiException.Conflict("invite_exists"); }
            await notes.Notify(target.Id, "guild_invite", new { guildId = mg.guild.Id, name = mg.guild.Name, inviteId = inv.Id, uid = ctx.User.AccountId() }, "guild_inv:" + inv.Id);
            await gs.Log(mg.guild.Id, mid, "invited", new { uid = req.Uid });
            return Results.Ok(new { inviteId = inv.Id });
        });

        g.MapPost("/guilds/invites/{id:long}/decline", async (long id, HttpContext ctx, PetStarDb db) =>
        {
            var (_, mid) = ctx.User.Player();
            int n = await db.GuildInvites.Where(i => i.Id == id && i.MembershipId == mid && i.Status == "pending").ExecuteUpdateAsync(u => u.SetProperty(i => i.Status, "declined"));
            return n == 0 ? throw ApiException.NotFound("invite_not_found") : Results.Ok(new { declined = true });
        });

        g.MapPost("/guilds/requests/{id:long}/{decision}", async (long id, string decision, HttpContext ctx, PetStarDb db, NotificationService notes, GuildConfig cfg, GuildService gs) =>
        {
            var (_, mid) = ctx.User.Player();
            var mg = await MyGuild(db, mid) ?? throw ApiException.Forbidden("not_in_guild");
            if (cfg.Rank(mg.me.Role) < cfg.Rank("officer")) throw ApiException.Forbidden("guild_officer_required");
            await using var tx = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlRawAsync("SELECT id FROM guilds WHERE id = {0} FOR UPDATE", mg.guild.Id);
            var r = await db.GuildJoinRequests.FirstOrDefaultAsync(x => x.Id == id && x.GuildId == mg.guild.Id && x.Status == "pending") ?? throw ApiException.NotFound();
            if (decision == "accept")
            {
                var max = await db.Guilds.Where(x => x.Id == mg.guild.Id).Select(x => x.MaxMembers).FirstAsync();
                if (await db.GuildMembers.CountAsync(m => m.GuildId == mg.guild.Id) >= max) throw ApiException.Conflict("guild_full");
                if (await db.GuildMembers.AnyAsync(m => m.MembershipId == r.MembershipId)) { r.Status = "rejected"; await db.SaveChangesAsync(); await tx.CommitAsync(); throw ApiException.Conflict("already_in_guild"); }
                db.GuildMembers.Add(new GuildMember { GuildId = mg.guild.Id, MembershipId = r.MembershipId, JoinedAt = DateTimeOffset.UtcNow });
                r.Status = "accepted";
            }
            else if (decision == "reject") r.Status = "rejected";
            else throw ApiException.BadRequest("invalid_decision");
            await db.SaveChangesAsync();
            await notes.Notify(r.MembershipId, "guild_request_" + r.Status, new { guildId = mg.guild.Id, name = mg.guild.Name }, "guild_req_result:" + r.Id);
            await gs.Log(mg.guild.Id, mid, "request_" + r.Status);
            await tx.CommitAsync();
            return Results.Ok(new { r.Status });
        });

        g.MapPost("/guilds/leave", async (HttpContext ctx, PetStarDb db, GuildConfig cfg, GuildService gs) =>
        {
            var (_, mid) = ctx.User.Player();
            var mg = await MyGuild(db, mid) ?? throw ApiException.Conflict("not_in_guild");
            await using var tx = await db.Database.BeginTransactionAsync();
            await db.GuildMembers.Where(m => m.MembershipId == mid).ExecuteDeleteAsync();
            if (mg.me.Role == "leader")
            {
                // leadership passes to the highest role, then the most senior member; an empty guild is dissolved
                var rest = await db.GuildMembers.Where(m => m.GuildId == mg.guild.Id).ToListAsync();
                var next = rest.OrderByDescending(m => cfg.Rank(m.Role)).ThenBy(m => m.JoinedAt).FirstOrDefault();
                if (next == null) await Dissolve(db, mg.guild.Id);
                else
                {
                    next.Role = "leader";
                    await db.Guilds.Where(x => x.Id == mg.guild.Id).ExecuteUpdateAsync(u => u.SetProperty(x => x.LeaderMembershipId, next.MembershipId));
                    await db.SaveChangesAsync();
                    await gs.Log(mg.guild.Id, next.MembershipId, "new_master");
                }
            }
            else await gs.Log(mg.guild.Id, mid, "left");
            await tx.CommitAsync();
            return Results.Ok(new { left = true });
        });

        // Safe dissolve: Guild Master only, typed confirmation (guild name), only when no other members remain or all agree by leaving.
        g.MapPost("/guilds/dissolve", async (GuildDissolveRequest req, HttpContext ctx, PetStarDb db, AuditService audit) =>
        {
            var (sid, mid) = ctx.User.Player();
            var mg = await MyGuild(db, mid) ?? throw ApiException.Forbidden("not_in_guild");
            if (mg.me.Role != "leader") throw ApiException.Forbidden("guild_leader_required");
            if (!string.Equals(req.Confirm.Trim(), mg.guild.Name, StringComparison.OrdinalIgnoreCase)) throw ApiException.BadRequest("confirm_name_mismatch");
            if (await db.GuildMembers.CountAsync(m => m.GuildId == mg.guild.Id) > 1) throw ApiException.Conflict("guild_has_members");
            if (await db.GuildWarEntries.AnyAsync(e => e.GuildId == mg.guild.Id && e.SettledAt == null)) throw ApiException.Conflict("guild_war_in_progress");
            await using var tx = await db.Database.BeginTransactionAsync();
            await Dissolve(db, mg.guild.Id);
            audit.Add("guild.dissolve", ctx.User.AccountId(), "guild", mg.guild.Id.ToString(), sid);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return Results.Ok(new { dissolved = true });
        });

        g.MapPost("/guilds/members/{uid:long}/role", async (long uid, GuildRoleRequest req, HttpContext ctx, PetStarDb db, GuildConfig cfg, GuildService gs, NotificationService notes) =>
        {
            var (sid, mid) = ctx.User.Player();
            var mg = await MyGuild(db, mid) ?? throw ApiException.Forbidden("not_in_guild");
            if (!cfg.Roles.Contains(req.Role)) throw ApiException.BadRequest("invalid_role");
            var target = await SocialEndpoints.Member(db, uid, sid);
            var gm = await db.GuildMembers.FirstOrDefaultAsync(m => m.GuildId == mg.guild.Id && m.MembershipId == target.Id) ?? throw ApiException.NotFound("member_not_found");
            if (gm.Role == "leader") throw ApiException.BadRequest("cannot_change_leader");
            int mine = cfg.Rank(mg.me.Role);
            await using var tx = await db.Database.BeginTransactionAsync();
            if (req.Role == "leader")
            {
                // transfer Guild Master (the current master becomes vice)
                if (mg.me.Role != "leader") throw ApiException.Forbidden("guild_leader_required");
                gm.Role = "leader";
                await db.GuildMembers.Where(m => m.GuildId == mg.guild.Id && m.MembershipId == mid).ExecuteUpdateAsync(u => u.SetProperty(m => m.Role, "vice"));
                await db.Guilds.Where(x => x.Id == mg.guild.Id).ExecuteUpdateAsync(u => u.SetProperty(x => x.LeaderMembershipId, target.Id));
            }
            else
            {
                // a member may only manage strictly lower ranks and only assign roles below their own (master assigns vice)
                if (mine < cfg.Rank("officer") || cfg.Rank(gm.Role) >= mine || cfg.Rank(req.Role) >= mine) throw ApiException.Forbidden(mg.me.Role == "leader" ? "guild_rank_too_low" : "guild_leader_required");
                gm.Role = req.Role;
            }
            await db.SaveChangesAsync();
            await gs.Log(mg.guild.Id, mid, "role_changed", new { uid, role = req.Role });
            await notes.Notify(target.Id, "guild_role_changed", new { guildId = mg.guild.Id, role = req.Role }, $"guild_role:{mg.guild.Id}:{target.Id}:{req.Role}:{DateTime.UtcNow:yyyyMMddHHmm}");
            await tx.CommitAsync();
            return Results.Ok(new { gm.Role });
        });

        g.MapPost("/guilds/members/{uid:long}/kick", async (long uid, HttpContext ctx, PetStarDb db, NotificationService notes, GuildConfig cfg, GuildService gs) =>
        {
            var (sid, mid) = ctx.User.Player();
            var mg = await MyGuild(db, mid) ?? throw ApiException.Forbidden("not_in_guild");
            var target = await SocialEndpoints.Member(db, uid, sid);
            var gm = await db.GuildMembers.FirstOrDefaultAsync(m => m.GuildId == mg.guild.Id && m.MembershipId == target.Id) ?? throw ApiException.NotFound("member_not_found");
            if (cfg.Rank(mg.me.Role) < cfg.Rank("officer") || cfg.Rank(mg.me.Role) <= cfg.Rank(gm.Role)) throw ApiException.Forbidden("guild_rank_too_low");
            db.GuildMembers.Remove(gm);
            await db.SaveChangesAsync();
            await gs.Log(mg.guild.Id, mid, "kicked", new { uid });
            await notes.Notify(target.Id, "guild_kicked", new { guildId = mg.guild.Id, name = mg.guild.Name }, $"guild_kick:{mg.guild.Id}:{target.Id}:{DateTime.UtcNow:yyyyMMddHHmm}");
            return Results.Ok(new { kicked = true });
        });

        g.MapPost("/guilds/announcements", async (GuildTextRequest req, HttpContext ctx, PetStarDb db, ContentGuard guard, NotificationService notes, GuildConfig cfg) =>
        {
            var (_, mid) = ctx.User.Player();
            await guard.EnsureAllowed(ctx.User.AccountId(), "social");
            var mg = await MyGuild(db, mid) ?? throw ApiException.Forbidden("not_in_guild");
            if (cfg.Rank(mg.me.Role) < cfg.Rank("officer")) throw ApiException.Forbidden("guild_officer_required");
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
            var hidden = await MarketEndpoints.BlockedIds(db, mid);
            var q = db.Posts.AsNoTracking().Where(p => p.GuildId == mg.guild.Id && p.Status == "visible" && !hidden.Contains(p.AuthorMembershipId));
            if (before.HasValue) q = q.Where(p => p.Id < before);
            var posts = await q.OrderByDescending(p => p.Id).Take(20).ToListAsync();
            return new { items = await FeedEndpoints.Views(db, posts, mid) };
        });

        g.MapPost("/guilds/feed", async (CreatePostRequest req, HttpContext ctx, PetStarDb db, GameData data, ContentGuard guard) =>
        {
            var (sid, mid) = ctx.User.Player();
            _ = await MyGuild(db, mid) ?? throw ApiException.Forbidden("not_in_guild");
            var post = await FeedEndpoints.CreatePost(req, ctx, db, data, guard, sid, mid, null, guildFeed: true);
            return (await FeedEndpoints.Views(db, new List<FeedPost> { post }, mid)).Single();
        });

        // ---------------- Missions / Boss / War ----------------
        g.MapGet("/guilds/missions", async (HttpContext ctx, PetStarDb db, GuildService gs) =>
        {
            var (_, mid) = ctx.User.Player();
            var mg = await MyGuild(db, mid) ?? throw ApiException.Forbidden("not_in_guild");
            return Results.Ok(await gs.Missions(mg.guild.Id, mid));
        });

        g.MapPost("/guilds/missions/{id}/claim", async (string id, HttpContext ctx, PetStarDb db, GuildService gs) =>
        {
            var (sid, mid) = ctx.User.Player();
            var mg = await MyGuild(db, mid) ?? throw ApiException.Forbidden("not_in_guild");
            return Results.Ok(await gs.Claim(mg.guild.Id, mid, sid, id));
        });

        g.MapGet("/guilds/boss", async (HttpContext ctx, PetStarDb db, GuildService gs) =>
        {
            var (_, mid) = ctx.User.Player();
            var mg = await MyGuild(db, mid) ?? throw ApiException.Forbidden("not_in_guild");
            var boss = await gs.CurrentBoss(mg.guild.Id);
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var board = await db.GuildBossAttacks.AsNoTracking().Where(a => a.BossId == boss.Id).GroupBy(a => a.MembershipId)
                .Select(x => new { mid = x.Key, damage = x.Sum(a => a.Damage), attacks = x.Count() }).OrderByDescending(x => x.damage).Take(30).ToListAsync();
            var badges = await SocialAccess.Badges(db, board.Select(b => b.mid));
            var history = await db.GuildBosses.AsNoTracking().Where(b => b.GuildId == mg.guild.Id && b.Id != boss.Id).OrderByDescending(b => b.Id).Take(8)
                .Select(b => new { b.PeriodKey, b.BossId, b.Status, b.MaxHp, b.Hp, b.DefeatedAt }).ToListAsync();
            var def = gs.Config.Boss.Bosses.First(b => b.Id == boss.BossId);
            return Results.Ok(new
            {
                boss = new { boss.Id, boss.BossId, def.FormId, boss.MaxHp, boss.Hp, boss.Status, boss.PeriodKey },
                attacksLeft = gs.Config.Boss.AttacksPerDay - await db.GuildBossAttacks.CountAsync(a => a.BossId == boss.Id && a.MembershipId == mid && a.Day == today),
                board = board.Select((b, i) => new { rank = i + 1, badges[b.mid].Uid, badges[b.mid].Nickname, b.damage, b.attacks }),
                history, rewardOnDefeat = gs.Config.Boss.RewardOnDefeat,
            });
        });

        g.MapPost("/guilds/boss/attack", async (HttpContext ctx, PetStarDb db, GuildService gs, TeamService teams) =>
        {
            var (sid, mid) = ctx.User.Player();
            var mg = await MyGuild(db, mid) ?? throw ApiException.Forbidden("not_in_guild");
            return Results.Ok(await gs.AttackBoss(mg.guild.Id, mid, sid, teams));
        });

        g.MapGet("/guilds/war", async (HttpContext ctx, PetStarDb db, GuildService gs) =>
        {
            var (sid, mid) = ctx.User.Player();
            var mg = await MyGuild(db, mid) ?? throw ApiException.Forbidden("not_in_guild");
            await gs.SettleWars(sid);
            var week = GuildService.Week(DateTimeOffset.UtcNow);
            var mine = await db.GuildWarEntries.AsNoTracking().FirstOrDefaultAsync(e => e.GuildId == mg.guild.Id && e.PeriodKey == week);
            var opp = mine?.OpponentGuildId == null ? null : await db.GuildWarEntries.AsNoTracking().FirstOrDefaultAsync(e => e.GuildId == mine.OpponentGuildId && e.PeriodKey == week);
            var oppGuild = opp == null ? null : await db.Guilds.AsNoTracking().Where(x => x.Id == opp.GuildId).Select(x => new { x.Id, x.Name, x.Tag, x.Level, x.EmblemId }).FirstOrDefaultAsync();
            var history = await db.GuildWarEntries.AsNoTracking().Where(e => e.GuildId == mg.guild.Id && e.PeriodKey != week).OrderByDescending(e => e.Id).Take(10)
                .Select(e => new { e.PeriodKey, e.Points, e.Wins, e.Battles, e.Result }).ToListAsync();
            var standings = await db.GuildWarEntries.AsNoTracking().Where(e => e.ServerId == sid && e.PeriodKey == week)
                .Join(db.Guilds, e => e.GuildId, x => x.Id, (e, x) => new { x.Name, x.Tag, e.Points, e.Wins, e.Battles }).OrderByDescending(x => x.Points).ThenByDescending(x => x.Wins).Take(20).ToListAsync();
            return Results.Ok(new { week, registered = mine != null, entry = mine == null ? null : new { mine.Id, mine.Points, mine.Wins, mine.Battles }, opponent = oppGuild, opponentPoints = opp?.Points, history, standings, rules = gs.Config.War });
        });

        g.MapPost("/guilds/war/register", async (HttpContext ctx, PetStarDb db, GuildService gs, GuildConfig cfg) =>
        {
            var (sid, mid) = ctx.User.Player();
            var mg = await MyGuild(db, mid) ?? throw ApiException.Forbidden("not_in_guild");
            if (cfg.Rank(mg.me.Role) < cfg.Rank("officer")) throw ApiException.Forbidden("guild_officer_required");
            var week = GuildService.Week(DateTimeOffset.UtcNow);
            int n = await db.Database.ExecuteSqlRawAsync("INSERT INTO guild_war_entries (server_id, period_key, guild_id, points, wins, battles, result, registered_at) VALUES ({0}, {1}, {2}, 0, 0, 0, 'pending', now()) ON CONFLICT DO NOTHING", sid, week, mg.guild.Id);
            if (n == 0) throw ApiException.Conflict("already_registered");
            await gs.Log(mg.guild.Id, mid, "war_registered", new { week });
            await gs.Pair(sid, week);
            return Results.Ok(new { registered = true });
        });

        // A war battle: your saved team vs an opponent member's saved team (AI controlled), played turn by turn on the 3v3 server.
        g.MapPost("/guilds/war/battle", async (HttpContext ctx, PetStarDb db, GuildService gs, TeamService teams) =>
        {
            var (sid, mid) = ctx.User.Player();
            var mg = await MyGuild(db, mid) ?? throw ApiException.Forbidden("not_in_guild");
            var week = GuildService.Week(DateTimeOffset.UtcNow);
            var mine = await db.GuildWarEntries.AsNoTracking().FirstOrDefaultAsync(e => e.GuildId == mg.guild.Id && e.PeriodKey == week) ?? throw ApiException.Conflict("war_not_registered");
            if (mine.OpponentGuildId == null) throw ApiException.Conflict("war_no_opponent");
            var since = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero);
            int today = await db.ArenaMatchPlayers.AsNoTracking().Where(p => p.MembershipId == mid && p.Side == 0)
                .Join(db.ArenaMatches.Where(m => m.Mode == "guildwar" && m.CreatedAt >= since), p => p.MatchId, m => m.Id, (p, m) => m.Id).CountAsync();
            if (today >= gs.Config.War.BattlesPerMemberPerDay) throw ApiException.Conflict("war_battles_used");
            if (await db.ArenaMatchPlayers.AnyAsync(p => p.MembershipId == mid && db.ArenaMatches.Any(m => m.Id == p.MatchId && m.Status == "active"))) throw ApiException.Conflict("already_in_match");
            var (units, _) = await teams.PlayerTeam(mid, sid, null);
            var defenders = await db.GuildMembers.AsNoTracking().Where(m => m.GuildId == mine.OpponentGuildId)
                .Join(db.TeamLineups.Where(l => l.IsDefault), m => m.MembershipId, l => l.MembershipId, (m, l) => l).ToListAsync();
            if (defenders.Count == 0) throw ApiException.Conflict("war_no_defenders");
            var pick = defenders[Random.Shared.Next(defenders.Count)];
            List<PetStar.Rules.Team.TeamSetupUnit> def;
            try { def = await teams.BuildTeam(pick.MembershipId, sid, pick.CardIds, ranked: true); }
            catch (ApiException) { throw ApiException.Conflict("war_defender_unavailable"); }
            var me = await db.Memberships.AsNoTracking().FirstAsync(m => m.Id == mid);
            var foe = await db.Memberships.AsNoTracking().FirstAsync(m => m.Id == pick.MembershipId);
            var m = await teams.Start(sid, "guildwar", new TeamService.Side(mid, me.Nickname, units, false), new TeamService.Side(null, foe.Nickname, def, true), stageId: "gw:" + mine.Id);
            return Results.Ok(await teams.Dto(m, TeamService.Load(m), mid));
        });

        // ---------------- Chat ----------------
        g.MapGet("/chat/{channel}", async (string channel, long? before, HttpContext ctx, PetStarDb db) =>
        {
            var (sid, mid) = ctx.User.Player();
            var (key, _) = await ResolveChannel(db, channel, sid, mid);
            var hidden = await MarketEndpoints.BlockedIds(db, mid);
            hidden.AddRange(await db.Mutes.AsNoTracking().Where(m => m.MuterMembershipId == mid).Select(m => m.MutedMembershipId).ToListAsync());
            var q = db.ChatMessages.AsNoTracking().Where(m => m.ServerId == sid && m.Channel == key && (m.Status == "visible" || (m.Status == "pending" && m.AuthorMembershipId == mid)) && !hidden.Contains(m.AuthorMembershipId));
            if (before.HasValue) q = q.Where(m => m.Id < before);
            var rows = await q.OrderByDescending(m => m.Id).Take(50).ToListAsync();
            rows.Reverse();
            var badges = await SocialAccess.Badges(db, rows.Select(r => r.AuthorMembershipId));
            if (rows.Count > 0 && before == null)
                await db.Database.ExecuteSqlRawAsync("INSERT INTO chat_read_markers (membership_id, channel, last_read_id) VALUES ({0}, {1}, {2}) ON CONFLICT (membership_id, channel) DO UPDATE SET last_read_id = GREATEST(chat_read_markers.last_read_id, EXCLUDED.last_read_id)", mid, key, rows[^1].Id);
            return rows.Select(m => { var b = badges[m.AuthorMembershipId]; return new { m.Id, m.Text, m.CreatedAt, authorUid = b.Uid, authorNickname = b.Nickname, mine = m.AuthorMembershipId == mid, bubble = b.BubbleId, frame = b.FrameId, status = m.Status, guildTag = b.GuildTag }; }).ToList();
        });

        // Unread counts per channel (world, guild, each friend DM) for the lobby chat dock badges.
        g.MapGet("/chat-unread", async (HttpContext ctx, PetStarDb db) =>
        {
            var (sid, mid) = ctx.User.Player();
            var keys = new List<string> { "world" };
            var gid = await SocialAccess.GuildOf(db, mid);
            if (gid != null) keys.Add("guild:" + gid);
            var friends = await SocialAccess.FriendIds(db, mid);
            keys.AddRange(friends.Select(f => $"dm:{Math.Min(mid, f)}:{Math.Max(mid, f)}"));
            var markers = await db.ChatReadMarkers.AsNoTracking().Where(r => r.MembershipId == mid && keys.Contains(r.Channel)).ToDictionaryAsync(r => r.Channel, r => r.LastReadId);
            var unread = new Dictionary<string, int>();
            foreach (var k in keys)
            {
                long last = markers.GetValueOrDefault(k);
                unread[k] = await db.ChatMessages.AsNoTracking().CountAsync(m => m.ServerId == sid && m.Channel == k && m.Id > last && m.Status == "visible" && m.AuthorMembershipId != mid);
            }
            var friendMembers = await db.Memberships.AsNoTracking().Where(m => friends.Contains(m.Id)).Select(m => new { m.Id, m.AccountId, m.Nickname, online = m.LastSeenAt > DateTimeOffset.UtcNow.AddMinutes(-5) }).ToListAsync();
            return new
            {
                world = unread["world"], guild = gid == null ? 0 : unread["guild:" + gid],
                friends = friendMembers.Select(f => new { uid = f.AccountId, f.Nickname, f.online, unread = unread[$"dm:{Math.Min(mid, f.Id)}:{Math.Max(mid, f.Id)}"] }),
            };
        });

        g.MapPost("/chat/{channel}", async (string channel, ChatSendRequest req, HttpContext ctx, PetStarDb db, ContentGuard guard, RealtimeHub hub) =>
        {
            var (sid, mid) = ctx.User.Player();
            await guard.EnsureAllowed(ctx.User.AccountId(), "social");
            var (key, recipients) = await ResolveChannel(db, channel, sid, mid);
            var text = ContentGuard.Clean(req.Text, 300, true);
            // nobody may impersonate system messages or forge internal post links
            if (text.StartsWith("[system", StringComparison.OrdinalIgnoreCase) || text.StartsWith("[post:", StringComparison.OrdinalIgnoreCase)) throw ApiException.BadRequest("text_invalid");
            var now = DateTimeOffset.UtcNow;
            var recent = await db.ChatMessages.AsNoTracking().Where(m => m.AuthorMembershipId == mid && m.CreatedAt > now.AddSeconds(-30)).ToListAsync();
            if (recent.Count >= 8 || recent.Any(m => m.CreatedAt > now.AddSeconds(-1))) throw new ApiException(429, "chat_too_fast");
            if (recent.Any(m => m.Text == text && m.Channel == key)) throw ApiException.Conflict("duplicate_message");
            var flags = ContentGuard.Assess(text);
            var msg = new ChatMessage { ServerId = sid, Channel = key, AuthorMembershipId = mid, Text = text, CreatedAt = now, Status = flags.Count > 0 ? "pending" : "visible" };
            db.ChatMessages.Add(msg);
            await db.SaveChangesAsync();
            if (flags.Count > 0) { await SocialAccess.Hold(db, sid, "chat", msg.Id, mid, flags); return Results.Ok(new { msg.Id, status = "pending" }); }
            var b = (await SocialAccess.Badges(db, new[] { mid }))[mid];
            var payload = new { channel = key, id = msg.Id, text, createdAt = now, authorUid = b.Uid, authorNickname = b.Nickname, bubble = b.BubbleId, frame = b.FrameId, guildTag = b.GuildTag };
            if (recipients == null) await hub.ToServer(sid, "chat", payload);
            else await hub.ToMembers(recipients, "chat", payload);
            return Results.Ok(new { msg.Id, status = "visible" });
        });
    }

    private static async Task Dissolve(PetStarDb db, long guildId)
    {
        // guild-only posts disappear with the guild (never re-exposed publicly)
        await db.Posts.Where(p => p.GuildId == guildId).ExecuteUpdateAsync(u => u.SetProperty(p => p.Status, "deleted"));
        await db.GuildMembers.Where(m => m.GuildId == guildId).ExecuteDeleteAsync();
        await db.Guilds.Where(x => x.Id == guildId).ExecuteDeleteAsync();
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
            if (!await SocialAccess.AreFriends(db, mid, other.Id)) throw ApiException.Forbidden("dm_friends_only");
            long a = Math.Min(mid, other.Id), b = Math.Max(mid, other.Id);
            return ($"dm:{a}:{b}", new List<long> { mid, other.Id });
        }
        throw ApiException.BadRequest("invalid_channel");
    }
}
