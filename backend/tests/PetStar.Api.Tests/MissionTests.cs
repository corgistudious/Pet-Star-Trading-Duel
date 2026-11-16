using System.Net;
using System.Text.Json;
using PetStar.Api.Infrastructure;
using PetStar.Contracts;

namespace PetStar.Api.Tests;

[Collection("api")]
public class MissionTests(ApiFactory f)
{
    private static JsonElement Mission(JsonElement root, string period, string id) =>
        root.GetProperty(period).GetProperty("items").EnumerateArray().First(m => m.GetProperty("id").GetString() == id);

    private static async Task<JsonElement> Missions(Player p) => await X.J(await p.Http.GetAsync("/api/missions"));

    [Fact]
    public void Periods_reset_at_utc_day_and_iso_monday()
    {
        var sunday = new DateTimeOffset(2026, 11, 15, 23, 59, 0, TimeSpan.Zero);
        var d = MissionPeriods.Daily(sunday);
        Assert.Equal("d:2026-11-15", d.key);
        Assert.Equal(new DateTimeOffset(2026, 11, 16, 0, 0, 0, TimeSpan.Zero), d.end);
        var w = MissionPeriods.Weekly(sunday);
        Assert.Equal("w:2026-W46", w.key);
        Assert.Equal(new DateTimeOffset(2026, 11, 9, 0, 0, 0, TimeSpan.Zero), w.start);
        Assert.Equal(new DateTimeOffset(2026, 11, 16, 0, 0, 0, TimeSpan.Zero), w.end);
        // 06:00 on Nov 16 in Vietnam (UTC+7) is still Nov 15 in UTC: resets follow UTC, not the device clock
        Assert.Equal("d:2026-11-15", MissionPeriods.Daily(new DateTimeOffset(2026, 11, 16, 6, 0, 0, TimeSpan.FromHours(7))).key);
        Assert.Equal("w:2026-W53", MissionPeriods.Weekly(new DateTimeOffset(2027, 1, 1, 12, 0, 0, TimeSpan.Zero)).key);
    }

    [Fact]
    public async Task Progress_is_derived_and_claim_pays_once_through_the_ledger()
    {
        var p = await f.NewPlayer("mis");
        var m0 = await Missions(p);
        Assert.Equal(0, Mission(m0, "daily", "d_login").GetProperty("progress").GetInt32());
        Assert.Equal("mission_not_completed", await Player.Code(await p.Post("/api/missions/d_login/claim", null)));
        Assert.Equal(HttpStatusCode.NotFound, (await p.Post("/api/missions/nope/claim", null)).StatusCode);

        await Player.Ok(await p.Post("/api/rewards/login/claim", null));
        await p.PostOk<PackOpeningDto>("/api/packs/open", new OpenPackRequest { PackTypeId = "basic_pack" }, Player.Key());
        var m1 = await Missions(p);
        Assert.True(Mission(m1, "daily", "d_login").GetProperty("completed").GetBoolean());
        Assert.Equal(1, Mission(m1, "daily", "d_open_pack").GetProperty("progress").GetInt32());
        Assert.Equal(1, Mission(m1, "weekly", "w_open_packs").GetProperty("progress").GetInt32());
        Assert.Equal(1, Mission(m1, "weekly", "w_login").GetProperty("progress").GetInt32());

        var before = (await p.Me()).Currencies["MINI_GREEN_BEAN"];
        // concurrent double-taps: exactly one claim succeeds
        var res = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => p.Post("/api/missions/d_login/claim", null)));
        Assert.Equal(1, res.Count(r => r.IsSuccessStatusCode));
        Assert.All(res.Where(r => !r.IsSuccessStatusCode), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        Assert.Equal(before + 30, (await p.Me()).Currencies["MINI_GREEN_BEAN"]);
        var period = m1.GetProperty("daily").GetProperty("periodKey").GetString();
        Assert.Equal(1L, await f.Scalar<long>("SELECT count(*) FROM economy_ledger WHERE ref_id = @p0", $"mission:d_login:{period}"));
        Assert.True(Mission(await Missions(p), "daily", "d_login").GetProperty("claimed").GetBoolean());
    }

    [Fact]
    public async Task Surrendered_battles_do_not_count_and_all_dailies_bonus_needs_every_claim()
    {
        var p = await f.NewPlayer("mis2");
        await L2.Deck(p);
        var b = await p.PostOk<BattleDto>("/api/battles/pve", new StartPveRequest { StageId = "c1_s1" }, Player.Key());
        await Player.Ok(await p.Post($"/api/battles/{b.Id}/surrender", null));
        var m = await Missions(p);
        Assert.Equal(0, Mission(m, "daily", "d_play_battles").GetProperty("progress").GetInt32());

        // claims for the other dailies recorded (as if completed); the bonus counts claims, not progress
        Assert.Equal("mission_not_completed", await Player.Code(await p.Post("/api/missions/d_all/claim", null)));
        var period = m.GetProperty("daily").GetProperty("periodKey").GetString()!;
        foreach (var id in new[] { "d_login", "d_play_battles", "d_win_battles" })
            await f.Sql("INSERT INTO mission_claims (membership_id, server_id, mission_id, period_key, at) SELECT id, 1, @p0, @p1, now() FROM server_memberships WHERE account_id = @p2", id, period, p.Uid);
        Assert.Equal(3, Mission(await Missions(p), "daily", "d_all").GetProperty("progress").GetInt32());
        await Player.Ok(await p.Post("/api/missions/d_open_pack/claim", null)); // L2.Deck opened 3 packs today
        var packs = (await p.Me()).Packs.GetValueOrDefault("basic_pack");
        await Player.Ok(await p.Post("/api/missions/d_all/claim", null));
        Assert.Equal(packs + 1, (await p.Me()).Packs["basic_pack"]);
    }

    [Fact]
    public async Task Missions_are_isolated_per_server_membership()
    {
        var p = await f.NewPlayer("mis3");
        await Player.Ok(await p.Post("/api/rewards/login/claim", null));
        await Player.Ok(await p.Post("/api/missions/d_login/claim", null));
        await f.Sql("INSERT INTO game_servers (id, code, name, status, registration_open, created_at) VALUES (77, 'PS-77', 'Mission Test', 'online', true, now()) ON CONFLICT DO NOTHING");
        await p.Enter(77);
        var m = await Missions(p);
        Assert.False(Mission(m, "daily", "d_login").GetProperty("claimed").GetBoolean());
        Assert.Equal(0, Mission(m, "daily", "d_login").GetProperty("progress").GetInt32());
    }
}

public class EmailBodyTests
{
    [Fact]
    public void Emails_link_to_the_web_client_in_both_languages_and_keep_the_raw_code()
    {
        var b = PetStar.Api.Auth.AuthEndpoints.EmailBody("reset", "abc+/=", "https://petstar.pages.dev/");
        Assert.Contains("https://petstar.pages.dev/?reset=abc%2B%2F%3D", b);
        Assert.Contains("đặt lại mật khẩu", b);
        Assert.Contains("reset your Pet Star password", b);
        Assert.EndsWith("Token: abc+/=", b);
        Assert.Contains("https://x.test/?verify=t1", PetStar.Api.Auth.AuthEndpoints.EmailBody("verify", "t1", "https://x.test"));
    }
}

[Collection("api")]
public class FeedGuildTabTests(ApiFactory f)
{
    [Fact]
    public async Task Feed_guild_tab_shows_the_members_guild_posts_only()
    {
        var p = await f.NewPlayer("fgt");
        var none = await X.J(await p.Http.GetAsync("/api/feed?tab=guild"));
        Assert.Equal("not_in_guild", none.GetProperty("note").GetString());
        await Player.Ok(await p.Post("/api/guilds", new PetStar.Api.Features.CreateGuildRequest { Name = "Tab" + Guid.NewGuid().ToString("N")[..6], Description = "", JoinPolicy = "open" }));
        await Player.Ok(await p.Post("/api/guilds/feed", new PetStar.Api.Features.CreatePostRequest { Type = "text", Caption = "guild only " + Guid.NewGuid() }));
        var tab = await X.J(await p.Http.GetAsync("/api/feed?tab=guild"));
        Assert.Equal(1, tab.GetProperty("items").GetArrayLength());
        var pub = await X.J(await p.Http.GetAsync("/api/feed?tab=global"));
        Assert.DoesNotContain(pub.GetProperty("items").EnumerateArray(), x => x.GetProperty("caption").GetString()!.StartsWith("guild only"));
    }
}

[Collection("api")]
public class EventAlbumTests(ApiFactory f)
{
    [Fact]
    public async Task Event_album_lists_pets_offered_by_events_and_counts_discovery()
    {
        var owner = await L2.Admin(f, "owner");
        var p = await f.NewPlayer("evalb");
        string code = "ALB-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        await Player.Ok(await owner.Post("/api/admin/giftcodes", new PetStar.Api.Features.CreateGiftcodeRequest
        {
            Code = code, MaxUses = 5, Rewards = new() { Cards = { new PetStar.Api.Features.RewardCard { FormId = "pet_emberkit", Rarity = "SR", Quality = "A", Finish = "Foil" } } },
        }));
        JsonElement Album(JsonElement r) => r.GetProperty("system").EnumerateArray().First(a => a.GetProperty("id").GetString() == "event");
        var before = Album(await X.J(await p.Http.GetAsync("/api/collections")));
        Assert.True(before.GetProperty("total").GetInt32() >= 1);
        await Player.Ok(await p.Post("/api/giftcodes/redeem", new PetStar.Api.Features.RedeemRequest { Code = code }));
        var after = Album(await X.J(await p.Http.GetAsync("/api/collections")));
        Assert.Equal(before.GetProperty("discovered").GetInt32() + (before.GetProperty("discovered").GetInt32() < before.GetProperty("total").GetInt32() ? 1 : 0),
            after.GetProperty("discovered").GetInt32());
    }
}
