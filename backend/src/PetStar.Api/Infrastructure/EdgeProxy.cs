using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace PetStar.Api.Infrastructure;

/// <summary>
/// Trust boundary for requests forwarded by the Cloudflare Pages Functions of https://petstar.pages.dev
/// (cloudflare/pages/functions). Config (VPS env PROXY_SHARED_SECRET -> Proxy:SharedSecret, Proxy:RequireSecret):
///  - With a shared secret, a request carrying the matching X-PetStar-Proxy-Auth header is from our proxy, so its
///    X-PetStar-Client-IP becomes the remote IP (rate limits stay per player instead of per Cloudflare egress IP).
///  - Proxy:RequireSecret=true refuses /api and /ws requests that bypass Cloudflare (403); health checks stay open.
///  - Without a secret nothing is trusted and nothing is refused (local development, tests, native clients).
/// The two headers are always removed before the app sees the request.
/// </summary>
public sealed class EdgeProxyMiddleware
{
    public const string AuthHeader = "X-PetStar-Proxy-Auth";
    public const string ClientIpHeader = "X-PetStar-Client-IP";

    private readonly RequestDelegate _next;
    private readonly byte[]? _secret;
    private readonly bool _require;

    public EdgeProxyMiddleware(RequestDelegate next, IConfiguration cfg)
    {
        _next = next;
        var s = cfg["Proxy:SharedSecret"];
        _secret = string.IsNullOrWhiteSpace(s) ? null : Encoding.UTF8.GetBytes(s.Trim());
        _require = _secret != null && cfg.GetValue("Proxy:RequireSecret", false);
    }

    public Task Invoke(HttpContext ctx)
    {
        var headers = ctx.Request.Headers;
        var auth = headers[AuthHeader].ToString();
        var clientIp = headers[ClientIpHeader].ToString();
        headers.Remove(AuthHeader);
        headers.Remove(ClientIpHeader);

        bool fromProxy = _secret != null && auth.Length > 0
            && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(auth), _secret);
        if (fromProxy && IPAddress.TryParse(clientIp, out var ip)) ctx.Connection.RemoteIpAddress = ip;

        if (_require && !fromProxy)
        {
            var path = ctx.Request.Path;
            if (path.StartsWithSegments("/api") || path.StartsWithSegments("/ws"))
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                return ctx.Response.WriteAsJsonAsync(new { code = "direct_access_forbidden" });
            }
        }
        return _next(ctx);
    }
}
