using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PetStar.Api.Auth;
using PetStar.Api.Data;
using PetStar.Api.Infrastructure;
using PetStar.Contracts;

namespace PetStar.Api.Features;

public sealed record StoreVerification(bool Valid, string? OrderId, string? Reason, string RawJson, bool Consumable = true);

public interface IStoreVerifier
{
    string Platform { get; }
    bool Configured { get; }
    Task<StoreVerification> VerifyAsync(string productId, string purchaseToken, CancellationToken ct);
    Task ConsumeAsync(string productId, string purchaseToken, CancellationToken ct);
}

public sealed class GooglePlayOptions
{
    public string PackageName { get; set; } = "";
    /// <summary>Path to the Play Console service-account JSON (mounted secret, never committed).</summary>
    public string ServiceAccountJsonPath { get; set; } = "";
}

/// <summary>Google Play Developer API (purchases.products.get / consume) verifier.
/// Requires a service account with "View financial data / Manage orders" on the Play Console app.</summary>
public sealed class GooglePlayVerifier(IOptions<GooglePlayOptions> opt, IHttpClientFactory http) : IStoreVerifier
{
    private readonly GooglePlayOptions _o = opt.Value;
    public string Platform => "google_play";
    public bool Configured => !string.IsNullOrEmpty(_o.PackageName) && File.Exists(_o.ServiceAccountJsonPath);

    public async Task<StoreVerification> VerifyAsync(string productId, string token, CancellationToken ct)
    {
        var client = await Authed(ct);
        var url = $"https://androidpublisher.googleapis.com/androidpublisher/v3/applications/{Uri.EscapeDataString(_o.PackageName)}/purchases/products/{Uri.EscapeDataString(productId)}/tokens/{Uri.EscapeDataString(token)}";
        var resp = await client.GetAsync(url, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) return new StoreVerification(false, null, $"google_http_{(int)resp.StatusCode}", body);
        var j = JsonNode.Parse(body)!;
        // purchaseState: 0 purchased, 1 canceled, 2 pending. consumptionState: 0 not consumed.
        int state = j["purchaseState"]?.GetValue<int>() ?? -1;
        int consumption = j["consumptionState"]?.GetValue<int>() ?? 0;
        if (state != 0) return new StoreVerification(false, j["orderId"]?.GetValue<string>(), state == 2 ? "pending" : "not_purchased", body);
        if (consumption != 0) return new StoreVerification(false, j["orderId"]?.GetValue<string>(), "already_consumed", body);
        return new StoreVerification(true, j["orderId"]?.GetValue<string>(), null, body);
    }

    public async Task ConsumeAsync(string productId, string token, CancellationToken ct)
    {
        var client = await Authed(ct);
        var url = $"https://androidpublisher.googleapis.com/androidpublisher/v3/applications/{Uri.EscapeDataString(_o.PackageName)}/purchases/products/{Uri.EscapeDataString(productId)}/tokens/{Uri.EscapeDataString(token)}:consume";
        (await client.PostAsync(url, null, ct)).EnsureSuccessStatusCode();
    }

    private async Task<HttpClient> Authed(CancellationToken ct)
    {
        var sa = JsonNode.Parse(await File.ReadAllTextAsync(_o.ServiceAccountJsonPath, ct))!;
        string email = sa["client_email"]!.GetValue<string>(), pem = sa["private_key"]!.GetValue<string>();
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string B64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        string header = B64(Encoding.UTF8.GetBytes("{\"alg\":\"RS256\",\"typ\":\"JWT\"}"));
        string claims = B64(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        {
            ["iss"] = email, ["scope"] = "https://www.googleapis.com/auth/androidpublisher",
            ["aud"] = "https://oauth2.googleapis.com/token", ["iat"] = now, ["exp"] = now + 3000,
        }));
        using var rsa = RSA.Create();
        rsa.ImportFromPem(pem);
        string sig = B64(rsa.SignData(Encoding.ASCII.GetBytes(header + "." + claims), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        var client = http.CreateClient("store");
        var tok = await client.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer", ["assertion"] = header + "." + claims + "." + sig,
        }), ct);
        tok.EnsureSuccessStatusCode();
        var access = JsonNode.Parse(await tok.Content.ReadAsStringAsync(ct))!["access_token"]!.GetValue<string>();
        var authed = http.CreateClient("store");
        authed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access);
        return authed;
    }
}

/// <summary>Development/test-only verifier. Registered only when Iap:AllowFakeStore=true AND environment is Development/Testing.</summary>
public sealed class FakeStoreVerifier : IStoreVerifier
{
    public string Platform => "fake_store";
    public bool Configured => true;
    public Task<StoreVerification> VerifyAsync(string productId, string token, CancellationToken ct) =>
        Task.FromResult(token.StartsWith("fake-ok-")
            ? new StoreVerification(true, "FAKE." + token, null, "{\"fake\":true}")
            : new StoreVerification(false, null, "not_purchased", "{\"fake\":true}"));
    public Task ConsumeAsync(string productId, string token, CancellationToken ct) => Task.CompletedTask;
}

public static class IapEndpoints
{
    public static void Map(WebApplication app)
    {
        var g = ServerEndpoints.GameGroup(app);

        g.MapGet("/iap/products", (GameData data) => data.Economy.IapProducts);

        g.MapPost("/iap/verify", async (IapVerifyRequest req, HttpContext ctx, PetStarDb db, GameData data, EconomyService eco,
            IEnumerable<IStoreVerifier> verifiers, AuditService audit, ILoggerFactory lf, CancellationToken ct) =>
        {
            var (sid, mid) = ctx.User.Player();
            var uid = ctx.User.AccountId();
            var product = data.Economy.IapProducts.FirstOrDefault(p => p.ProductId == req.ProductId) ?? throw ApiException.BadRequest("unknown_product");
            var verifier = verifiers.FirstOrDefault(v => v.Platform == req.Platform);
            if (verifier == null || !verifier.Configured) throw new ApiException(503, "iap_unavailable");
            if (string.IsNullOrEmpty(req.PurchaseToken) || req.PurchaseToken.Length > 4096) throw ApiException.BadRequest("invalid_purchase_token");
            var tokenHash = SecureRandomSource.Sha256(req.PurchaseToken);

            // already processed (retry, or replay of a token): never grant twice
            var existing = await db.IapPurchases.AsNoTracking().FirstOrDefaultAsync(p => p.Platform == req.Platform && p.PurchaseTokenHash == tokenHash, ct);
            if (existing != null)
            {
                if (existing.AccountId != uid) { audit.Add("iap.token_replay_other_account", uid, "iap", existing.Id.ToString()); await db.SaveChangesAsync(ct); throw ApiException.Conflict("purchase_already_used"); }
                return new IapVerifyResponse { Status = existing.Status, ProductId = existing.ProductId, Granted = 0, Currencies = await eco.Balances(mid) };
            }

            var v = await verifier.VerifyAsync(product.ProductId, req.PurchaseToken, ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var purchase = new IapPurchase
            {
                Id = Guid.NewGuid(), AccountId = uid, MembershipId = mid, ServerId = sid, Platform = req.Platform, ProductId = product.ProductId,
                PurchaseTokenHash = tokenHash, OrderId = v.OrderId, Status = v.Valid ? "granted" : "rejected", RejectReason = v.Reason,
                VerificationJson = v.RawJson.Length > 8000 ? "{}" : v.RawJson, CreatedAt = DateTimeOffset.UtcNow,
                GrantedAt = v.Valid ? DateTimeOffset.UtcNow : null,
            };
            db.IapPurchases.Add(purchase);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException) { throw ApiException.Conflict("purchase_in_progress"); } // concurrent duplicate lost the race
            long granted = 0;
            if (v.Valid)
            {
                granted = product.Amount + product.Bonus;
                await eco.Currency(mid, sid, product.Currency, granted, "iap_purchase", purchase.Id.ToString("N"));
            }
            audit.Add(v.Valid ? "iap.granted" : "iap.rejected", uid, "iap", purchase.Id.ToString(), sid, new { product.ProductId, v.OrderId, v.Reason });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            if (v.Valid)
            {
                try { await verifier.ConsumeAsync(product.ProductId, req.PurchaseToken, ct); }
                catch (Exception ex) { lf.CreateLogger("iap").LogWarning(ex, "consume failed for purchase {Id}; grant already recorded", purchase.Id); }
            }
            return new IapVerifyResponse { Status = purchase.Status, ProductId = product.ProductId, Granted = granted, Currencies = await eco.Balances(mid) };
        });
    }
}
