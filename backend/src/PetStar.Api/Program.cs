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
builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<PasswordService>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<EconomyService>();
builder.Services.AddScoped<IdempotencyService>();
builder.Services.AddScoped<ServerGate>();
builder.Services.AddScoped<ValuationService>();
builder.Services.AddScoped<CardFactory>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<AchievementService>();
builder.Services.AddScoped<FraudService>();
builder.Services.AddScoped<ContentGuard>();
builder.Services.AddHostedService<MarketMaintenanceService>();
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
app.UseExceptionHandler();
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
AdminEndpoints.Map(app);

if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
    app.MapGet("/dev/outbox", () => { lock (DevEmailSender.Outbox) return DevEmailSender.Outbox.Select(m => new { m.To, m.Subject, m.Body, m.At }).ToList(); });

await Seeder.Run(app.Services, cfg, app.Logger);
app.Run();

public partial class Program;
