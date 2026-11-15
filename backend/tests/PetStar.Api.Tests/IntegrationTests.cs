using System.Net;
using System.Net.Http.Json;
using PetStar.Contracts;
using PetStar.Rules.Battle;

namespace PetStar.Api.Tests;

[Collection("api")]
public class AuthTests(ApiFactory f)
{
    [Fact]
    public async Task Register_assigns_uid_from_100000_and_login_works()
    {
        var p = new Player(f.CreateClient());
        var a = await p.Register($"a{Guid.NewGuid():N}@test.local", "Passw0rd1", "Alice");
        Assert.True(a.Uid >= 100000);
        var r = await p.Http.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = p.Email, Password = p.Password, Platform = "windows" });
        await Player.Ok(r);
        var me = await p.Get<ProfileDto>("/api/me");
        Assert.Equal(a.Uid, me.Uid);
        Assert.False(me.EmailVerified);
    }

    [Fact]
    public async Task Duplicate_email_and_weak_password_rejected()
    {
        var p = new Player(f.CreateClient());
        var email = $"d{Guid.NewGuid():N}@test.local";
        await p.Register(email, "Passw0rd1", "Dup");
        var r = await p.Http.PostAsJsonAsync("/api/auth/register", new RegisterRequest { Email = email.ToUpperInvariant(), Password = "Passw0rd1", DisplayName = "Dup2" });
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        var w = await p.Http.PostAsJsonAsync("/api/auth/register", new RegisterRequest { Email = $"w{Guid.NewGuid():N}@test.local", Password = "short", DisplayName = "Weak" });
        Assert.Equal("weak_password", await Player.Code(w));
    }

    [Fact]
    public async Task Password_is_hashed_not_stored()
    {
        var p = new Player(f.CreateClient());
        await p.Register($"h{Guid.NewGuid():N}@test.local", "Passw0rd1", "Hash");
        var hash = await f.Scalar<string>("SELECT password_hash FROM accounts WHERE id = @p0", p.Uid);
        Assert.DoesNotContain("Passw0rd1", hash);
        Assert.True(hash!.Length > 50);
    }

    [Fact]
    public async Task Lockout_after_five_failed_logins()
    {
        var p = new Player(f.CreateClient());
        await p.Register($"l{Guid.NewGuid():N}@test.local", "Passw0rd1", "Lock");
        for (int i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await p.Http.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = p.Email, Password = "wrong" + i })).StatusCode);
        var r = await p.Http.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = p.Email, Password = p.Password });
        Assert.Equal("account_locked_temporarily", await Player.Code(r));
    }

    [Fact]
    public async Task Refresh_rotates_and_reuse_revokes_all_sessions()
    {
        var p = new Player(f.CreateClient());
        await p.Register($"r{Guid.NewGuid():N}@test.local", "Passw0rd1", "Rot");
        var old = p.Refresh;
        var r1 = await p.Http.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest { RefreshToken = old });
        await Player.Ok(r1);
        var fresh = (await r1.Content.ReadFromJsonAsync<AuthResponse>(Player.Json))!;
        Assert.NotEqual(old, fresh.RefreshToken);
        // replaying the rotated token is treated as theft: everything is revoked
        var replay = await p.Http.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest { RefreshToken = old });
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        var r2 = await p.Http.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest { RefreshToken = fresh.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, r2.StatusCode);
    }

    [Fact]
    public async Task Logout_revokes_access_token()
    {
        var p = new Player(f.CreateClient());
        await p.Register($"o{Guid.NewGuid():N}@test.local", "Passw0rd1", "Out");
        await Player.Ok(await p.Post("/api/auth/logout", null));
        Assert.Equal(HttpStatusCode.Unauthorized, (await p.Http.GetAsync("/api/me")).StatusCode);
    }

    [Fact]
    public async Task Email_verification_and_password_reset_flow()
    {
        var p = new Player(f.CreateClient());
        await p.Register($"v{Guid.NewGuid():N}@test.local", "Passw0rd1", "Verify");
        string TokenFor(string subject) => p.Http.GetFromJsonAsync<List<OutboxMail>>("/dev/outbox").Result!
            .Last(m => m.To == p.Email && m.Subject == subject).Body.Split("Token: ")[1].Trim();
        await Player.Ok(await p.Post("/api/auth/verify-email", new TokenRequest { Token = TokenFor("email.verify") }));
        Assert.True((await p.Get<ProfileDto>("/api/me")).EmailVerified);
        // token is single use
        Assert.Equal(HttpStatusCode.BadRequest, (await p.Post("/api/auth/verify-email", new TokenRequest { Token = TokenFor("email.verify") })).StatusCode);

        await Player.Ok(await p.Post("/api/auth/forgot-password", new EmailRequest { Email = p.Email }));
        await Player.Ok(await p.Post("/api/auth/reset-password", new ResetPasswordRequest { Token = TokenFor("email.reset"), NewPassword = "NewPassw0rd2" }));
        Assert.Equal(HttpStatusCode.Unauthorized, (await p.Http.GetAsync("/api/me")).StatusCode); // sessions revoked
        Assert.Equal(HttpStatusCode.OK, (await p.Http.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = p.Email, Password = "NewPassw0rd2" })).StatusCode);
        // unknown email: same response (no enumeration)
        Assert.Equal(HttpStatusCode.OK, (await p.Post("/api/auth/forgot-password", new EmailRequest { Email = "nobody@test.local" })).StatusCode);
    }

    private sealed record OutboxMail(string To, string Subject, string Body);
}

[Collection("api")]
public class ServerAndEconomyTests(ApiFactory f)
{
    [Fact]
    public async Task Gameplay_requires_selected_server()
    {
        var p = new Player(f.CreateClient());
        await p.Register($"s{Guid.NewGuid():N}@test.local", "Passw0rd1", "NoServer");
        Assert.Equal(HttpStatusCode.Forbidden, (await p.Http.GetAsync("/api/player")).StatusCode);
        var servers = await p.Get<List<ServerDto>>("/api/servers");
        Assert.Contains(servers, s => s.Code == "PS-1" && s.Name == "Global" && s.Status == "online");
    }

    [Fact]
    public async Task Joining_grants_starter_resources_once_with_ledger()
    {
        var p = await f.NewPlayer();
        var me = await p.Me();
        Assert.Equal(500, me.Currencies["MINI_GREEN_BEAN"]);
        Assert.Equal(0, me.Currencies["RAINBOW_STAR"]);
        Assert.Equal(3, me.Packs["basic_pack"]);
        await p.Enter(1); // re-entering does not grant again
        Assert.Equal(500, (await p.Me()).Currencies["MINI_GREEN_BEAN"]);
        var ledger = await p.Get<List<LedgerEntryDto>>("/api/economy/ledger");
        Assert.Single(ledger);
        Assert.Equal("starter_grant", ledger[0].Reason);
        Assert.Equal(500, ledger[0].BalanceAfter);
    }

    [Fact]
    public async Task Purchase_pack_spends_beans_and_never_goes_negative()
    {
        var p = await f.NewPlayer();
        var buy = await p.PostOk<PurchasePackResponse>("/api/packs/purchase", new PurchasePackRequest { PackTypeId = "basic_pack", Quantity = 4 }, Player.Key());
        Assert.Equal(7, buy.Owned);
        Assert.Equal(100, buy.Currencies["MINI_GREEN_BEAN"]);
        var r = await p.Post("/api/packs/purchase", new PurchasePackRequest { PackTypeId = "basic_pack", Quantity = 2 }, Player.Key());
        Assert.Equal("insufficient_mini_green_bean", await Player.Code(r));
        Assert.Equal(100, (await p.Me()).Currencies["MINI_GREEN_BEAN"]);
    }

    [Fact]
    public async Task Concurrent_purchases_cannot_double_spend()
    {
        var p = await f.NewPlayer(); // 500 beans => at most 5 single purchases of 100
        var tasks = Enumerable.Range(0, 12).Select(_ => p.Post("/api/packs/purchase", new PurchasePackRequest { PackTypeId = "basic_pack", Quantity = 1 }, Player.Key()));
        var res = await Task.WhenAll(tasks);
        Assert.Equal(5, res.Count(r => r.IsSuccessStatusCode));
        var me = await p.Me();
        Assert.Equal(0, me.Currencies["MINI_GREEN_BEAN"]);
        Assert.Equal(8, me.Packs["basic_pack"]);
        var sum = await f.Scalar<long>("SELECT COALESCE(SUM(delta),0) FROM economy_ledger WHERE membership_id = (SELECT id FROM server_memberships WHERE account_id=@p0)", p.Uid);
        Assert.Equal(0, sum); // ledger reconciles with wallet
    }
}

[Collection("api")]
public class PackOpeningTests(ApiFactory f)
{
    [Fact]
    public async Task Open_pack_creates_exactly_five_owned_cards_and_consumes_one_pack()
    {
        var p = await f.NewPlayer();
        var o = await p.PostOk<PackOpeningDto>("/api/packs/open", new OpenPackRequest { PackTypeId = "basic_pack" }, Player.Key());
        Assert.Equal(5, o.Cards.Count);
        Assert.Equal(5, o.Cards.Select(c => c.Id).Distinct().Count());
        var me = await p.Me();
        Assert.Equal(2, me.Packs["basic_pack"]);
        Assert.Equal(5, me.CardCount);
        var home = await p.Get<CardPageDto>("/api/cards");
        Assert.Equal(o.Cards.Select(c => c.Id).OrderBy(x => x), home.Items.Select(c => c.Id).OrderBy(x => x));
        Assert.True(me.CodexDiscovered >= 1);
        var hist = await f.Scalar<long>("SELECT count(*) FROM card_ownership_history WHERE ref_id = @p0", o.OpeningId.ToString("N"));
        Assert.Equal(5, hist);
    }

    [Fact]
    public async Task Retry_with_same_key_returns_same_result_without_new_cards()
    {
        var p = await f.NewPlayer();
        var key = Player.Key();
        var a = await p.PostOk<PackOpeningDto>("/api/packs/open", new OpenPackRequest { PackTypeId = "basic_pack" }, key);
        var r = await p.Post("/api/packs/open", new OpenPackRequest { PackTypeId = "basic_pack" }, key);
        await Player.Ok(r);
        Assert.Equal("true", r.Headers.GetValues("Idempotent-Replay").Single());
        var b = (await r.Content.ReadFromJsonAsync<PackOpeningDto>(Player.Json))!;
        Assert.Equal(a.OpeningId, b.OpeningId);
        Assert.Equal(a.Cards.Select(c => c.Id), b.Cards.Select(c => c.Id));
        var me = await p.Me();
        Assert.Equal(2, me.Packs["basic_pack"]);
        Assert.Equal(5, me.CardCount);
        // same key, different request => rejected
        var reuse = await p.Post("/api/packs/open", new OpenPackRequest { PackTypeId = "premium_pack" }, key);
        Assert.Equal("idempotency_key_reused", await Player.Code(reuse));
    }

    [Fact]
    public async Task Concurrent_duplicate_requests_open_only_once()
    {
        var p = await f.NewPlayer();
        var key = Player.Key();
        var res = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => p.Post("/api/packs/open", new OpenPackRequest { PackTypeId = "basic_pack" }, key)));
        Assert.All(res, r => Assert.True(r.IsSuccessStatusCode));
        var ids = (await Task.WhenAll(res.Select(r => r.Content.ReadFromJsonAsync<PackOpeningDto>(Player.Json)))).Select(o => o!.OpeningId).Distinct();
        Assert.Single(ids);
        Assert.Equal(5, (await p.Me()).CardCount);
    }

    [Fact]
    public async Task Cannot_open_more_packs_than_owned_even_concurrently()
    {
        var p = await f.NewPlayer(); // 3 packs
        var res = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => p.Post("/api/packs/open", new OpenPackRequest { PackTypeId = "basic_pack" }, Player.Key())));
        Assert.Equal(3, res.Count(r => r.IsSuccessStatusCode));
        var me = await p.Me();
        Assert.Equal(0, me.Packs["basic_pack"]);
        Assert.Equal(15, me.CardCount);
        Assert.Equal(15, await f.Scalar<long>("SELECT count(DISTINCT card_id) FROM pack_opening_results r JOIN pack_openings o ON o.id=r.opening_id JOIN server_memberships m ON m.id=o.membership_id WHERE m.account_id=@p0", p.Uid));
    }

    [Fact]
    public async Task Missing_idempotency_key_rejected_and_unacknowledged_results_recoverable()
    {
        var p = await f.NewPlayer();
        Assert.Equal("idempotency_key_required", await Player.Code(await p.Post("/api/packs/open", new OpenPackRequest { PackTypeId = "basic_pack" })));
        var o = await p.PostOk<PackOpeningDto>("/api/packs/open", new OpenPackRequest { PackTypeId = "basic_pack" }, Player.Key());
        var pending = await p.Get<List<PackOpeningDto>>("/api/packs/openings?unacknowledged=true");
        Assert.Contains(pending, x => x.OpeningId == o.OpeningId);
        await Player.Ok(await p.Post($"/api/packs/openings/{o.OpeningId}/ack", null));
        Assert.DoesNotContain(await p.Get<List<PackOpeningDto>>("/api/packs/openings?unacknowledged=true"), x => x.OpeningId == o.OpeningId);
    }

    [Fact]
    public async Task Odds_are_public()
    {
        var odds = await f.CreateClient().GetFromJsonAsync<PackOddsDto>("/api/packs/basic_pack/odds", Player.Json);
        Assert.Equal(100.0, Math.Round(odds!.Slots1To4.Values.Sum(), 4));
        Assert.Equal(100.0, Math.Round(odds.Slot5.Values.Sum(), 4));
    }
}

[Collection("api")]
public class MultiServerTests(ApiFactory f)
{
    private async Task<Player> Admin()
    {
        var a = await f.NewPlayer("admin");
        await f.Sql("INSERT INTO account_roles (account_id, role, granted_at) VALUES (@p0, 'owner', now())", a.Uid);
        await a.Enter(1); // refresh token with role claim
        return a;
    }

    [Fact]
    public async Task Non_admin_cannot_manage_servers()
    {
        var p = await f.NewPlayer();
        Assert.Equal(HttpStatusCode.Forbidden, (await p.Post("/api/admin/servers", new AdminServerRequest { Id = 99, Code = "PS-99" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await p.Http.GetAsync("/api/admin/audit")).StatusCode);
    }

    [Fact]
    public async Task Progress_is_independent_per_server_and_not_leaked()
    {
        var admin = await Admin();
        int sid = 100 + Random.Shared.Next(1000);
        await Player.Ok(await admin.Post("/api/admin/servers", new AdminServerRequest { Id = sid, Code = $"PS-{sid}", Name = "Global", Status = "online", RegistrationOpen = true }));

        var p = await f.NewPlayer();
        var o = await p.PostOk<PackOpeningDto>("/api/packs/open", new OpenPackRequest { PackTypeId = "basic_pack" }, Player.Key());
        var cardOnPs1 = o.Cards[0].Id;

        await p.Enter(sid);
        var me2 = await p.Me();
        Assert.Equal(sid, me2.ServerId);
        Assert.Equal(0, me2.CardCount);              // fresh progress
        Assert.Equal(3, me2.Packs["basic_pack"]);    // own starter grant
        Assert.Equal(HttpStatusCode.NotFound, (await p.Http.GetAsync($"/api/cards/{cardOnPs1}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await p.Post($"/api/cards/{cardOnPs1}/evolve", null, Player.Key())).StatusCode);

        await p.Enter(1);
        var me1 = await p.Me();
        Assert.Equal(5, me1.CardCount);              // PS-1 data intact
        Assert.Equal(2, me1.Packs["basic_pack"]);
    }

    [Fact]
    public async Task Database_rejects_card_owned_by_membership_of_another_server()
    {
        var admin = await Admin();
        int sid = 2000 + Random.Shared.Next(1000);
        await Player.Ok(await admin.Post("/api/admin/servers", new AdminServerRequest { Id = sid, Code = $"PS-{sid}", Status = "online", RegistrationOpen = true }));
        var p = await f.NewPlayer();
        var mid = await f.Scalar<long>("SELECT id FROM server_memberships WHERE account_id=@p0 AND server_id=1", p.Uid);
        var ex = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => f.Sql(
            "INSERT INTO card_instances (server_id, owner_membership_id, serial, form_id, rarity, quality, finish, level, exp, skill_level, is_favorite, is_locked, origin, created_at, updated_at) " +
            "VALUES (@p0, @p1, 'ZZTEST000001', 'pet_emberkit', 'N', 'B', 'Normal', 1, 0, 1, false, false, 'test', now(), now())", sid, mid));
        Assert.Equal("23503", ex.SqlState); // foreign key violation (composite owner+server)
    }

    [Fact]
    public async Task Closed_registration_and_maintenance_are_enforced()
    {
        var admin = await Admin();
        int sid = 3000 + Random.Shared.Next(1000);
        await Player.Ok(await admin.Post("/api/admin/servers", new AdminServerRequest { Id = sid, Code = $"PS-{sid}", Status = "online", RegistrationOpen = false }));
        var p = await f.NewPlayer();
        Assert.Equal("server_registration_closed", await Player.Code(await p.Post($"/api/servers/{sid}/enter", new EnterServerRequest())));

        var patch = new HttpRequestMessage(HttpMethod.Patch, "/api/admin/servers/1") { Content = JsonContent.Create(new AdminServerPatch { Status = "maintenance" }) };
        await Player.Ok(await admin.Http.SendAsync(patch));
        try
        {
            var r = await p.Http.GetAsync("/api/player");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, r.StatusCode);
            Assert.Equal("server_maintenance", await Player.Code(r));
            Assert.Equal(HttpStatusCode.OK, (await admin.Http.GetAsync("/api/player")).StatusCode); // staff bypass
        }
        finally
        {
            var back = new HttpRequestMessage(HttpMethod.Patch, "/api/admin/servers/1") { Content = JsonContent.Create(new AdminServerPatch { Status = "online" }) };
            await Player.Ok(await admin.Http.SendAsync(back));
        }
        Assert.True(await f.Scalar<long>("SELECT count(*) FROM admin_audit_logs WHERE action='admin.server.update'") >= 2);
    }
}

[Collection("api")]
public class EvolutionDeckBattleTests(ApiFactory f)
{
    private async Task<(Player p, long mid, List<CardDto> cards)> PlayerWithCards(int packs = 3)
    {
        var p = await f.NewPlayer();
        var cards = new List<CardDto>();
        for (int i = 0; i < packs; i++) cards.AddRange((await p.PostOk<PackOpeningDto>("/api/packs/open", new OpenPackRequest { PackTypeId = "basic_pack" }, Player.Key())).Cards);
        var mid = await f.Scalar<long>("SELECT id FROM server_memberships WHERE account_id=@p0 AND server_id=1", p.Uid);
        return (p, mid, cards);
    }

    [Fact]
    public async Task Evolution_keeps_card_id_consumes_materials_once()
    {
        var (p, mid, cards) = await PlayerWithCards(1);
        CardDto? card = null;
        foreach (var c in cards)
            if ((await p.Get<EvolutionPreviewDto>($"/api/cards/{c.Id}/evolution-preview")).Next != null) { card = c; break; }
        if (card == null) return; // all standalone (unlikely); nothing to evolve
        var element = card.Element;
        // too low level
        Assert.Equal("evolution_leveltoolow", await Player.Code(await p.Post($"/api/cards/{card.Id}/evolve", null, Player.Key())));
        await f.Sql("UPDATE card_instances SET level = 15 WHERE id = @p0", card.Id);
        await f.Sql("INSERT INTO inventory_items (membership_id, item_id, quantity) VALUES (@p0, @p1, 7)", mid, "essence_" + element);
        var preview = await p.Get<EvolutionPreviewDto>($"/api/cards/{card.Id}/evolution-preview");
        Assert.Equal("Ok", preview.Check);
        Assert.NotNull(preview.Next);

        var key = Player.Key();
        var r1 = await p.Post($"/api/cards/{card.Id}/evolve", null, key);
        await Player.Ok(r1);
        await Player.Ok(await p.Post($"/api/cards/{card.Id}/evolve", null, key)); // retry => replay
        var after = await p.Get<CardDto>($"/api/cards/{card.Id}");
        Assert.Equal(card.Id, after.Id);
        Assert.Equal(preview.Next!.FormId, after.FormId);
        Assert.Equal(card.Rarity, after.Rarity); // rarity is not upgraded
        var me = await p.Me();
        Assert.Equal(2, me.Items["essence_" + element]);
        Assert.Equal(1, await f.Scalar<long>("SELECT count(*) FROM economy_ledger WHERE reason = 'evolution' AND delta = -500 AND ref_id = @p0", $"card:{card.Id}:{card.FormId}"));
        Assert.Equal(1, await f.Scalar<long>("SELECT count(*) FROM card_evolution_history WHERE card_id=@p0", card.Id));
        var codex = await p.Get<CodexDto>("/api/codex");
        Assert.Contains(codex.Entries, e => e.FormId == after.FormId && e.Discovered);
    }

    [Fact]
    public async Task Deck_validation_and_save()
    {
        var (p, _, cards) = await PlayerWithCards(2);
        var bad = new SaveDeckRequest { Name = "Dup", Slots = Enumerable.Range(0, 5).Select(i => new DeckSlotDto { Position = i, CardId = cards[0].Id }).ToList() };
        Assert.Equal(HttpStatusCode.BadRequest, (await p.Post("/api/decks", bad)).StatusCode);
        var other = await f.NewPlayer();
        var stolen = new SaveDeckRequest { Name = "Steal", Slots = cards.Take(5).Select((c, i) => new DeckSlotDto { Position = i, CardId = c.Id }).ToList() };
        Assert.Equal("deck_card_not_owned", await Player.Code(await other.Post("/api/decks", stolen)));
        var deck = await SaveValidDeck(p, cards);
        Assert.True(deck.IsDefault);
        Assert.True(deck.TotalPower > 0);
    }

    private static async Task<DeckDto> SaveValidDeck(Player p, List<CardDto> cards)
    {
        // pick 5 cards with at most 2 per family
        var pick = new List<CardDto>();
        foreach (var c in cards.OrderByDescending(c => c.Power))
            if (pick.Count(x => x.FamilyId == c.FamilyId) < 2 && pick.Count < 5) pick.Add(c);
        Assert.Equal(5, pick.Count);
        return await p.PostOk<DeckDto>("/api/decks", new SaveDeckRequest { Name = "Main", IsDefault = true, Slots = pick.Select((c, i) => new DeckSlotDto { Position = i, CardId = c.Id }).ToList() });
    }

    [Fact]
    public async Task Pve_battle_full_flow_with_server_authority()
    {
        var (p, _, cards) = await PlayerWithCards(3);
        await SaveValidDeck(p, cards);
        var b = await p.PostOk<BattleDto>("/api/battles/pve", new StartPveRequest { StageId = "c1_s1" }, Player.Key());
        Assert.Equal(10, b.Units.Count);
        Assert.InRange(b.MyEnergy, 3, 5); // 3 base, +1 per "battle_start_energy" passive in the deck
        Assert.Equal("active", b.Status);

        // illegal: ultimate costs more than available energy / unit acts twice
        var illegal = new SubmitRoundRequest { Round = 1, Actions = new() { new PlannedAction { UnitIndex = 0 }, new PlannedAction { UnitIndex = 0 } } };
        Assert.Equal("duplicate_action", await Player.Code(await p.Post($"/api/battles/{b.Id}/rounds", illegal)));
        Assert.Equal("round_mismatch", await Player.Code(await p.Post($"/api/battles/{b.Id}/rounds", new SubmitRoundRequest { Round = 5 })));

        int guard = 0;
        BattleDto cur = b;
        while (cur.Status == "active" && guard++ < 25)
            cur = await p.PostOk<BattleDto>($"/api/battles/{b.Id}/rounds", new SubmitRoundRequest { Round = cur.Round });
        Assert.Equal("finished", cur.Status);
        Assert.NotNull(cur.WinnerSide);
        // replaying an old round never re-resolves or re-grants
        var before = await p.Me();
        var replay = await p.PostOk<BattleDto>($"/api/battles/{b.Id}/rounds", new SubmitRoundRequest { Round = 1 });
        Assert.Equal("finished", replay.Status);
        var after = await p.Me();
        Assert.Equal(before.Currencies["MINI_GREEN_BEAN"], after.Currencies["MINI_GREEN_BEAN"]);
        if (cur.WinnerSide == 0)
        {
            Assert.NotNull(cur.Rewards);
            Assert.True(cur.Rewards!.FirstClear);
            var stages = await p.Get<List<PveStageDto>>("/api/pve/stages");
            Assert.True(stages.First(s => s.Id == "c1_s2").Unlocked);
        }
        Assert.Equal(1, await f.Scalar<long>("SELECT count(*) FROM battle_results WHERE battle_id=@p0", b.Id));
    }

    [Fact]
    public async Task Locked_stage_and_foreign_battle_access_rejected()
    {
        var (p, _, cards) = await PlayerWithCards(3);
        await SaveValidDeck(p, cards);
        Assert.Equal("stage_locked", await Player.Code(await p.Post("/api/battles/pve", new StartPveRequest { StageId = "c1_boss" }, Player.Key())));
        var b = await p.PostOk<BattleDto>("/api/battles/pve", new StartPveRequest { StageId = "c1_s1" }, Player.Key());
        var other = await f.NewPlayer();
        Assert.Equal(HttpStatusCode.NotFound, (await other.Http.GetAsync($"/api/battles/{b.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Post($"/api/battles/{b.Id}/rounds", new SubmitRoundRequest { Round = 1 })).StatusCode);
        var s = await p.PostOk<BattleDto>($"/api/battles/{b.Id}/surrender", null);
        Assert.Equal(1, s.WinnerSide);
    }
}

[Collection("api")]
public class IapTests(ApiFactory f)
{
    [Fact]
    public async Task Real_money_purchases_are_disabled_by_default()
    {
        var p = await f.NewPlayer();
        var r = await p.Post("/api/iap/verify", new IapVerifyRequest { Platform = "fake_store", ProductId = "rainbow_star_60", PurchaseToken = "fake-ok-x" });
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    [Fact]
    public async Task Verified_purchase_grants_rainbow_star_exactly_once()
    {
        var p = await f.NewPlayer(client: f.IapClient());
        var token = "fake-ok-" + Guid.NewGuid().ToString("N");
        var req = new IapVerifyRequest { Platform = "fake_store", ProductId = "rainbow_star_300", PurchaseToken = token };
        var r = await p.PostOk<IapVerifyResponse>("/api/iap/verify", req);
        Assert.Equal("granted", r.Status);
        Assert.Equal(330, r.Granted);
        Assert.Equal(330, r.Currencies["RAINBOW_STAR"]);
        var again = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => p.Post("/api/iap/verify", req)));
        Assert.Equal(330, (await p.Me()).Currencies["RAINBOW_STAR"]);
        // token replay from a different account is refused
        var thief = await f.NewPlayer(client: f.IapClient());
        Assert.Equal("purchase_already_used", await Player.Code(await thief.Post("/api/iap/verify", req)));
        Assert.Equal(0, (await thief.Me()).Currencies["RAINBOW_STAR"]);
    }

    [Fact]
    public async Task Invalid_purchase_and_unconfigured_store_grant_nothing()
    {
        var p = await f.NewPlayer(client: f.IapClient());
        var r = await p.PostOk<IapVerifyResponse>("/api/iap/verify", new IapVerifyRequest { Platform = "fake_store", ProductId = "rainbow_star_60", PurchaseToken = "bogus-" + Guid.NewGuid() });
        Assert.Equal("rejected", r.Status);
        Assert.Equal(0, r.Granted);
        var g = await p.Post("/api/iap/verify", new IapVerifyRequest { Platform = "google_play", ProductId = "rainbow_star_60", PurchaseToken = "x" });
        Assert.Equal("iap_unavailable", await Player.Code(g));
        Assert.Equal(0, (await p.Me()).Currencies["RAINBOW_STAR"]);
    }
}
