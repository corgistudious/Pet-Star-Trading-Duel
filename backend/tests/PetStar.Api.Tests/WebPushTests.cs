using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PetStar.Api.Features;
using PetStar.Api.Infrastructure;
using PetStar.Contracts;

namespace PetStar.Api.Tests;

/// <summary>In-process stand-in for FCM / Mozilla / Apple push services.</summary>
public static class FakePush
{
    public static readonly (string pub, string priv) Keys = WebPushCrypto.GenerateVapidKeys();
    public static string VapidPublic => Keys.pub;
    public static string VapidPrivate => Keys.priv;
    public sealed record Sent(string Endpoint, Dictionary<string, string> Headers, byte[] Body);
    public static readonly ConcurrentQueue<Sent> Requests = new();
    public static readonly ConcurrentDictionary<string, HttpStatusCode> Responses = new();

    public sealed class Handler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var h = r.Headers.Concat(r.Content!.Headers).ToDictionary(x => x.Key, x => string.Join(",", x.Value), StringComparer.OrdinalIgnoreCase);
            Requests.Enqueue(new Sent(r.RequestUri!.ToString(), h, await r.Content.ReadAsByteArrayAsync(ct)));
            return new HttpResponseMessage(Responses.GetValueOrDefault(r.RequestUri!.ToString(), HttpStatusCode.Created));
        }
    }

    /// <summary>A browser-side subscription: endpoint + P-256 key pair + auth secret.</summary>
    public sealed class Device
    {
        public readonly string Endpoint = "https://fcm.googleapis.com/fcm/send/" + Guid.NewGuid().ToString("N");
        public readonly ECDiffieHellman Key = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        public readonly byte[] Auth = RandomNumberGenerator.GetBytes(16);
        public byte[] Public => WebPushCrypto.PublicBytes(Key.ExportParameters(false));
        public object Body(List<string>? categories = null) => new { endpoint = Endpoint, keys = new { p256dh = WebPushCrypto.B64Url(Public), auth = WebPushCrypto.B64Url(Auth) }, categories };
        public List<Sent> Received() => Requests.Where(x => x.Endpoint == Endpoint).ToList();
        public JsonElement Decrypt(Sent s) => JsonDocument.Parse(PushTestDecryptor.Decrypt(s.Body, Key, Public, Auth)).RootElement;

        public async Task<Sent> WaitOne(int timeoutMs = 6000)
        {
            for (int i = 0; i < timeoutMs / 50; i++) { var r = Received(); if (r.Count > 0) return r[0]; await Task.Delay(50); }
            throw new Xunit.Sdk.XunitException("no push delivered to " + Endpoint);
        }
        public async Task AssertNone(int waitMs = 1200) { await Task.Delay(waitMs); Assert.Empty(Received()); }
    }
}

[Collection("api")]
public class WebPushTests(ApiFactory f)
{
    [Fact]
    public async Task Config_exposes_public_key_and_subscribe_rejects_foreign_endpoints_and_bad_keys()
    {
        var p = await f.NewPlayer("push0");
        var cfg = await X.J(await p.Http.GetAsync("/api/push/config"));
        Assert.True(cfg.GetProperty("enabled").GetBoolean());
        Assert.Equal(FakePush.VapidPublic, cfg.GetProperty("publicKey").GetString());
        var d = new FakePush.Device();
        foreach (var bad in new[] { "https://evil.example.com/push", "http://fcm.googleapis.com/fcm/send/x", "https://169.254.169.254/latest", "https://fcm.googleapis.com.evil.io/x", "https://user@fcm.googleapis.com/x", "https://fcm.googleapis.com:8443/x" })
            Assert.Equal("push_endpoint_not_allowed", await Player.Code(await p.Post("/api/push/subscriptions", new { endpoint = bad, keys = new { p256dh = WebPushCrypto.B64Url(d.Public), auth = WebPushCrypto.B64Url(d.Auth) } })));
        Assert.Equal("push_keys_invalid", await Player.Code(await p.Post("/api/push/subscriptions", new { endpoint = d.Endpoint, keys = new { p256dh = "AAAA", auth = WebPushCrypto.B64Url(d.Auth) } })));
        Assert.Equal("push_category_invalid", await Player.Code(await p.Post("/api/push/subscriptions", d.Body(new() { "spam" }))));
        Assert.True(PushEndpointPolicy.IsAllowed("https://web.push.apple.com/QGmx"));
        Assert.True(PushEndpointPolicy.IsAllowed("https://wns2-par02p.notify.windows.com/w/?token=x"));
    }

    [Fact]
    public async Task Offline_seller_gets_an_encrypted_vapid_signed_push_when_a_card_sells()
    {
        var owner = await X.Owner(f);
        var seller = await f.NewPlayer("pushS");
        var buyer = await f.NewPlayer("pushB");
        await X.Fund(owner, buyer, X.Beans, 1000);
        var device = new FakePush.Device();
        await Player.Ok(await seller.Post("/api/push/subscriptions", device.Body()));
        var card = (await X.Open(seller))[0];
        var listing = await X.J(await seller.Post("/api/market/listings", new CreateListingRequest { CardId = card.Id, Currency = X.Beans, Price = 1234, ConfirmPriceWarning = true }, Player.Key()));
        await Player.Ok(await buyer.Post("/api/market/buy", new BuyRequest { ListingId = listing.GetProperty("listingId").GetInt64(), ExpectedPrice = 1234 }, Player.Key()));

        await device.WaitOne();
        FakePush.Sent sent = null!;
        for (int i = 0; i < 100 && sent == null; i++)   // achievements may push too; find the sale message
        {
            sent = device.Received().FirstOrDefault(x => device.Decrypt(x).GetProperty("type").GetString() == "market_sold")!;
            if (sent == null) await Task.Delay(50);
        }
        Assert.NotNull(sent);
        Assert.Equal("aes128gcm", sent.Headers["Content-Encoding"]);
        Assert.Equal("86400", sent.Headers["TTL"]);
        var auth = sent.Headers["Authorization"];
        Assert.EndsWith("k=" + FakePush.VapidPublic, auth);
        var jwt = auth[8..auth.IndexOf(',')].Split('.');
        using (var v = ECDsa.Create(WebPushCrypto.Params(WebPushCrypto.FromB64Url(FakePush.VapidPublic))))
            Assert.True(v.VerifyData(Encoding.ASCII.GetBytes(jwt[0] + "." + jwt[1]), WebPushCrypto.FromB64Url(jwt[2]), HashAlgorithmName.SHA256));
        Assert.Equal("https://fcm.googleapis.com", JsonDocument.Parse(WebPushCrypto.FromB64Url(jwt[1])).RootElement.GetProperty("aud").GetString());

        var msg = device.Decrypt(sent);   // only the browser can read it
        Assert.Equal("Card sold", msg.GetProperty("title").GetString());
        Assert.Contains("1,234 Mini Green Bean", msg.GetProperty("body").GetString());
        Assert.Contains(card.Name, msg.GetProperty("body").GetString());
        Assert.Equal("/market", msg.GetProperty("url").GetString());
        Assert.Equal("PS-1", msg.GetProperty("server").GetString());
        Assert.DoesNotContain(card.Serial, Encoding.UTF8.GetString(sent.Body)); // nothing readable in transit
    }

    [Fact]
    public async Task Connected_players_get_the_in_app_notification_instead_and_categories_are_respected()
    {
        var a = await f.NewPlayer("pushA");
        var b = await f.NewPlayer("pushF");
        var device = new FakePush.Device();
        await Player.Ok(await a.Post("/api/push/subscriptions", device.Body()));

        var ticket = (await X.J(await a.Post("/api/realtime/ticket", null))).GetProperty("ticket").GetString();
        var ws = await f.Server.CreateWebSocketClient().ConnectAsync(new Uri($"ws://localhost/ws?ticket={ticket}"), CancellationToken.None);
        await ws.ReceiveAsync(new byte[1024], CancellationToken.None); // hello
        await Player.Ok(await b.Post($"/api/players/{a.Uid}/follow", null));
        await device.AssertNone();                                      // online: no push
        try { await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None); } catch { }
        await Task.Delay(300);

        await Player.Ok(await a.Http.PutAsJsonAsync("/api/push/subscriptions/categories", new { endpoint = device.Endpoint, categories = new[] { "market" } }));
        await Player.Ok(await b.Post("/api/friends/requests", new FriendRequestBody { Uid = a.Uid }));
        await device.AssertNone();                                      // social disabled on this device

        await Player.Ok(await a.Http.PutAsJsonAsync("/api/push/subscriptions/categories", new { endpoint = device.Endpoint, categories = new[] { "social", "market" } }));
        var c = await f.NewPlayer("pushC");
        await Player.Ok(await c.Post($"/api/players/{a.Uid}/follow", null));
        var msg = device.Decrypt(await device.WaitOne());
        Assert.Equal("New follower", msg.GetProperty("title").GetString());
        Assert.Equal($"/player/{c.Uid}", msg.GetProperty("url").GetString());
    }

    [Fact]
    public async Task Gone_subscriptions_are_removed_test_push_and_device_takeover()
    {
        var a = await f.NewPlayer("pushG");
        var device = new FakePush.Device();
        await Player.Ok(await a.Post("/api/push/subscriptions", device.Body()));
        var r = await X.J(await a.Post("/api/push/test", null));
        Assert.Equal(1, r.GetProperty("delivered").GetInt32());
        var vi = device.Decrypt(device.Received()[0]);
        Assert.Equal("Pet Star notifications are on", vi.GetProperty("title").GetString());

        // the same browser signs into another account: the subscription moves, the old account stops receiving
        var b = await f.NewPlayer("pushH");
        await Player.Ok(await b.Post("/api/push/subscriptions", device.Body()));
        Assert.False((await X.J(await a.Post("/api/push/subscriptions/status", new { endpoint = device.Endpoint }))).GetProperty("subscribed").GetBoolean());
        Assert.True((await X.J(await b.Post("/api/push/subscriptions/status", new { endpoint = device.Endpoint }))).GetProperty("subscribed").GetBoolean());

        FakePush.Responses[device.Endpoint] = HttpStatusCode.Gone;       // user revoked permission in the browser
        await Player.Ok(await b.Post("/api/push/test", null));
        Assert.Equal(0L, await f.Scalar<long>("SELECT count(*) FROM push_subscriptions WHERE endpoint = @p0", device.Endpoint));
    }

    [Fact]
    public async Task Pushes_for_rolled_back_notifications_are_never_sent()
    {
        var a = await f.NewPlayer("pushR");
        var device = new FakePush.Device();
        await Player.Ok(await a.Post("/api/push/subscriptions", device.Body()));
        var mid = await f.Scalar<long>("SELECT id FROM server_memberships WHERE account_id = @p0", a.Uid);
        var dispatcher = f.Services.GetRequiredService<PushDispatcher>();
        int n = await dispatcher.Handle(new PushJob(mid, "new_follower", null, "never-committed-" + Guid.NewGuid()), CancellationToken.None);
        Assert.Equal(0, n);
        Assert.Empty(device.Received());
    }
}
