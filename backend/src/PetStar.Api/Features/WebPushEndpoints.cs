using Microsoft.EntityFrameworkCore;
using PetStar.Api.Auth;
using PetStar.Api.Data;
using PetStar.Api.Infrastructure;

namespace PetStar.Api.Features;

public sealed class PushKeys { public string P256dh { get; set; } = ""; public string Auth { get; set; } = ""; }
public sealed class PushSubscribeRequest { public string Endpoint { get; set; } = ""; public PushKeys Keys { get; set; } = new(); public List<string>? Categories { get; set; } }
public sealed class PushEndpointRequest { public string Endpoint { get; set; } = ""; }
public sealed class PushCategoriesRequest { public string Endpoint { get; set; } = ""; public List<string> Categories { get; set; } = new(); }

/// <summary>Web Push subscription management (per device, per account). Sending happens in PushDispatcher.</summary>
public static class WebPushEndpoints
{
    public static void Map(WebApplication app)
    {
        // public key is needed before login (browser subscription is created with it); not a secret
        app.MapGet("/api/push/config", (WebPushOptions opt, PushTexts texts) =>
            new { enabled = opt.Enabled, publicKey = opt.Enabled ? opt.PublicKey : null, categories = texts.Categories }).RequireRateLimiting("public");

        var g = app.MapGroup("/api/push").RequireAuthorization().RequireRateLimiting("user");

        g.MapPost("/subscriptions", async (PushSubscribeRequest req, HttpContext ctx, PetStarDb db, WebPushOptions opt, PushTexts texts) =>
        {
            if (!opt.Enabled) throw new ApiException(503, "push_not_configured");
            var uid = ctx.User.AccountId();
            if (!PushEndpointPolicy.IsAllowed(req.Endpoint)) throw ApiException.BadRequest("push_endpoint_not_allowed");
            byte[] key, auth;
            try { key = WebPushCrypto.FromB64Url(req.Keys.P256dh); auth = WebPushCrypto.FromB64Url(req.Keys.Auth); }
            catch (FormatException) { throw ApiException.BadRequest("push_keys_invalid"); }
            if (key.Length != 65 || key[0] != 4 || auth.Length != 16) throw ApiException.BadRequest("push_keys_invalid");
            var cats = Categories(req.Categories, texts);
            if (await db.PushSubscriptions.CountAsync(s => s.AccountId == uid && s.Endpoint != req.Endpoint) >= 10) throw ApiException.Conflict("push_too_many_devices");
            var s = await db.PushSubscriptions.FirstOrDefaultAsync(x => x.Endpoint == req.Endpoint);
            if (s == null) db.PushSubscriptions.Add(s = new WebPushSubscription { Endpoint = req.Endpoint, CreatedAt = DateTimeOffset.UtcNow });
            // the same browser signing into another account takes the subscription over (one device = one account)
            s.AccountId = uid; s.P256dh = req.Keys.P256dh; s.Auth = req.Keys.Auth; s.FailureCount = 0;
            if (req.Categories != null || s.Id == 0) s.Categories = cats;
            var ua = ctx.Request.Headers.UserAgent.ToString();
            s.UserAgent = ua.Length > 200 ? ua[..200] : ua;
            await db.SaveChangesAsync();
            return Results.Ok(new { subscribed = true, categories = s.Categories.Split(',') });
        });

        g.MapPut("/subscriptions/categories", async (PushCategoriesRequest req, HttpContext ctx, PetStarDb db, PushTexts texts) =>
        {
            var uid = ctx.User.AccountId();
            var s = await db.PushSubscriptions.FirstOrDefaultAsync(x => x.Endpoint == req.Endpoint && x.AccountId == uid) ?? throw ApiException.NotFound("push_subscription_not_found");
            s.Categories = Categories(req.Categories, texts);
            await db.SaveChangesAsync();
            return Results.Ok(new { categories = s.Categories.Split(',', StringSplitOptions.RemoveEmptyEntries) });
        });

        g.MapPost("/subscriptions/status", async (PushEndpointRequest req, HttpContext ctx, PetStarDb db) =>
        {
            var uid = ctx.User.AccountId();
            var s = await db.PushSubscriptions.AsNoTracking().FirstOrDefaultAsync(x => x.Endpoint == req.Endpoint && x.AccountId == uid);
            return new { subscribed = s != null, categories = s?.Categories.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>() };
        });

        g.MapPost("/subscriptions/delete", async (PushEndpointRequest req, HttpContext ctx, PetStarDb db) =>
        {
            var uid = ctx.User.AccountId();
            int n = await db.PushSubscriptions.Where(x => x.Endpoint == req.Endpoint && x.AccountId == uid).ExecuteDeleteAsync();
            return Results.Ok(new { deleted = n > 0 });
        });

        g.MapPost("/test", async (HttpContext ctx, PetStarDb db, PushDispatcher dispatcher, WebPushOptions opt) =>
        {
            if (!opt.Enabled) throw new ApiException(503, "push_not_configured");
            var (_, mid) = ctx.User.Player();
            if (mid == 0) throw ApiException.BadRequest("server_not_selected");
            int delivered = await dispatcher.Handle(new PushJob(mid, "push_test", null, null, IgnorePresence: true), ctx.RequestAborted);
            return Results.Ok(new { delivered });
        }).RequireRateLimiting("giftcode");
    }

    private static string Categories(List<string>? requested, PushTexts texts)
    {
        var list = requested ?? texts.Categories.ToList();
        if (list.Any(c => !texts.Categories.Contains(c))) throw ApiException.BadRequest("push_category_invalid");
        return string.Join(',', list.Distinct());
    }
}
