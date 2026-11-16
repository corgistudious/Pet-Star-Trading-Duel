using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using PetStar.Api.Infrastructure;

namespace PetStar.Api.Tests;

/// <summary>Trust boundary for the Cloudflare Pages proxy (no database needed).</summary>
public class EdgeProxyTests
{
    private static async Task<(HttpContext ctx, bool reachedApp)> Run(Dictionary<string, string?> cfg, string path, params (string k, string v)[] headers)
    {
        bool reached = false;
        var mw = new EdgeProxyMiddleware(_ => { reached = true; return Task.CompletedTask; },
            new ConfigurationBuilder().AddInMemoryCollection(cfg).Build());
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = path;
        ctx.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.2"); // the Nginx / Cloudflare hop
        ctx.Response.Body = new MemoryStream();
        foreach (var (k, v) in headers) ctx.Request.Headers[k] = v;
        await mw.Invoke(ctx);
        return (ctx, reached);
    }

    private static readonly Dictionary<string, string?> Secret = new() { ["Proxy:SharedSecret"] = "s3cret-value" };

    [Fact]
    public async Task Valid_secret_sets_player_ip_and_strips_headers()
    {
        var (ctx, ok) = await Run(Secret, "/api/auth/login", (EdgeProxyMiddleware.AuthHeader, "s3cret-value"), (EdgeProxyMiddleware.ClientIpHeader, "203.0.113.9"));
        Assert.True(ok);
        Assert.Equal("203.0.113.9", ctx.Connection.RemoteIpAddress!.ToString());
        Assert.False(ctx.Request.Headers.ContainsKey(EdgeProxyMiddleware.AuthHeader));
        Assert.False(ctx.Request.Headers.ContainsKey(EdgeProxyMiddleware.ClientIpHeader));
    }

    [Fact]
    public async Task Forged_or_missing_secret_is_not_trusted()
    {
        var (ctx, ok) = await Run(Secret, "/api/auth/login", (EdgeProxyMiddleware.AuthHeader, "wrong"), (EdgeProxyMiddleware.ClientIpHeader, "203.0.113.9"));
        Assert.True(ok);
        Assert.Equal("10.0.0.2", ctx.Connection.RemoteIpAddress!.ToString());
        (ctx, ok) = await Run(new(), "/api/x", (EdgeProxyMiddleware.AuthHeader, ""), (EdgeProxyMiddleware.ClientIpHeader, "203.0.113.9"));
        Assert.True(ok);
        Assert.Equal("10.0.0.2", ctx.Connection.RemoteIpAddress!.ToString());
    }

    [Fact]
    public async Task Require_secret_blocks_direct_api_and_ws_but_not_health()
    {
        var cfg = new Dictionary<string, string?>(Secret) { ["Proxy:RequireSecret"] = "true" };
        var (ctx, ok) = await Run(cfg, "/api/player");
        Assert.False(ok); Assert.Equal(403, ctx.Response.StatusCode);
        (ctx, ok) = await Run(cfg, "/ws");
        Assert.False(ok); Assert.Equal(403, ctx.Response.StatusCode);
        (_, ok) = await Run(cfg, "/health/ready");
        Assert.True(ok);
        (_, ok) = await Run(cfg, "/api/player", (EdgeProxyMiddleware.AuthHeader, "s3cret-value"));
        Assert.True(ok);
        // RequireSecret without a secret configured never locks anyone out
        (_, ok) = await Run(new() { ["Proxy:RequireSecret"] = "true" }, "/api/player");
        Assert.True(ok);
    }
}
