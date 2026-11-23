using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PetStar.Api.Features;
using PetStar.Contracts;

namespace PetStar.Api.Tests;

/// <summary>Social Hub, Guild v2, trades, cosmetics shop, mini games and leaderboards through the real API + PostgreSQL.</summary>
[Collection("api")]
public class SocialHubTests(ApiFactory f)
{
    private static readonly JsonSerializerOptions J = new(JsonSerializerDefaults.Web);
    private static StringContent Body(object o) => new(JsonSerializer.Serialize(o, J), System.Text.Encoding.UTF8, "application/json");
    private static Task<HttpResponseMessage> Put(Player p, string url, object o) => p.Http.PutAsync(url, Body(o));
    private static Task<HttpResponseMessage> Delete(Player p, string url) => p.Http.DeleteAsync(url);
    private static async Task<long> Post(Player p, CreatePostRequest r) => (await X.J(await p.Post("/api/feed/posts", r))).GetProperty("id").GetInt64();
    private Task<long> Owner(long cardId) => f.Scalar<long>("SELECT m.account_id FROM card_instances c JOIN server_memberships m ON m.id = c.owner_membership_id WHERE c.id = @p0", cardId);
    private static string U() => Guid.NewGuid().ToString("N")[..8];

    private async Task<Player> Mod()
    {
        var mod = await f.NewPlayer("mod");
        await f.Sql("INSERT INTO account_roles (account_id, role, granted_at) VALUES (@p0, 'moderator', now())", mod.Uid);
        await mod.Enter(1);
        return mod;
    }

    private async Task<(Player p, List<CardDto> cards)> WithTeam(string prefix, string character = "trainer_boy")
    {
        var p = await f.NewPlayer(prefix);
        var cards = await X.Open(p, 1);
        await Player.Ok(await p.Post("/api/team/lineups", new SaveTeamRequest { Name = "Main", IsDefault = true, CardIds = cards.Take(3).Select(c => c.Id).ToList() }));
        await Player.Ok(await p.Post("/api/trainer", new ChooseTrainerRequest { CharacterId = character }));
        return (p, cards);
    }

    // ------------------------------------------------------------------ Social feed

    [Fact]
    public async Task Post_visibility_is_enforced_for_public_friends_private_and_guild()
    {
        var a = await f.NewPlayer("va"); var friend = await f.NewPlayer("vf"); var stranger = await f.NewPlayer("vs");
        await L2.Friends(a, friend);
        long pub = await Post(a, new CreatePostRequest { Caption = "public " + U(), Visibility = "public" });
        long fr = await Post(a, new CreatePostRequest { Caption = "friends " + U(), Visibility = "friends" });
        long priv = await Post(a, new CreatePostRequest { Caption = "private " + U(), Visibility = "private" });
        Assert.Equal("not_in_guild", await Player.Code(await a.Post("/api/feed/posts", new CreatePostRequest { Caption = "g " + U(), Visibility = "guild" })));

        Assert.Equal(HttpStatusCode.OK, (await stranger.Http.GetAsync($"/api/feed/posts/{pub}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Http.GetAsync($"/api/feed/posts/{fr}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await friend.Http.GetAsync($"/api/feed/posts/{fr}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await friend.Http.GetAsync($"/api/feed/posts/{priv}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await a.Http.GetAsync($"/api/feed/posts/{priv}")).StatusCode);
        // no interaction without view permission
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Post($"/api/feed/posts/{fr}/like", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Post($"/api/feed/posts/{fr}/comments", new CommentRequest { Text = "hi" })).StatusCode);
        // feeds: friends tab shows the friends post to the friend only; public tabs never show non-public posts
        var friendFeed = (await X.GetJ(friend, "/api/feed?tab=friends")).GetProperty("items").EnumerateArray().Select(p => p.GetProperty("id").GetInt64()).ToList();
        Assert.Contains(fr, friendFeed); Assert.DoesNotContain(priv, friendFeed);
        var latest = (await X.GetJ(stranger, "/api/feed?tab=latest&limit=50")).GetProperty("items").EnumerateArray().Select(p => p.GetProperty("id").GetInt64()).ToList();
        Assert.Contains(pub, latest); Assert.DoesNotContain(fr, latest); Assert.DoesNotContain(priv, latest);
        // profile posts tab honours visibility, then profile privacy
        Assert.Single((await X.GetJ(stranger, $"/api/players/{a.Uid}/posts")).GetProperty("items").EnumerateArray());
        await Player.Ok(await Put(a, "/api/social/settings", new SocialSettingsRequest { ProfileVisibility = "private" }));
        Assert.True((await X.GetJ(stranger, $"/api/players/{a.Uid}/posts")).GetProperty("restricted").GetBoolean());
    }

    [Fact]
    public async Task Likes_are_unique_replies_comment_likes_and_notifications_work()
    {
        var a = await f.NewPlayer("la"); var b = await f.NewPlayer("lb"); var c = await f.NewPlayer("lc");
        long pid = await Post(a, new CreatePostRequest { Caption = "like me " + U() });
        var tasks = Enumerable.Range(0, 6).Select(_ => b.Post($"/api/feed/posts/{pid}/like", null));
        foreach (var r in await Task.WhenAll(tasks)) await Player.Ok(r);   // concurrent double taps
        var post = (await X.GetJ(a, $"/api/feed/posts/{pid}")).GetProperty("post");
        Assert.Equal(1, post.GetProperty("likes").GetInt32());
        Assert.Equal(1, await f.Scalar<long>("SELECT count(*) FROM feed_likes WHERE post_id = @p0", pid));
        await Player.Ok(await Delete(b, $"/api/feed/posts/{pid}/like"));
        await Player.Ok(await Delete(b, $"/api/feed/posts/{pid}/like"));
        Assert.Equal(0, (await X.GetJ(a, $"/api/feed/posts/{pid}")).GetProperty("post").GetProperty("likes").GetInt32());
        await Player.Ok(await b.Post($"/api/feed/posts/{pid}/like", null));
        // one notification per liker even after unlike/like again
        Assert.Equal(1, await f.Scalar<long>("SELECT count(*) FROM notifications n JOIN server_memberships m ON m.id = n.membership_id WHERE m.account_id = @p0 AND n.type = 'post_liked'", a.Uid));

        long top = (await X.J(await b.Post($"/api/feed/posts/{pid}/comments", new CommentRequest { Text = "Cute pet!" }))).GetProperty("commentId").GetInt64();
        long reply = (await X.J(await c.Post($"/api/feed/posts/{pid}/comments", new CommentRequest { Text = "Agreed", ParentId = top }))).GetProperty("commentId").GetInt64();
        Assert.Equal("reply_depth_limit", await Player.Code(await a.Post($"/api/feed/posts/{pid}/comments", new CommentRequest { Text = "deeper", ParentId = reply })));
        Assert.Equal(1, await f.Scalar<long>("SELECT count(*) FROM notifications n JOIN server_memberships m ON m.id = n.membership_id WHERE m.account_id = @p0 AND n.type = 'comment_replied'", b.Uid));
        await Player.Ok(await a.Post($"/api/feed/comments/{top}/like", null));
        await Player.Ok(await a.Post($"/api/feed/comments/{top}/like", null));
        var detail = await X.GetJ(a, $"/api/feed/posts/{pid}");
        var tc = detail.GetProperty("comments").EnumerateArray().First(x => x.GetProperty("id").GetInt64() == top);
        Assert.Equal(1, tc.GetProperty("likes").GetInt32());
        Assert.True(tc.GetProperty("likedByMe").GetBoolean());
        Assert.Equal(top, detail.GetProperty("comments").EnumerateArray().First(x => x.GetProperty("id").GetInt64() == reply).GetProperty("parentId").GetInt64());
        Assert.Equal(2, detail.GetProperty("post").GetProperty("comments").GetInt32());
        // own comment delete keeps the counter right
        await Player.Ok(await Delete(c, $"/api/feed/comments/{reply}"));
        Assert.Equal(1, (await X.GetJ(a, $"/api/feed/posts/{pid}")).GetProperty("post").GetProperty("comments").GetInt32());
    }

    [Fact]
    public async Task Shares_keep_the_source_and_private_content_cannot_be_shared()
    {
        var a = await f.NewPlayer("sa"); var b = await f.NewPlayer("sb"); var c = await f.NewPlayer("sc");
        await L2.Friends(a, b); await L2.Friends(b, c);
        long pub = await Post(a, new CreatePostRequest { Caption = "share me " + U() });
        long fr = await Post(a, new CreatePostRequest { Caption = "friends only " + U(), Visibility = "friends" });
        Assert.Equal("post_not_shareable", await Player.Code(await b.Post($"/api/feed/posts/{fr}/share", new ShareRequest { Target = "feed" })));
        var share = await X.J(await b.Post($"/api/feed/posts/{pub}/share", new ShareRequest { Target = "feed", Caption = "look!" }));
        Assert.Equal("share", share.GetProperty("type").GetString());
        Assert.Equal(pub, share.GetProperty("sharedFrom").GetProperty("id").GetInt64());
        Assert.Equal(a.Uid, share.GetProperty("sharedFrom").GetProperty("authorUid").GetInt64());
        await Player.Ok(await b.Post($"/api/feed/posts/{pub}/share", new ShareRequest { Target = "friend", Uid = c.Uid }));
        Assert.Contains((await X.GetJ(c, $"/api/chat/dm:{b.Uid}")).EnumerateArray(), m => m.GetProperty("text").GetString() == $"[post:{pub}]");
        Assert.Equal(2, (await X.GetJ(a, $"/api/feed/posts/{pub}")).GetProperty("post").GetProperty("shares").GetInt32());
        Assert.True(await f.Scalar<long>("SELECT count(*) FROM notifications n JOIN server_memberships m ON m.id = n.membership_id WHERE m.account_id = @p0 AND n.type = 'post_shared'", a.Uid) >= 1);
        // users cannot forge internal links in chat
        Assert.Equal("text_invalid", await Player.Code(await b.Post($"/api/chat/dm:{c.Uid}", new ChatSendRequest { Text = "[post:1]" })));
        // when the source is deleted the share shows it as unavailable
        await Player.Ok(await Delete(a, $"/api/feed/posts/{pub}"));
        var view = (await X.GetJ(c, $"/api/feed/posts/{share.GetProperty("id").GetInt64()}")).GetProperty("post");
        Assert.False(view.GetProperty("sharedFrom").GetProperty("available").GetBoolean());
    }

    [Fact]
    public async Task Showcase_uses_server_data_and_weekly_spotlight_counts_only_valid_likes()
    {
        var (a, cards) = await WithTeam("sh");
        var b = await f.NewPlayer("shb");
        var theirs = (await X.Open(b, 1))[0];
        await f.Sql("UPDATE card_instances SET rarity = 'N' WHERE id = @p0", cards[3].Id);
        await f.Sql("UPDATE card_instances SET rarity = 'SSR' WHERE id = @p0", cards[4].Id);
        Assert.Equal("card_not_rare", await Player.Code(await a.Post("/api/feed/posts", new CreatePostRequest { Type = "showcase", ShowcaseKind = "rare", CardIds = new() { cards[3].Id } })));
        Assert.Equal("card_not_owned", await Player.Code(await a.Post("/api/feed/posts", new CreatePostRequest { Type = "showcase", ShowcaseKind = "rare", CardIds = new() { theirs.Id } })));
        long rare = await Post(a, new CreatePostRequest { Type = "showcase", ShowcaseKind = "rare", CardIds = new() { cards[4].Id }, Caption = "My SSR " + U() });
        long team = await Post(a, new CreatePostRequest { Type = "showcase", ShowcaseKind = "team" });
        var teamPost = (await X.GetJ(b, $"/api/feed/posts/{team}")).GetProperty("post");
        Assert.Equal(3, teamPost.GetProperty("cards").GetArrayLength());
        Assert.Equal("no_companion", await Player.Code(await a.Post("/api/feed/posts", new CreatePostRequest { Type = "showcase", ShowcaseKind = "companion" })));
        Assert.Contains(rare, (await X.GetJ(b, "/api/feed?tab=rare&limit=50")).GetProperty("items").EnumerateArray().Select(p => p.GetProperty("id").GetInt64()));
        Assert.Contains(team, (await X.GetJ(b, "/api/feed?tab=showcase&limit=50")).GetProperty("items").EnumerateArray().Select(p => p.GetProperty("id").GetInt64()));

        await Player.Ok(await a.Post($"/api/feed/posts/{rare}/like", null));   // self like: allowed once, never counted for the spotlight
        var spot = await X.GetJ(a, "/api/feed/featured-week");
        Assert.DoesNotContain(spot.GetProperty("items").EnumerateArray(), x => x.GetProperty("post").GetProperty("id").GetInt64() == rare);
        await Player.Ok(await b.Post($"/api/feed/posts/{rare}/like", null));
        await Player.Ok(await b.Post($"/api/feed/posts/{rare}/comments", new CommentRequest { Text = "Wow " + U() }));
        var item = (await X.GetJ(a, "/api/feed/featured-week")).GetProperty("items").EnumerateArray().First(x => x.GetProperty("post").GetProperty("id").GetInt64() == rare);
        Assert.Equal(5, item.GetProperty("score").GetInt32());   // 1 valid liker x3 + 1 commenter x2
    }

    [Fact]
    public async Task Risky_text_is_held_for_review_and_reports_auto_hide()
    {
        var a = await f.NewPlayer("ma"); var b = await f.NewPlayer("mb");
        var held = await X.J(await a.Post("/api/feed/posts", new CreatePostRequest { Caption = "ban acc gia re, add zalo 0912 345 678 " + U() }));
        long pid = held.GetProperty("id").GetInt64();
        Assert.Equal("pending", held.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await b.Http.GetAsync($"/api/feed/posts/{pid}")).StatusCode);
        Assert.Equal(1, await f.Scalar<long>("SELECT count(*) FROM moderation_queue WHERE target_type = 'post' AND target_id = @p0 AND status = 'pending'", pid));
        // the client cannot approve itself; a moderator can
        Assert.Equal(HttpStatusCode.Forbidden, (await a.Http.GetAsync("/api/admin/social/queue")).StatusCode);
        var mod = await Mod();
        long qid = (await X.GetJ(mod, "/api/admin/social/queue")).EnumerateArray().First(x => x.GetProperty("targetId").GetInt64() == pid).GetProperty("id").GetInt64();
        await Player.Ok(await mod.Post($"/api/admin/social/queue/{qid}/approve", new QueueDecisionRequest { Note = "context ok" }));
        Assert.Equal(HttpStatusCode.OK, (await b.Http.GetAsync($"/api/feed/posts/{pid}")).StatusCode);
        Assert.Equal("already_reviewed", await Player.Code(await mod.Post($"/api/admin/social/queue/{qid}/reject", new QueueDecisionRequest())));

        // risky chat is not broadcast
        var r = await X.J(await a.Post("/api/chat/world", new ChatSendRequest { Text = "chuyen khoan momo nhe " + U() }));
        Assert.Equal("pending", r.GetProperty("status").GetString());
        Assert.DoesNotContain((await X.GetJ(b, "/api/chat/world")).EnumerateArray(), m => m.GetProperty("id").GetInt64() == r.GetProperty("id").GetInt64());

        // three distinct reports hide a post and queue it
        long target = await Post(b, new CreatePostRequest { Caption = "harmless? " + U() });
        for (int i = 0; i < 3; i++)
        {
            var rep = await f.NewPlayer("rep");
            await Player.Ok(await rep.Post("/api/reports", new ReportRequest { TargetType = "post", TargetId = target, Reason = "spam" }));
        }
        Assert.Equal("hidden", await f.Scalar<string>("SELECT status FROM feed_posts WHERE id = @p0", target));
        Assert.Equal(1, await f.Scalar<long>("SELECT count(*) FROM moderation_queue WHERE target_type = 'post' AND target_id = @p0 AND flags = 'report'", target));
    }

    // ------------------------------------------------------------------ Guild

    [Fact]
    public async Task Guild_roles_privacy_invites_missions_and_dissolve()
    {
        var master = await f.NewPlayer("gm"); var m1 = await f.NewPlayer("g1"); var m2 = await f.NewPlayer("g2"); var outsider = await f.NewPlayer("go");
        string tag = "T" + Random.Shared.Next(1000, 9999);
        string name = "Guild " + U();
        long gid = (await X.J(await master.Post("/api/guilds", new CreateGuildRequest { Name = name, Tag = tag, EmblemId = "emblem_moon", BannerId = "banner_aurora", Visibility = "private" }))).GetProperty("id").GetInt64();
        Assert.Equal("guild_tag_taken", await Player.Code(await outsider.Post("/api/guilds", new CreateGuildRequest { Name = "Other " + U(), Tag = tag })));
        Assert.Equal("guild_invite_only", await Player.Code(await m1.Post($"/api/guilds/{gid}/join", null)));
        Assert.DoesNotContain((await X.GetJ(outsider, "/api/guilds?q=" + tag)).EnumerateArray(), x => x.GetProperty("id").GetInt64() == gid);
        await Player.Ok(await master.Post("/api/guilds/invites", new GuildInviteRequest { Uid = m1.Uid }));
        await Player.Ok(await master.Post("/api/guilds/invites", new GuildInviteRequest { Uid = m2.Uid }));
        Assert.Single((await X.GetJ(m1, "/api/guilds/mine")).GetProperty("invites").EnumerateArray());
        Assert.Equal("joined", (await X.J(await m1.Post($"/api/guilds/{gid}/join", null))).GetProperty("status").GetString());
        Assert.Equal("joined", (await X.J(await m2.Post($"/api/guilds/{gid}/join", null))).GetProperty("status").GetString());

        // role ladder: master > vice > officer > elite > member, all checked on the server
        await Player.Ok(await master.Post($"/api/guilds/members/{m1.Uid}/role", new GuildRoleRequest { Role = "vice" }));
        await Player.Ok(await m1.Post($"/api/guilds/members/{m2.Uid}/role", new GuildRoleRequest { Role = "officer" }));
        Assert.Equal(HttpStatusCode.Forbidden, (await m2.Post($"/api/guilds/members/{m1.Uid}/kick", null)).StatusCode);       // officer < vice
        Assert.Equal(HttpStatusCode.Forbidden, (await m2.Post($"/api/guilds/members/{m2.Uid}/role", new GuildRoleRequest { Role = "vice" })).StatusCode);
        await Player.Ok(await m1.Post($"/api/guilds/members/{m2.Uid}/role", new GuildRoleRequest { Role = "elite" }));
        Assert.Equal(HttpStatusCode.Forbidden, (await m2.Post("/api/guilds/invites", new GuildInviteRequest { Uid = outsider.Uid })).StatusCode); // elite cannot invite
        Assert.Equal("cannot_change_leader", await Player.Code(await m1.Post($"/api/guilds/members/{master.Uid}/role", new GuildRoleRequest { Role = "member" })));
        var hall = await X.GetJ(master, "/api/guilds/mine");
        Assert.Equal(tag, hall.GetProperty("guild").GetProperty("tag").GetString());
        Assert.Equal(3, hall.GetProperty("members").GetArrayLength());
        Assert.Contains(hall.GetProperty("logs").EnumerateArray(), l => l.GetProperty("action").GetString() == "role_changed");

        // missions advance from real actions (pack openings) and each member claims once
        await X.Open(m2, 3);
        await X.Open(m1, 2);
        var gd = (await X.GetJ(m2, "/api/guilds/missions")).EnumerateArray().First(x => x.GetProperty("id").GetString() == "gd_packs");
        Assert.True(gd.GetProperty("completed").GetBoolean());
        await Player.Ok(await m2.Post("/api/guilds/missions/gd_packs/claim", null));
        Assert.Equal("already_claimed", await Player.Code(await m2.Post("/api/guilds/missions/gd_packs/claim", null)));
        Assert.Equal("mission_not_completed", await Player.Code(await m2.Post("/api/guilds/missions/gw_pvp_wins/claim", null)));
        Assert.True(await f.Scalar<long>("SELECT exp FROM guilds WHERE id = @p0", gid) >= 80);
        Assert.True(await f.Scalar<long>("SELECT points FROM guild_contributions WHERE guild_id = @p0 AND period_key = 'all' AND membership_id = (SELECT id FROM server_memberships WHERE account_id = @p1 AND server_id = 1)", gid, m2.Uid) > 0);

        // guild-only posts never leak; guild chat is members-only
        await Player.Ok(await m2.Post("/api/guilds/feed", new CreatePostRequest { Caption = "secret plan " + U() }));
        Assert.Single((await X.GetJ(master, "/api/feed?tab=guild")).GetProperty("items").EnumerateArray());
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.Http.GetAsync("/api/chat/guild")).StatusCode);

        // dissolve: typed confirmation, only when the master is alone
        Assert.Equal("confirm_name_mismatch", await Player.Code(await master.Post("/api/guilds/dissolve", new GuildDissolveRequest { Confirm = "nope" })));
        Assert.Equal("guild_has_members", await Player.Code(await master.Post("/api/guilds/dissolve", new GuildDissolveRequest { Confirm = name })));
        await Player.Ok(await m1.Post("/api/guilds/leave", null));
        await Player.Ok(await m2.Post("/api/guilds/leave", null));
        await Player.Ok(await master.Post("/api/guilds/dissolve", new GuildDissolveRequest { Confirm = name }));
        Assert.Equal(0, await f.Scalar<long>("SELECT count(*) FROM guilds WHERE id = @p0", gid));
        Assert.Equal(0, await f.Scalar<long>("SELECT count(*) FROM feed_posts WHERE guild_id = @p0 AND status <> 'deleted'", gid));
    }

    [Fact]
    public async Task Guild_boss_damage_is_simulated_on_the_server_and_war_pairs_guilds()
    {
        var (a, _) = await WithTeam("gba");
        var (b, _) = await WithTeam("gbb");
        long g1 = (await X.J(await a.Post("/api/guilds", new CreateGuildRequest { Name = "Boss " + U() }))).GetProperty("id").GetInt64();
        long g2 = (await X.J(await b.Post("/api/guilds", new CreateGuildRequest { Name = "Rival " + U() }))).GetProperty("id").GetInt64();
        var boss = (await X.GetJ(a, "/api/guilds/boss")).GetProperty("boss");
        long hp0 = boss.GetProperty("hp").GetInt64();
        long total = 0;
        for (int i = 0; i < 3; i++)
        {
            var hit = await X.J(await a.Post("/api/guilds/boss/attack", null));
            Assert.True(hit.GetProperty("damage").GetInt64() > 0, hit.ToString());
            total += hit.GetProperty("damage").GetInt64();
        }
        Assert.Equal("boss_attacks_used", await Player.Code(await a.Post("/api/guilds/boss/attack", null)));
        var after = await X.GetJ(a, "/api/guilds/boss");
        Assert.Equal(hp0 - total, after.GetProperty("boss").GetProperty("hp").GetInt64());
        Assert.Equal(total, after.GetProperty("board")[0].GetProperty("damage").GetInt64());
        Assert.Equal(total, await f.Scalar<long>("SELECT sum(damage) FROM guild_boss_attacks a JOIN guild_bosses b ON b.id = a.boss_id WHERE b.guild_id = @p0", g1));

        // leaderboards pick it up from the server records
        var lb = await X.GetJ(a, "/api/leaderboards/boss_damage?period=week");
        Assert.Contains(lb.GetProperty("rows").EnumerateArray(), r => r.GetProperty("uid").GetInt64() == a.Uid && r.GetProperty("score").GetInt64() == total);
        Assert.Equal(a.Uid, lb.GetProperty("me").GetProperty("uid").GetInt64());
        var glb = await X.GetJ(a, "/api/leaderboards/guild_boss?period=week");
        Assert.Contains(glb.GetProperty("rows").EnumerateArray(), r => r.GetProperty("subjectId").GetInt64() == g1);
        Assert.Equal(HttpStatusCode.NotFound, (await a.Http.GetAsync("/api/leaderboards/made_up")).StatusCode);

        // war: both register, are paired, a war battle starts on the 3v3 server and scores for the attacker's guild
        await Player.Ok(await a.Post("/api/guilds/war/register", null));
        Assert.Equal("already_registered", await Player.Code(await a.Post("/api/guilds/war/register", null)));
        await Player.Ok(await b.Post("/api/guilds/war/register", null));
        var war = await X.GetJ(a, "/api/guilds/war");
        Assert.Equal(g2, war.GetProperty("opponent").GetProperty("id").GetInt64());
        var match = await X.J(await a.Post("/api/guilds/war/battle", null));
        Assert.Equal("guildwar", match.GetProperty("mode").GetString());
        var mid = match.GetProperty("id").GetGuid();
        await Player.Ok(await a.Post($"/api/team/matches/{mid}/actions", new TeamActionRequest { ActionId = Guid.NewGuid().ToString("N"), Version = match.GetProperty("version").GetInt32(), Action = new PetStar.Rules.Team.TeamAction { Type = "surrender" } }));
        var entry = (await X.GetJ(a, "/api/guilds/war")).GetProperty("entry");
        Assert.Equal(1, entry.GetProperty("battles").GetInt32());
        Assert.Equal(0, entry.GetProperty("points").GetInt32());
    }

    // ------------------------------------------------------------------ Market trades

    [Fact]
    public async Task Card_trade_swaps_atomically_and_fails_safely_when_cards_change()
    {
        var a = await f.NewPlayer("ta"); var b = await f.NewPlayer("tb");
        var ca = await X.Open(a, 1); var cb = await X.Open(b, 1);
        Assert.Equal("trade_card_not_owned", await Player.Code(await a.Post("/api/market/trades", new TradeOfferRequest { Uid = b.Uid, OfferCardIds = new() { cb[0].Id }, RequestCardIds = new() { ca[0].Id } })));
        long t1 = (await X.J(await a.Post("/api/market/trades", new TradeOfferRequest { Uid = b.Uid, OfferCardIds = new() { ca[0].Id }, RequestCardIds = new() { cb[0].Id, cb[1].Id } }))).GetProperty("tradeId").GetInt64();
        Assert.Single((await X.GetJ(b, "/api/market/trades?box=incoming")).EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await a.Post($"/api/market/trades/{t1}/accept", null)).StatusCode);    // only the receiver accepts
        var res = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => b.Post($"/api/market/trades/{t1}/accept", null)));
        Assert.Equal(1, res.Count(r => r.IsSuccessStatusCode));
        Assert.Equal(b.Uid, await Owner(ca[0].Id));
        Assert.Equal(2, await f.Scalar<long>("SELECT count(*) FROM card_instances WHERE id = ANY(@p0) AND owner_membership_id = (SELECT id FROM server_memberships WHERE account_id = @p1 AND server_id = 1)", new[] { cb[0].Id, cb[1].Id }, a.Uid));
        Assert.Equal(3, await f.Scalar<long>("SELECT count(*) FROM card_ownership_history WHERE ref_id = @p0", "trade:" + t1));

        // a card listed after the offer makes the accept fail and nothing moves
        long t2 = (await X.J(await a.Post("/api/market/trades", new TradeOfferRequest { Uid = b.Uid, OfferCardIds = new() { ca[1].Id }, RequestCardIds = new() { cb[2].Id } }))).GetProperty("tradeId").GetInt64();
        await Player.Ok(await a.Post("/api/market/listings", new CreateListingRequest { CardId = ca[1].Id, Currency = X.Beans, Price = 50, ConfirmPriceWarning = true }, Player.Key()));
        Assert.Equal("trade_cards_changed", await Player.Code(await b.Post($"/api/market/trades/{t2}/accept", null)));
        Assert.Equal("failed", await f.Scalar<string>("SELECT status FROM trade_offers WHERE id = @p0", t2));
        Assert.Equal(b.Uid, await Owner(cb[2].Id));
        // listed cards cannot be offered, and the market only takes beans
        Assert.Equal("card_busy", await Player.Code(await a.Post("/api/market/trades", new TradeOfferRequest { Uid = b.Uid, OfferCardIds = new() { ca[1].Id }, RequestCardIds = new() { cb[3].Id } })));
        Assert.Equal("market_currency_beans_only", await Player.Code(await a.Post("/api/market/listings", new CreateListingRequest { CardId = ca[2].Id, Currency = X.Stars, Price = 5, ConfirmPriceWarning = true }, Player.Key())));
        long t3 = (await X.J(await a.Post("/api/market/trades", new TradeOfferRequest { Uid = b.Uid, OfferCardIds = new() { ca[2].Id }, RequestCardIds = new() { cb[3].Id } }))).GetProperty("tradeId").GetInt64();
        await Player.Ok(await a.Post($"/api/market/trades/{t3}/cancel", null));
        Assert.Equal("trade_closed", await Player.Code(await b.Post($"/api/market/trades/{t3}/accept", null)));
    }

    // ------------------------------------------------------------------ Cosmetics / Shop

    [Fact]
    public async Task Shop_purchases_are_server_priced_idempotent_and_equipable()
    {
        var owner = await X.Owner(f);
        var (p, _) = await WithTeam("cs", "trainer_girl");
        var cat = await X.GetJ(p, "/api/shop/catalog");
        Assert.False(cat.GetProperty("realMoney").GetBoolean());
        Assert.Contains(cat.GetProperty("items").EnumerateArray(), i => i.GetProperty("id").GetString() == "skin_girl_blossom" && i.GetProperty("purchasable").GetBoolean());
        Assert.Contains(cat.GetProperty("items").EnumerateArray(), i => i.GetProperty("id").GetString() == "skin_boy_starlight" && !i.GetProperty("purchasable").GetBoolean());

        var key = Player.Key();
        await Player.Ok(await p.Post("/api/shop/buy", new ShopBuyRequest { ItemId = "frame_leaf" }, key));
        var replay = await X.J(await p.Post("/api/shop/buy", new ShopBuyRequest { ItemId = "frame_leaf" }, key));
        Assert.True(replay.GetProperty("replay").GetBoolean());
        Assert.Equal(1, await f.Scalar<long>("SELECT count(*) FROM economy_ledger l JOIN shop_purchases s ON l.ref_id = 'shop:' || s.id WHERE s.idempotency_key = @p0", key));
        Assert.Equal("already_owned", await Player.Code(await p.Post("/api/shop/buy", new ShopBuyRequest { ItemId = "frame_leaf" }, Player.Key())));
        Assert.Equal("item_not_for_sale", await Player.Code(await p.Post("/api/shop/buy", new ShopBuyRequest { ItemId = "frame_lantern_event" }, Player.Key())));
        Assert.Equal("idempotency_key_required", await Player.Code(await p.Post("/api/shop/buy", new ShopBuyRequest { ItemId = "bubble_cloud" })));
        Assert.Equal("outfit_wrong_character", await Player.Code(await p.Post("/api/shop/buy", new ShopBuyRequest { ItemId = "skin_boy_starlight" }, Player.Key())));
        Assert.Equal("insufficient_mini_green_bean", await Player.Code(await p.Post("/api/shop/buy", new ShopBuyRequest { ItemId = "skin_girl_blossom" }, Player.Key())));
        Assert.Equal("requires_guild", await Player.Code(await p.Post("/api/shop/buy", new ShopBuyRequest { ItemId = "bubble_guild_banner" }, Player.Key())));

        Assert.Equal("item_not_owned", await Player.Code(await Put(p, "/api/cosmetics/equip", new EquipRequest { Slot = "bubble", ItemId = "bubble_starlight" })));
        Assert.Equal("wrong_slot", await Player.Code(await Put(p, "/api/cosmetics/equip", new EquipRequest { Slot = "bubble", ItemId = "frame_leaf" })));
        await Player.Ok(await Put(p, "/api/cosmetics/equip", new EquipRequest { Slot = "frame", ItemId = "frame_leaf" }));
        await X.Fund(owner, p, X.Beans, 3000);
        await Player.Ok(await p.Post("/api/shop/buy", new ShopBuyRequest { ItemId = "skin_girl_blossom" }, Player.Key()));
        await Player.Ok(await Put(p, "/api/trainer/outfit", new { outfitId = "skin_girl_blossom" }));
        await Player.Ok(await p.Post("/api/shop/buy", new ShopBuyRequest { ItemId = "bundle_pet_shards", Quantity = 2 }, Player.Key()));
        Assert.Equal("daily_limit_reached", await Player.Code(await p.Post("/api/shop/buy", new ShopBuyRequest { ItemId = "bundle_pet_shards", Quantity = 4 }, Player.Key())));

        // equipped cosmetics follow the player everywhere (feed, chat) and survive re-login
        await p.Enter(1);
        var cos = await X.GetJ(p, "/api/cosmetics");
        Assert.Equal("frame_leaf", cos.GetProperty("equipped").GetProperty("frame").GetString());
        Assert.Equal("skin_girl_blossom", (await X.GetJ(p, "/api/trainer")).GetProperty("outfitId").GetString());
        var post = await X.J(await p.Post("/api/feed/posts", new CreatePostRequest { Caption = "new look " + U() }));
        Assert.Equal("frame_leaf", post.GetProperty("author").GetProperty("frameId").GetString());
        await Player.Ok(await Put(p, "/api/cosmetics/equip", new EquipRequest { Slot = "frame", ItemId = null }));
        Assert.Equal("frame_default", (await X.GetJ(p, "/api/cosmetics")).GetProperty("equipped").GetProperty("frame").GetString());
    }

    // ------------------------------------------------------------------ Mini games

    [Fact]
    public async Task Mini_game_scores_are_computed_by_the_server_with_reward_limits()
    {
        var p = await f.NewPlayer("mg");
        var data = f.Services.GetRequiredService<GameData>();

        // memory: solve from the board; a score field from the client would be ignored (none is accepted)
        var start = await X.J(await p.Post("/api/minigames/memory/start", null));
        var sid = start.GetProperty("sessionId").GetGuid();
        var board = start.GetProperty("payload").GetProperty("board").EnumerateArray().Select(x => x.GetString()!).ToList();
        var moves = board.Select((f, i) => (f, i)).GroupBy(x => x.f).SelectMany(g => g.Select(x => x.i)).ToList();
        Assert.Equal("too_fast", await Player.Code(await p.Post($"/api/minigames/sessions/{sid}/finish", new MiniGameFinishRequest { Moves = moves })));
        start = await X.J(await p.Post("/api/minigames/memory/start", null));
        sid = start.GetProperty("sessionId").GetGuid();
        board = start.GetProperty("payload").GetProperty("board").EnumerateArray().Select(x => x.GetString()!).ToList();
        moves = board.Select((f, i) => (f, i)).GroupBy(x => x.f).SelectMany(g => g.Select(x => x.i)).ToList();
        await f.Sql("UPDATE minigame_sessions SET started_at = now() - interval '20 seconds' WHERE id = @p0", sid);
        var done = await X.J(await p.Post($"/api/minigames/sessions/{sid}/finish", new MiniGameFinishRequest { Moves = moves }));
        Assert.True(done.GetProperty("score").GetInt32() >= 900);
        Assert.True(done.GetProperty("rewarded").GetBoolean());
        var again = await X.J(await p.Post($"/api/minigames/sessions/{sid}/finish", new MiniGameFinishRequest { Moves = moves }));
        Assert.True(again.GetProperty("replay").GetBoolean());
        Assert.Equal(1, await f.Scalar<long>("SELECT count(*) FROM economy_ledger WHERE ref_id = @p0", "mg:" + sid));

        // incomplete board is rejected
        var s2 = await X.J(await p.Post("/api/minigames/memory/start", null));
        await f.Sql("UPDATE minigame_sessions SET started_at = now() - interval '20 seconds' WHERE id = @p0", s2.GetProperty("sessionId").GetGuid());
        Assert.Equal("game_not_completed", await Player.Code(await p.Post($"/api/minigames/sessions/{s2.GetProperty("sessionId").GetGuid()}/finish", new MiniGameFinishRequest { Moves = new() { 0, 1 } })));

        // sprint: auto-clickers are rejected, human-like taps score
        var sp = (await X.J(await p.Post("/api/minigames/sprint/start", null))).GetProperty("sessionId").GetGuid();
        await f.Sql("UPDATE minigame_sessions SET started_at = now() - interval '21 seconds' WHERE id = @p0", sp);
        Assert.Equal("autoclick_detected", await Player.Code(await p.Post($"/api/minigames/sessions/{sp}/finish", new MiniGameFinishRequest { Taps = Enumerable.Range(0, 300).Select(i => i * 60).ToList() })));
        var sp2 = (await X.J(await p.Post("/api/minigames/sprint/start", null))).GetProperty("sessionId").GetGuid();
        await f.Sql("UPDATE minigame_sessions SET started_at = now() - interval '21 seconds' WHERE id = @p0", sp2);
        var rnd = new Random(7); int t = 0; var taps = new List<int>();
        while (true) { t += 110 + rnd.Next(80); if (t > 19_900) break; taps.Add(t); }
        var spr = await X.J(await p.Post($"/api/minigames/sessions/{sp2}/finish", new MiniGameFinishRequest { Taps = taps }));
        Assert.Equal(taps.Count * 10, spr.GetProperty("score").GetInt32());

        // quiz: answers are graded on the server (seed-derived questions, answers never sent to the client)
        var q = await X.J(await p.Post("/api/minigames/quiz/start", null));
        var qid = q.GetProperty("sessionId").GetGuid();
        Assert.False(q.GetProperty("payload").GetProperty("questions")[0].TryGetProperty("answer", out _));
        long seed = await f.Scalar<long>("SELECT seed FROM minigame_sessions WHERE id = @p0", qid);
        var answers = MiniGameEndpoints.Quiz(seed, data).Select(x => x.Answer).ToList();
        await f.Sql("UPDATE minigame_sessions SET started_at = now() - interval '10 seconds' WHERE id = @p0", qid);
        var quiz = await X.J(await p.Post($"/api/minigames/sessions/{qid}/finish", new MiniGameFinishRequest { Answers = answers }));
        Assert.True(quiz.GetProperty("score").GetInt32() >= 1000);
        // 3 rewarded plays per day (memory + sprint + quiz); the next one scores but pays nothing
        var p4 = await X.J(await p.Post("/api/minigames/quiz/start", null));
        var p4id = p4.GetProperty("sessionId").GetGuid();
        await f.Sql("UPDATE minigame_sessions SET started_at = now() - interval '10 seconds' WHERE id = @p0", p4id);
        var last = await X.J(await p.Post($"/api/minigames/sessions/{p4id}/finish", new MiniGameFinishRequest { Answers = MiniGameEndpoints.Quiz(await f.Scalar<long>("SELECT seed FROM minigame_sessions WHERE id = @p0", p4id), data).Select(x => x.Answer).ToList() }));
        Assert.False(last.GetProperty("rewarded").GetBoolean());
        var summary = await X.GetJ(p, "/api/minigames");
        Assert.Equal(3, summary.GetProperty("rewardedToday").GetInt32());
        Assert.Contains((await X.GetJ(p, "/api/leaderboards/minigames?period=week")).GetProperty("rows").EnumerateArray(), r => r.GetProperty("uid").GetInt64() == p.Uid);
    }

    [Fact]
    public async Task Chat_unread_counts_and_bubbles_follow_the_sender()
    {
        var a = await f.NewPlayer("ca"); var b = await f.NewPlayer("cb");
        await L2.Friends(a, b);
        await Player.Ok(await a.Post($"/api/chat/dm:{b.Uid}", new ChatSendRequest { Text = "hello friend " + U() }));
        var unread = await X.GetJ(b, "/api/chat-unread");
        Assert.Equal(1, unread.GetProperty("friends").EnumerateArray().First(x => x.GetProperty("uid").GetInt64() == a.Uid).GetProperty("unread").GetInt32());
        var msgs = await X.GetJ(b, $"/api/chat/dm:{a.Uid}");
        Assert.Equal("bubble_default", msgs[msgs.GetArrayLength() - 1].GetProperty("bubble").GetString());
        Assert.Equal(0, (await X.GetJ(b, "/api/chat-unread")).GetProperty("friends").EnumerateArray().First(x => x.GetProperty("uid").GetInt64() == a.Uid).GetProperty("unread").GetInt32());
        Assert.Equal("text_invalid", await Player.Code(await a.Post("/api/chat/world", new ChatSendRequest { Text = "[SYSTEM] free stars" })));
    }
}
