using System.Net;
using System.Text.Json;
using PetStar.Api.Features;
using PetStar.Contracts;
using PetStar.Rules;

namespace PetStar.Api.Tests;

/// <summary>Card system expansion against the real API + PostgreSQL: 9 packs, purchase conditions (level, VIP, limited
/// events, Star invitations), Divine Pets, progression (stars, enhancement, breakthrough, awakening, skill, engravings,
/// reset), market transfer of upgraded cards, battle integration, admin RBAC and server separation.</summary>
[Collection("api")]
public class CardSystemTests(ApiFactory f)
{
    private static async Task<Player> Staff(ApiFactory f, string role)
    {
        var p = await f.NewPlayer(role);
        await f.Sql("INSERT INTO account_roles (account_id, role, granted_at) VALUES (@p0, @p1, now())", p.Uid, role);
        await p.Enter(1);
        return p;
    }

    private async Task<long> Mid(Player p, int server = 1) =>
        await f.Scalar<long>("SELECT id FROM server_memberships WHERE account_id=@p0 AND server_id=@p1", p.Uid, server);

    private Task SetLevel(Player p, int level, int server = 1) =>
        f.Sql("UPDATE server_memberships SET level=@p0 WHERE account_id=@p1 AND server_id=@p2", level, p.Uid, server);

    private static Task Grant(Player owner, Player p, string item, long amount, int server = 1) =>
        Player.Ok(owner.Post("/api/admin/cardsys/materials/grant", new AdminGrantRequest { Uid = p.Uid, ServerId = server, ItemId = item, Amount = amount, Reason = "integration test materials" }).Result);

    private static Task<HttpResponseMessage> Buy(Player p, string pack, int qty = 1, string? key = null) =>
        p.Post("/api/packs/purchase", new PurchasePackRequest { PackTypeId = pack, Quantity = qty }, key ?? Player.Key());

    private static Task<HttpResponseMessage> Up(Player p, long cardId, string kind, int from, string? key = null) =>
        p.Post($"/api/cards/{cardId}/{kind}", new UpgradeRequest { From = from }, key ?? Player.Key());

    // ------------------------------------------------------------------ packs

    [Fact]
    public async Task Shop_lists_nine_packs_with_server_evaluated_conditions_and_public_odds()
    {
        var p = await f.NewPlayer();
        var shop = await p.Get<List<PackShopItemDto>>("/api/packs");
        var ids = shop.Select(x => x.Id).ToList();
        Assert.Equal(new[] { "basic_pack", "premium_pack", "transcendent_pack", "epic_pack", "legendary_pack", "mythic_pack", "divine_premium_pack", "divine_limited_pack", "divine_star_pack" },
            ids);
        Assert.Equal("pack_level_too_low", shop.Single(x => x.Id == "divine_limited_pack").BlockedReason); // level is checked first, then VIP, then the event
        Assert.True(shop.Single(x => x.Id == "basic_pack").CanBuy);
        Assert.Equal("pack_level_too_low", shop.Single(x => x.Id == "divine_premium_pack").BlockedReason);
        Assert.Equal("Gói Tiêu Chuẩn", shop.Single(x => x.Id == "basic_pack").Names["vi"]);
        Assert.Equal("Standard Pack", shop.Single(x => x.Id == "basic_pack").Names["en"]);

        var odds = await p.Get<List<PackOddsDto>>("/api/packs/odds");
        Assert.Equal(9, odds.Count);
        var lim = await p.Get<PackOddsDto>("/api/packs/divine_limited_pack/odds");
        Assert.Equal(0.15, lim.DivineGroupPerSlot, 6);
        Assert.Equal(0.03, lim.DivinePerPetPerSlot, 6);
        Assert.Equal(5, lim.DivinePets.Count);
        foreach (var o in odds.Append(lim))
        {
            Assert.True(o.SameTableAllSlots);
            Assert.Equal(100.0, o.Slots1To4.Values.Sum(), 6);
            Assert.All(o.Pack, r => Assert.InRange(r.AtLeastOneInPack, r.PerSlot, 100));
        }
        var star = odds.Single(o => o.PackTypeId == "divine_star_pack");
        Assert.Equal(0.02, star.DivinePerPetPerSlot, 6);
        Assert.Equal(Math.Round((1 - Math.Pow(1 - 0.001, 5)) * 100, 6), star.DivineAtLeastOneInPack, 6);
    }

    [Fact]
    public async Task Premium_pack_needs_level_30_and_VIP_3_earned_only_from_system_star_spending()
    {
        var owner = await X.Owner(f);
        var p = await f.NewPlayer();
        await X.Fund(owner, p, X.Stars, 2000);
        Assert.Equal("pack_level_too_low", await Player.Code(await Buy(p, "divine_premium_pack")));
        await SetLevel(p, 30);
        Assert.Equal("pack_vip_too_low", await Player.Code(await Buy(p, "divine_premium_pack")));
        // 40 Epic packs × 20 stars = 800 points → VIP 3
        for (int i = 0; i < 4; i++) await Player.Ok(await Buy(p, "epic_pack", 10));
        var vip = await p.Get<VipDto>("/api/vip");
        Assert.Equal(800, vip.Points);
        Assert.Equal(3, vip.Level);
        await Player.Ok(await Buy(p, "divine_premium_pack"));
        Assert.Equal(2000 - 800 - 200, X.Bal(await p.Me(), X.Stars));
        var hist = await X.GetJ(p, "/api/packs/purchases?packTypeId=divine_premium_pack");
        Assert.Equal(1, hist.GetArrayLength());
        Assert.Equal(200, hist[0].GetProperty("amount").GetInt64());
        // admin funding did not give VIP points; only purchases did
        Assert.Equal(1000, (await p.Get<VipDto>("/api/vip")).Points);
    }

    [Fact]
    public async Task Limited_pack_is_sold_only_inside_an_event_with_player_and_total_limits()
    {
        var owner = await X.Owner(f);
        var admin = await Staff(f, "admin");
        var a = await f.NewPlayer("la"); var b = await f.NewPlayer("lb");
        foreach (var p in new[] { a, b })
        {
            await X.Fund(owner, p, X.Stars, 5000);
            await SetLevel(p, 40);
            await Player.Ok(await owner.Post("/api/admin/cardsys/vip/grant", new AdminGrantRequest { Uid = p.Uid, ServerId = 1, Amount = 1500, Reason = "integration test vip level" }));
        }
        Assert.Equal("pack_event_not_active", await Player.Code(await Buy(a, "divine_limited_pack")));
        var ev = await X.J(await admin.Post("/api/admin/cardsys/events", new AdminEventRequest
        {
            PackTypeId = "divine_limited_pack", ServerId = 1, NameVi = "Lễ hội đèn lồng", NameEn = "Lantern Festival",
            StartsAt = DateTimeOffset.UtcNow.AddMinutes(-1), EndsAt = DateTimeOffset.UtcNow.AddDays(1), TotalLimit = 3, PerPlayerLimit = 2,
        }));
        long evId = ev.GetProperty("id").GetInt64();
        await Player.Ok(await Buy(a, "divine_limited_pack", 2));
        Assert.Equal("pack_event_player_limit", await Player.Code(await Buy(a, "divine_limited_pack")));
        await Player.Ok(await Buy(b, "divine_limited_pack"));
        Assert.Equal("pack_event_sold_out", await Player.Code(await Buy(b, "divine_limited_pack")));
        var shop = await a.Get<List<PackShopItemDto>>("/api/packs");
        var item = shop.Single(x => x.Id == "divine_limited_pack");
        Assert.Equal(2, item.Event!.BoughtByMe); Assert.Equal(3, item.Event.Sold);
        // ending the event stops sales but owned packs still open and cards are never taken back
        await Player.Ok(await admin.Http.PatchAsJsonAsync($"/api/admin/cardsys/events/{evId}", new AdminEventPatch { Cancelled = true, Reason = "integration test end of event" }));
        Assert.Equal("pack_event_not_active", await Player.Code(await Buy(a, "divine_limited_pack")));
        var opened = await a.PostOk<PackOpeningDto>("/api/packs/open", new OpenPackRequest { PackTypeId = "divine_limited_pack" }, Player.Key());
        Assert.Equal(5, opened.Cards.Count);
        Assert.All(opened.Cards, c => Assert.True(c.DivineSet == null || c.DivineSet == "limited"));
        Assert.Equal(5, (await a.Get<CardPageDto>("/api/cards?pageSize=200")).Items.Count(c => opened.Cards.Any(o => o.Id == c.Id)));
    }

    [Fact]
    public async Task Star_pack_requires_a_valid_invitation_checked_at_purchase_time()
    {
        var owner = await X.Owner(f);
        var admin = await Staff(f, "admin");
        var gm = await Staff(f, "gm");
        var p = await f.NewPlayer();
        await X.Fund(owner, p, X.Stars, 5000);
        await SetLevel(p, 60);
        await Player.Ok(await owner.Post("/api/admin/cardsys/vip/grant", new AdminGrantRequest { Uid = p.Uid, ServerId = 1, Amount = 10000, Reason = "integration test vip level" }));
        Assert.Equal("pack_invitation_required", await Player.Code(await Buy(p, "divine_star_pack")));
        // a GM may look but not issue
        Assert.Equal(HttpStatusCode.Forbidden, (await gm.Post("/api/admin/cardsys/invitations", new AdminInviteRequest { Uid = p.Uid, ServerId = 1 })).StatusCode);
        var inv = await X.J(await admin.Post("/api/admin/cardsys/invitations", new AdminInviteRequest { Uid = p.Uid, ServerId = 1, Days = 7, MaxPacks = 2, Note = "test" }));
        Guid invId = inv.GetProperty("id").GetGuid();
        Assert.Equal("pack_invitation_exhausted", await Player.Code(await Buy(p, "divine_star_pack", 3)));
        await Player.Ok(await Buy(p, "divine_star_pack"));
        var shop = (await p.Get<List<PackShopItemDto>>("/api/packs")).Single(x => x.Id == "divine_star_pack");
        Assert.Equal(1, shop.InvitationPacksLeft);
        // expired (server time) → refused
        await f.Sql("UPDATE star_invitations SET expires_at = now() - interval '1 minute' WHERE id = @p0", invId);
        Assert.Equal("pack_invitation_required", await Player.Code(await Buy(p, "divine_star_pack")));
        await f.Sql("UPDATE star_invitations SET expires_at = now() + interval '1 day' WHERE id = @p0", invId);
        await Player.Ok(await admin.Post($"/api/admin/cardsys/invitations/{invId}/revoke", new AdminRevokeRequest { Reason = "integration test revoke" }));
        Assert.Equal("pack_invitation_required", await Player.Code(await Buy(p, "divine_star_pack")));
        var list = await X.GetJ(gm, $"/api/admin/cardsys/invitations?serverId=1&uid={p.Uid}");
        var row = list.EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == invId);
        Assert.Equal("revoked", row.GetProperty("status").GetString());
        Assert.Equal(1, row.GetProperty("usedPacks").GetInt32());
        Assert.True(await f.Scalar<long>("SELECT count(*) FROM admin_audit_logs WHERE action IN ('cardsys.invitation.issue','cardsys.invitation.revoke') AND target_id IN (@p0, @p1)", (await Mid(p)).ToString(), invId.ToString()) >= 2);
    }

    [Fact]
    public async Task Divine_pets_come_out_at_ten_stars_with_serials_and_the_odds_change_is_versioned()
    {
        var owner = await X.Owner(f);
        var admin = await Staff(f, "admin");
        var p = await f.NewPlayer();
        // admins cannot change odds; the owner can, only with a valid table, and it creates a new version
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.Http.PutAsJsonAsync("/api/admin/cardsys/packs/divine_premium_pack/odds", new AdminPackOddsRequest { SlotTable = new() { ["DV"] = 1_000_000 }, Reason = "integration test odds" })).StatusCode);
        var bad = await owner.Http.PutAsJsonAsync("/api/admin/cardsys/packs/divine_premium_pack/odds", new AdminPackOddsRequest { SlotTable = new() { ["DV"] = 999_999 }, Reason = "integration test odds" });
        Assert.Equal("invalid_pack_config", await Player.Code(bad));
        int before = await f.Scalar<int>("SELECT version FROM pack_types WHERE id='divine_premium_pack'");
        await Player.Ok(await owner.Http.PutAsJsonAsync("/api/admin/cardsys/packs/divine_premium_pack/odds", new AdminPackOddsRequest { SlotTable = new() { ["DV"] = 1_000_000 }, Reason = "integration test: force divine rolls" }));
        int after = await f.Scalar<int>("SELECT version FROM pack_types WHERE id='divine_premium_pack'");
        Assert.Equal(before + 1, after);
        Assert.Equal(1, await f.Scalar<long>("SELECT count(*) FROM pack_type_versions WHERE pack_type_id='divine_premium_pack' AND version=@p0 AND reason LIKE 'integration test%'", after));

        await f.Sql("INSERT INTO pack_inventory (membership_id, pack_type_id, quantity) VALUES (@p0, 'divine_premium_pack', 1)", await Mid(p));
        var o = await p.PostOk<PackOpeningDto>("/api/packs/open", new OpenPackRequest { PackTypeId = "divine_premium_pack" }, Player.Key());
        Assert.All(o.Cards, c => { Assert.Equal("DV", c.Rarity); Assert.Equal(10, c.Stars); Assert.Equal("premium", c.DivineSet); Assert.StartsWith("pet_dv_", c.FormId); });
        Assert.Equal(5, o.Cards.Select(c => c.Serial).Distinct().Count());
        Assert.Equal(after, await f.Scalar<int>("SELECT pack_config_version FROM pack_openings WHERE id=@p0", o.OpeningId));
        var pass = await p.Get<PassportDto>($"/api/cards/{o.Cards[0].Id}/passport");
        Assert.Equal("DV", pass.Card.Rarity);
        Assert.All(pass.Values, v => Assert.True(v.Initial > 0));
        // a divine card can still be enhanced (10 stars is not "maxed")
        Assert.Equal("star_max_reached", await Player.Code(await Up(p, o.Cards[0].Id, "star", 10)));
        await Grant(owner, p, Progression.EnhanceStone, 10);
        await X.Fund(owner, p, X.Beans, 1000);
        await Player.Ok(await Up(p, o.Cards[0].Id, "enhance", 0));
        // restore the published odds for other tests
        await Player.Ok(await owner.Http.PutAsJsonAsync("/api/admin/cardsys/packs/divine_premium_pack/odds", new AdminPackOddsRequest
        {
            SlotTable = new() { ["R"] = 50000, ["SR"] = 250000, ["SSR"] = 420000, ["UR"] = 240000, ["EX"] = 38000, ["DV"] = 2000 }, Reason = "integration test: restore odds",
        }));
    }

    [Fact]
    public async Task Concurrent_opens_and_retries_never_duplicate_packs_cards_or_charges()
    {
        var owner = await X.Owner(f);
        var p = await f.NewPlayer();
        var mid = await Mid(p);
        await f.Sql("UPDATE pack_inventory SET quantity = 1 WHERE membership_id=@p0 AND pack_type_id='basic_pack'", mid);
        var tasks = Enumerable.Range(0, 6).Select(_ => p.Post("/api/packs/open", new OpenPackRequest { PackTypeId = "basic_pack" }, Player.Key())).ToList();
        var res = await Task.WhenAll(tasks);
        Assert.Equal(1, res.Count(r => r.IsSuccessStatusCode));
        Assert.Equal(5, await f.Scalar<long>("SELECT count(*) FROM card_instances WHERE owner_membership_id=@p0", mid));
        // purchase retry with the same key: one charge, one pack
        await X.Fund(owner, p, X.Beans, 1000);
        long beans = X.Bal(await p.Me(), X.Beans);
        var key = Player.Key();
        var r1 = await Buy(p, "transcendent_pack", 1, key); var r2 = await Buy(p, "transcendent_pack", 1, key);
        await Player.Ok(r1); await Player.Ok(r2);
        Assert.Equal(beans - 800, X.Bal(await p.Me(), X.Beans));
        Assert.Equal(1, await f.Scalar<int>("SELECT quantity FROM pack_inventory WHERE membership_id=@p0 AND pack_type_id='transcendent_pack'", mid));
    }

    // ------------------------------------------------------------------ progression

    [Fact]
    public async Task Stars_go_one_to_ten_charging_the_exact_table_once_per_step()
    {
        var owner = await X.Owner(f);
        var p = await f.NewPlayer();
        var card = (await X.Open(p))[0];
        await Grant(owner, p, Progression.PetShard, 575);
        await X.Fund(owner, p, X.Beans, 11_550);
        long beans0 = X.Bal(await p.Me(), X.Beans);
        Assert.Equal("stale_state", await Player.Code(await Up(p, card.Id, "star", 2)));
        var key = Player.Key();
        await Player.Ok(await Up(p, card.Id, "star", 1, key));
        await Player.Ok(await Up(p, card.Id, "star", 1, key));      // retry: replayed, not charged again
        Assert.Equal("stale_state", await Player.Code(await Up(p, card.Id, "star", 1))); // double tap with a new key
        var preview = await p.Get<CardUpgradesDto>($"/api/cards/{card.Id}/upgrades");
        var opt = preview.Options.Single(o => o.Kind == "star");
        Assert.Equal(2, opt.Current); Assert.Equal(200, opt.Cost!.Beans); Assert.Equal(10, opt.Cost.Items["pet_shard"]);
        Assert.True(opt.Preview!.Power > preview.Card.Power);
        for (int s = 2; s < 10; s++) await Player.Ok(await Up(p, card.Id, "star", s));
        var c = await p.Get<CardDto>($"/api/cards/{card.Id}");
        Assert.Equal(10, c.Stars);
        Assert.Equal(card.Serial, c.Serial);
        Assert.Equal(0, (await p.Me()).Items.GetValueOrDefault("pet_shard"));
        Assert.Equal(beans0 - 11_550, X.Bal(await p.Me(), X.Beans));
        Assert.Equal("star_max_reached", await Player.Code(await Up(p, card.Id, "star", 10)));
        Assert.Equal(9, await f.Scalar<long>("SELECT count(*) FROM card_progress_events WHERE card_id=@p0 AND type='star_up'", card.Id));
        Assert.Equal("insufficient_items", await Player.Code(await Up(p, (await X.Open(p))[0].Id, "star", 1)));
    }

    [Fact]
    public async Task Enhancement_to_plus_20_with_reset_refund_and_re_enhance()
    {
        var owner = await X.Owner(f);
        var p = await f.NewPlayer();
        var card = (await X.Open(p))[0];
        await Grant(owner, p, Progression.EnhanceStone, 1000);
        await X.Fund(owner, p, X.Beans, 100_000);
        for (int l = 0; l < 5; l++) await Player.Ok(await Up(p, card.Id, "enhance", l));
        Assert.Equal("enhance_level_too_low", await Player.Code(await Up(p, card.Id, "enhance", 5)));
        await f.Sql("UPDATE card_instances SET level = 50 WHERE id=@p0", card.Id);
        for (int l = 5; l < 20; l++) await Player.Ok(await Up(p, card.Id, "enhance", l));
        Assert.Equal("enhance_max_reached", await Player.Code(await Up(p, card.Id, "enhance", 20)));
        var c = await p.Get<CardDto>($"/api/cards/{card.Id}");
        Assert.Equal(20, c.EnhanceLevel);
        var spent = Progression.EnhanceSpent(20);
        Assert.Equal(1000 - spent.Items["enhance_stone"], (await p.Me()).Items["enhance_stone"]);
        var pv = await p.Get<ResetPreviewDto>($"/api/cards/{card.Id}/reset-preview?kind=enhance");
        Assert.Equal(spent.Beans / 2, pv.RefundBeans);
        long beans = X.Bal(await p.Me(), X.Beans);
        await Player.Ok(await p.Post($"/api/cards/{card.Id}/reset", new ResetRequest { Kind = "enhance", From = 20 }, Player.Key()));
        Assert.Equal(beans + spent.Beans / 2, X.Bal(await p.Me(), X.Beans));
        c = await p.Get<CardDto>($"/api/cards/{card.Id}");
        Assert.Equal(0, c.EnhanceLevel); Assert.Equal(1, c.Stars); Assert.Equal(card.Serial, c.Serial);
        Assert.Equal("reset_nothing_to_reset", await Player.Code(await p.Post($"/api/cards/{card.Id}/reset", new ResetRequest { Kind = "enhance", From = 0 }, Player.Key())));
        await Player.Ok(await Up(p, card.Id, "enhance", 0));   // re-enhancing after a reset is a new charge, not a duplicate
    }

    [Fact]
    public async Task Breakthrough_awakening_and_skill_ten_follow_their_gates()
    {
        var owner = await X.Owner(f);
        var p = await f.NewPlayer();
        var card = (await X.Open(p))[0];
        foreach (var (item, n) in new[] { ("breakthrough_core", 100L), ("awakening_crystal", 10L), ("training_scroll", 200L), ("pet_shard", 575L) })
            await Grant(owner, p, item, n);
        await X.Fund(owner, p, X.Beans, 200_000);
        await f.Sql("UPDATE card_instances SET level = 50 WHERE id=@p0", card.Id);
        Assert.Equal("breakthrough_stars_too_low", await Player.Code(await Up(p, card.Id, "breakthrough", 0)));
        for (int s = 1; s < 10; s++) await Player.Ok(await Up(p, card.Id, "star", s));
        await Player.Ok(await Up(p, card.Id, "breakthrough", 0));
        Assert.Equal("breakthrough_level_too_low", await Player.Code(await Up(p, card.Id, "breakthrough", 1)));
        var c = await p.Get<CardDto>($"/api/cards/{card.Id}");
        Assert.Equal(55, c.LevelCap);
        Assert.Equal("awaken_breakthrough_too_low", await Player.Code(await Up(p, card.Id, "awaken", 0)));
        await f.Sql("UPDATE card_instances SET level = 55 WHERE id=@p0", card.Id);
        await Player.Ok(await Up(p, card.Id, "breakthrough", 1));
        Assert.Equal("awaken_level_too_low", await Player.Code(await Up(p, card.Id, "awaken", 0)));
        await f.Sql("UPDATE card_instances SET level = 60 WHERE id=@p0", card.Id);
        await Player.Ok(await Up(p, card.Id, "awaken", 0));
        Assert.Equal("awaken_max_reached", await Player.Code(await Up(p, card.Id, "awaken", 1)));
        for (int i = 0; i < 6; i++) await Player.Ok(await p.Post($"/api/cards/{card.Id}/train", null, Player.Key()));   // 1 → 7 (bt 2)
        Assert.Equal("training_breakthrough_too_low", await Player.Code(await p.Post($"/api/cards/{card.Id}/train", null, Player.Key())));
        await f.Sql("UPDATE card_instances SET level = 70, breakthrough = 4 WHERE id=@p0", card.Id);
        for (int i = 0; i < 3; i++) await Player.Ok(await p.Post($"/api/cards/{card.Id}/train", null, Player.Key()));
        Assert.Equal("skill_max_level", await Player.Code(await p.Post($"/api/cards/{card.Id}/train", null, Player.Key())));
        c = await p.Get<CardDto>($"/api/cards/{card.Id}");
        Assert.True(c.Awakened); Assert.Equal(10, c.SkillLevel); Assert.Equal(70, c.LevelCap);
        // the DB itself refuses impossible values
        var ex = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => f.Sql("UPDATE card_instances SET enhance_level = 21 WHERE id=@p0", card.Id));
        Assert.Equal("23514", ex.SqlState);
    }

    [Fact]
    public async Task Engravings_respect_slots_type_limits_and_are_saved_on_the_server()
    {
        var owner = await X.Owner(f);
        var p = await f.NewPlayer();
        var card = (await X.Open(p))[0];
        await Grant(owner, p, Progression.EngravingDust, 500);
        await X.Fund(owner, p, X.Beans, 50_000);
        var ids = new List<long>();
        foreach (var t in new[] { "atk", "atk", "atk", "crit" })
            ids.Add((await X.J(await p.Post("/api/engravings/craft", new CraftEngravingRequest { Type = t }, Player.Key()))).GetProperty("engraving").GetProperty("id").GetInt64());
        Assert.Equal("engraving_slot_locked", await Player.Code(await p.Post($"/api/cards/{card.Id}/engravings/equip", new EquipEngravingRequest { EngravingId = ids[0], Slot = 1 }, Player.Key())));
        await f.Sql("UPDATE card_instances SET level = 30 WHERE id=@p0", card.Id);
        await Player.Ok(await p.Post($"/api/cards/{card.Id}/engravings/equip", new EquipEngravingRequest { EngravingId = ids[0], Slot = 1 }, Player.Key()));
        await Player.Ok(await p.Post($"/api/cards/{card.Id}/engravings/equip", new EquipEngravingRequest { EngravingId = ids[1], Slot = 2 }, Player.Key()));
        Assert.Equal("engraving_slot_locked", await Player.Code(await p.Post($"/api/cards/{card.Id}/engravings/equip", new EquipEngravingRequest { EngravingId = ids[2], Slot = 3 }, Player.Key())));
        await f.Sql("UPDATE card_instances SET breakthrough = 1 WHERE id=@p0", card.Id);
        Assert.Equal("engraving_type_limit", await Player.Code(await p.Post($"/api/cards/{card.Id}/engravings/equip", new EquipEngravingRequest { EngravingId = ids[2], Slot = 3 }, Player.Key())));
        var before = await p.Get<CardDto>($"/api/cards/{card.Id}");
        await Player.Ok(await p.Post($"/api/cards/{card.Id}/engravings/equip", new EquipEngravingRequest { EngravingId = ids[3], Slot = 3 }, Player.Key()));
        for (int l = 1; l < 5; l++) await Player.Ok(await p.Post($"/api/engravings/{ids[0]}/upgrade", new UpgradeRequest { From = l }, Player.Key()));
        Assert.Equal("engraving_max_level", await Player.Code(await p.Post($"/api/engravings/{ids[0]}/upgrade", new UpgradeRequest { From = 5 }, Player.Key())));
        var c = await p.Get<CardDto>($"/api/cards/{card.Id}");
        Assert.Equal(3, c.Engravings.Count);
        Assert.True(c.Atk > before.Atk && c.Crit > before.Crit);
        Assert.Equal("engraving_in_use", await Player.Code(await p.Post($"/api/cards/{card.Id}/engravings/equip", new EquipEngravingRequest { EngravingId = ids[0], Slot = 1 }, Player.Key())));
        await Player.Ok(await p.Post($"/api/cards/{card.Id}/engravings/unequip", new UnequipEngravingRequest { Slot = 3 }, Player.Key()));
        Assert.Equal(2, (await p.Get<CardDto>($"/api/cards/{card.Id}")).Engravings.Count);
    }

    // ------------------------------------------------------------------ market, battle, separation, RBAC

    [Fact]
    public async Task Upgraded_card_trades_for_rainbow_stars_keeping_everything_and_creating_no_currency()
    {
        var owner = await X.Owner(f);
        var seller = await f.NewPlayer("s"); var buyer = await f.NewPlayer("b");
        var card = (await X.Open(seller))[0];
        await f.Sql("UPDATE card_instances SET level=40, stars=7, enhance_level=12, breakthrough=0, skill_level=4 WHERE id=@p0", card.Id);
        await Grant(owner, seller, Progression.EngravingDust, 10);
        await X.Fund(owner, seller, X.Beans, 1000);
        long eid = (await X.J(await seller.Post("/api/engravings/craft", new CraftEngravingRequest { Type = "hp" }, Player.Key()))).GetProperty("engraving").GetProperty("id").GetInt64();
        await Player.Ok(await seller.Post($"/api/cards/{card.Id}/engravings/equip", new EquipEngravingRequest { EngravingId = eid, Slot = 1 }, Player.Key()));
        await X.Fund(owner, buyer, X.Stars, 1000);
        long lid = (await X.J(await seller.Post("/api/market/listings", new CreateListingRequest { CardId = card.Id, Currency = X.Stars, Price = 300, ConfirmPriceWarning = true }, Player.Key()))).GetProperty("listingId").GetInt64();
        Assert.Equal("card_busy", await Player.Code(await Up(seller, card.Id, "enhance", 12)));   // listed cards cannot be changed
        long totalBefore = X.Bal(await seller.Me(), X.Stars) + X.Bal(await buyer.Me(), X.Stars);
        await Player.Ok(await buyer.Post("/api/market/buy", new BuyRequest { ListingId = lid, ExpectedPrice = 300 }, Player.Key()));
        long totalAfter = X.Bal(await seller.Me(), X.Stars) + X.Bal(await buyer.Me(), X.Stars);
        Assert.Equal(totalBefore - 9, totalAfter);   // only the 3 % fee leaves; nothing is created
        var c = await buyer.Get<CardDto>($"/api/cards/{card.Id}");
        Assert.Equal((7, 12, 4, 40, card.Serial), (c.Stars, c.EnhanceLevel, c.SkillLevel, c.Level, c.Serial));
        Assert.Single(c.Engravings);   // policy: equipped engravings travel with the card
        Assert.Equal(await Mid(buyer), await f.Scalar<long>("SELECT membership_id FROM card_engravings WHERE id=@p0", eid));
        var pass = await buyer.Get<PassportDto>($"/api/cards/{card.Id}/passport");
        Assert.Equal(1, pass.TransferCount);
        Assert.Contains(pass.Upgrades, u => u.Type == "engraving_equip");
    }

    [Fact]
    public async Task Battles_use_server_stored_progression_for_unit_stats()
    {
        var p = await f.NewPlayer();
        var cards = await X.Open(p, 2);
        var pick = new List<CardDto>();
        foreach (var c in cards) if (pick.Count < 5 && pick.Count(x => x.FamilyId == c.FamilyId) < 2) pick.Add(c);
        await Player.Ok(await p.Post("/api/decks", new SaveDeckRequest { Name = "Main", IsDefault = true, Slots = pick.Select((c, i) => new DeckSlotDto { Position = i, CardId = c.Id }).ToList() }));
        var b1 = await p.PostOk<BattleDto>("/api/battles/pve", new StartPveRequest { StageId = "c1_s1" }, Player.Key());
        var u1 = b1.Units.Single(u => u.CardId == pick[0].Id);
        await f.Sql("UPDATE card_instances SET stars = 10, enhance_level = 20, level = 1 WHERE id=@p0", pick[0].Id);
        var b2 = await p.PostOk<BattleDto>("/api/battles/pve", new StartPveRequest { StageId = "c1_s1" }, Player.Key());
        var u2 = b2.Units.Single(u => u.CardId == pick[0].Id);
        double expected = Progression.StarMultiplier(10) * (1 + Progression.EnhanceBonus(20));
        Assert.InRange(u2.Atk / (double)u1.Atk, expected * 0.97, expected * 1.03);
        Assert.Equal(u1.Spd, u2.Spd);   // stars/enhancement never change speed
    }

    [Fact]
    public async Task Vip_and_invitations_are_separate_per_server()
    {
        var owner = await X.Owner(f);
        var admin = await Staff(f, "admin");
        int sid = 7000 + Random.Shared.Next(1000);
        await Player.Ok(await owner.Post("/api/admin/servers", new AdminServerRequest { Id = sid, Code = $"PS-{sid}", Status = "online", RegistrationOpen = true }));
        var p = await f.NewPlayer();
        await SetLevel(p, 60);
        await Player.Ok(await owner.Post("/api/admin/cardsys/vip/grant", new AdminGrantRequest { Uid = p.Uid, ServerId = 1, Amount = 10000, Reason = "integration test vip level" }));
        await Player.Ok(await admin.Post("/api/admin/cardsys/invitations", new AdminInviteRequest { Uid = p.Uid, ServerId = 1, MaxPacks = 1 }));
        await p.Enter(sid);
        await SetLevel(p, 60, sid);
        await X.Fund(owner, p, X.Stars, 1000, sid);
        Assert.Equal(0, (await p.Get<VipDto>("/api/vip")).Level);
        Assert.Equal("pack_vip_too_low", await Player.Code(await Buy(p, "divine_star_pack")));
        await Player.Ok(await owner.Post("/api/admin/cardsys/vip/grant", new AdminGrantRequest { Uid = p.Uid, ServerId = sid, Amount = 10000, Reason = "integration test vip level" }));
        Assert.Equal("pack_invitation_required", await Player.Code(await Buy(p, "divine_star_pack")));   // the PS-1 invitation does not count here
    }

    [Fact]
    public async Task Card_system_admin_endpoints_enforce_roles()
    {
        var p = await f.NewPlayer();
        var gm = await Staff(f, "gm");
        var admin = await Staff(f, "admin");
        Assert.Equal(HttpStatusCode.Forbidden, (await p.Http.GetAsync("/api/admin/cardsys/packs")).StatusCode);
        await Player.Ok(await gm.Http.GetAsync("/api/admin/cardsys/packs"));
        await Player.Ok(await gm.Http.GetAsync("/api/admin/cardsys/issued?serverId=1"));
        await Player.Ok(await gm.Http.GetAsync("/api/admin/cardsys/divine?serverId=1"));
        await Player.Ok(await gm.Http.GetAsync("/api/admin/cardsys/anomalies?serverId=1"));
        await Player.Ok(await gm.Http.GetAsync("/api/admin/cardsys/rainbow-stars?serverId=1"));
        await Player.Ok(await gm.Http.GetAsync("/api/admin/cardsys/materials?serverId=1"));
        Assert.Equal(HttpStatusCode.Forbidden, (await gm.Http.PatchAsJsonAsync("/api/admin/cardsys/packs/epic_pack", new AdminPackPatch { PriceAmount = 25, Reason = "integration test price" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.Post("/api/admin/cardsys/materials/grant", new AdminGrantRequest { Uid = p.Uid, ServerId = 1, ItemId = "pet_shard", Amount = 5, Reason = "integration test grant" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.Post("/api/admin/cardsys/vip/grant", new AdminGrantRequest { Uid = p.Uid, ServerId = 1, Amount = 5, Reason = "integration test grant" })).StatusCode);
        Assert.Equal("reason_required", await Player.Code(await admin.Http.PatchAsJsonAsync("/api/admin/cardsys/packs/epic_pack", new AdminPackPatch { PriceAmount = 25 })));
        await Player.Ok(await admin.Http.PatchAsJsonAsync("/api/admin/cardsys/packs/epic_pack", new AdminPackPatch { PriceAmount = 20, Reason = "integration test: same price, new version" }));
        var log = await X.GetJ(gm, "/api/admin/cardsys/config-log");
        Assert.Contains(log.EnumerateArray(), a => a.GetProperty("action").GetString() == "cardsys.pack.update");
        // an admin can never give itself the owner role through these endpoints (no such endpoint exists)
        Assert.Equal(HttpStatusCode.NotFound, (await admin.Post("/api/admin/cardsys/roles", new { role = "owner" })).StatusCode);
    }
}

internal static class HttpJsonExt
{
    public static Task<HttpResponseMessage> PatchAsJsonAsync<T>(this HttpClient c, string url, T body) =>
        c.PatchAsync(url, new StringContent(JsonSerializer.Serialize(body, new JsonSerializerOptions(JsonSerializerDefaults.Web)), System.Text.Encoding.UTF8, "application/json"));

    public static Task<HttpResponseMessage> PutAsJsonAsync<T>(this HttpClient c, string url, T body) =>
        c.PutAsync(url, new StringContent(JsonSerializer.Serialize(body, new JsonSerializerOptions(JsonSerializerDefaults.Web)), System.Text.Encoding.UTF8, "application/json"));
}
