using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using PetStar.Api.Auth;
using PetStar.Api.Data;
using PetStar.Api.Features;
using PetStar.Api.Infrastructure;
using StackExchange.Redis;

// `dotnet PetStar.Api.dll generate-vapid` prints a fresh VAPID key pair for Web Push (run on the server, store in
// the secrets file; the private key never goes into git or chat).
if (args.Length > 0 && args[0] == "generate-vapid")
{
    var (vapidPub, vapidPriv) = WebPushCrypto.GenerateVapidKeys();
    Console.WriteLine($"WEBPUSH_PUBLIC_KEY={vapidPub}\nWEBPUSH_PRIVATE_KEY={vapidPriv}");
    return;
}

var builder = WebApplication.CreateBuilder(args);
var cfg = builder.Configuration;
var env = builder.Environment;

// ---------- configuration & secrets ----------
var jwt = cfg.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
if (string.IsNullOrEmpty(jwt.SigningKey) || jwt.SigningKey.Length < 32)
{
    if (!env.IsDevelopment() && !env.IsEnvironment("Testing"))
        throw new InvalidOperationException("Jwt:SigningKey (>=32 chars) must be provided via environment/secret store");
    jwt.SigningKey = SecureRandomSource.Token(48); // ephemeral dev key
}
builder.Services.Configure<JwtOptions>(o =>
{
    cfg.GetSection("Jwt").Bind(o);
    o.SigningKey = jwt.SigningKey;
});
builder.Services.Configure<SmtpOptions>(cfg.GetSection("Smtp"));
builder.Services.Configure<GooglePlayOptions>(cfg.GetSection("GooglePlay"));

var connStr = cfg.GetConnectionString("Postgres") ?? throw new InvalidOperationException("ConnectionStrings:Postgres missing");
builder.Services.AddDbContext<PetStarDb>(o => o.UseNpgsql(connStr).UseSnakeCaseNamingConvention());

var seedDir = CatalogLoader.SeedDir(env.ContentRootPath);
builder.Services.AddSingleton(new GameData(CatalogLoader.LoadCatalog(seedDir), CatalogLoader.LoadEconomy(seedDir), CatalogLoader.LoadPve(seedDir),
    CatalogLoader.LoadValuation(seedDir), CatalogLoader.LoadMarket(seedDir), CatalogLoader.LoadAchievements(seedDir)));

var redisConn = cfg.GetConnectionString("Redis");
if (!string.IsNullOrEmpty(redisConn))
{
    builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConn));
    builder.Services.AddSingleton<IPresence, RedisPresence>();
}
else builder.Services.AddSingleton<IPresence, MemoryPresence>();

builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient("store");
// Web Push: configured only when WebPush:PublicKey/PrivateKey/Subject are provided (otherwise disabled, never faked)
var webPush = cfg.GetSection("WebPush").Get<WebPushOptions>() ?? new WebPushOptions();
builder.Services.AddSingleton(webPush);
builder.Services.AddSingleton(PushTexts.Load(seedDir));
builder.Services.AddSingleton<PushQueue>();
builder.Services.AddHttpClient("webpush", c => c.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddScoped<WebPushSender>();
builder.Services.AddSingleton<PushDispatcher>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<PushDispatcher>());
builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<PasswordService>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<EconomyService>();
builder.Services.AddScoped<VipService>();
builder.Services.AddScoped<PackGate>();
builder.Services.AddScoped<IdempotencyService>();
builder.Services.AddScoped<ServerGate>();
builder.Services.AddScoped<ValuationService>();
builder.Services.AddScoped<CardFactory>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<AchievementService>();
builder.Services.AddSingleton(MissionCatalog.Load(seedDir));
builder.Services.AddScoped<MissionService>();
builder.Services.AddScoped<FraudService>();
builder.Services.AddScoped<ContentGuard>();
builder.Services.AddHostedService<MarketMaintenanceService>();
builder.Services.AddHostedService<PvpTimeoutService>();
builder.Services.AddSingleton<RealtimeHub>();
builder.Services.AddSingleton<TicketStore>();

// CORS for the web client (Cloudflare Pages). Explicit origins only; bearer tokens, no cookies.
var corsOrigins = (cfg.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>()).Where(o => !string.IsNullOrWhiteSpace(o)).ToArray();
builder.Services.AddCors(o => o.AddPolicy("web", p =>
{
    if (corsOrigins.Length > 0) p.WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod().WithExposedHeaders("Idempotent-Replay").SetPreflightMaxAge(TimeSpan.FromHours(1));
}));
builder.Services.AddSingleton<IStoreVerifier, GooglePlayVerifier>();
if ((env.IsDevelopment() || env.IsEnvironment("Testing")) && cfg.GetValue("Iap:AllowFakeStore", false))
    builder.Services.AddSingleton<IStoreVerifier, FakeStoreVerifier>();
if (!string.IsNullOrEmpty(cfg["Smtp:Host"])) builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
else builder.Services.AddSingleton<IEmailSender, DevEmailSender>();

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    o.SerializerOptions.NumberHandling = JsonNumberHandling.AllowReadingFromString;
});

// ---------- authentication / authorization ----------
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.MapInboundClaims = false;
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidIssuer = jwt.Issuer, ValidAudience = jwt.Audience, IssuerSigningKey = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(jwt.SigningKey)),
        ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true, ValidateIssuerSigningKey = true,
        ClockSkew = TimeSpan.FromSeconds(30), NameClaimType = "sub", RoleClaimType = ClaimTypes.Role,
    };
    o.Events = new JwtBearerEvents { OnTokenValidated = SessionValidator.OnTokenValidated };
});
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("InServer", p => p.RequireAuthenticatedUser().RequireClaim(ClaimsExtensions.ServerClaim).RequireClaim(ClaimsExtensions.MembershipClaim))
    .AddPolicy("ServerAdmin", p => p.RequireRole("owner", "admin"))
    .AddPolicy("GameMaster", p => p.RequireRole("owner", "admin", "gm"))
    .AddPolicy("Moderation", p => p.RequireRole("owner", "admin", "gm", "moderator"))
    .AddPolicy("Owner", p => p.RequireRole("owner"));

// ---------- rate limiting ----------
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    int authPerMin = cfg.GetValue("RateLimits:AuthPerMinute", 20);
    int userPerMin = cfg.GetValue("RateLimits:UserPerMinute", 240);
    o.AddPolicy("auth", ctx => RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = authPerMin, Window = TimeSpan.FromMinutes(1) }));
    o.AddPolicy("public", ctx => RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1) }));
    o.AddPolicy("user", ctx => RateLimitPartition.GetTokenBucketLimiter(ctx.User.FindFirst("sub")?.Value ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
        _ => new TokenBucketRateLimiterOptions { TokenLimit = userPerMin / 4, TokensPerPeriod = userPerMin / 60 + 1, ReplenishmentPeriod = TimeSpan.FromSeconds(1) }));
    o.AddPolicy("giftcode", ctx => RateLimitPartition.GetFixedWindowLimiter(ctx.User.FindFirst("sub")?.Value ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = cfg.GetValue("RateLimits:GiftcodePerMinute", 10), Window = TimeSpan.FromMinutes(1) }));
    o.OnRejected = async (c, ct) => await c.HttpContext.Response.WriteAsJsonAsync(new ErrorBody("rate_limited", null, c.HttpContext.TraceIdentifier), ct);
});

builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks().AddDbContextCheck<PetStarDb>("postgres");
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear(); o.KnownProxies.Clear(); // only Nginx can reach the API container (docker network)
});

var app = builder.Build();
app.UseForwardedHeaders();
// after the Nginx forwarded headers: the Cloudflare Pages proxy (same-origin /api on petstar.pages.dev) sets the player IP
app.UseMiddleware<PetStar.Api.Infrastructure.EdgeProxyMiddleware>();
app.UseExceptionHandler();
app.UseCors("web");
// Origin is checked in the /ws handler: browser origins must be allow-listed; native clients (no Origin) are allowed.
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(25) });
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapHealthChecks("/health/live", new() { Predicate = _ => false });
app.MapHealthChecks("/health/ready");
app.MapGet("/api/version", () => new { name = "Pet Star: Trading Duel API", version = typeof(Program).Assembly.GetName().Version?.ToString() });

AuthEndpoints.Map(app);
ServerEndpoints.Map(app);
PackEndpoints.Map(app);
CardEndpoints.Map(app);
CardUpgradeEndpoints.Map(app);
DeckEndpoints.Map(app);
BattleEndpoints.Map(app);
// Real-money purchases are disabled in this phase (owner decision); the verification code stays for a future,
// separately approved payment system and is only mapped when Iap:Enabled=true.
if (cfg.GetValue("Iap:Enabled", false)) IapEndpoints.Map(app);
PassportEndpoints.Map(app);
MarketEndpoints.Map(app);
AuctionEndpoints.Map(app);
SocialEndpoints.Map(app);
FeedEndpoints.Map(app);
CommunityEndpoints.Map(app);
RankedEndpoints.Map(app);
AdminEconomyEndpoints.Map(app);
GuildChatEndpoints.Map(app);
LivePvpEndpoints.Map(app);
RewardsEndpoints.Map(app);
MissionEndpoints.Map(app);
WebPushEndpoints.Map(app);
AdminEndpoints.Map(app);
AdminCardSystemEndpoints.Map(app);

if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
    app.MapGet("/dev/outbox", () => { lock (DevEmailSender.Outbox) return DevEmailSender.Outbox.Select(m => new { m.To, m.Subject, m.Body, m.At }).ToList(); });

AuthEndpoints.WebBaseUrl = cfg["App:WebBaseUrl"] is { Length: > 0 } webBase ? webBase
    : (cfg.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>()).FirstOrDefault(o => !string.IsNullOrWhiteSpace(o));
await Seeder.Run(app.Services, cfg, app.Logger);
app.Run();

public partial class Program;
