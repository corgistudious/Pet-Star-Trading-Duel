using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PetStar.Api.Data;
using PetStar.Api.Infrastructure;

namespace PetStar.Api.Auth;

public sealed class JwtOptions
{
    public string Issuer { get; set; } = "petstar";
    public string Audience { get; set; } = "petstar-client";
    /// <summary>HMAC signing key (≥ 32 bytes). Supplied by environment / secret store, never committed.</summary>
    public string SigningKey { get; set; } = "";
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 30;
}

public sealed class TokenService(IOptions<JwtOptions> opt)
{
    private readonly JwtOptions _o = opt.Value;

    public SymmetricSecurityKey Key => new(Encoding.UTF8.GetBytes(_o.SigningKey));

    public (string token, DateTimeOffset expires) CreateAccessToken(Account acc, Guid sessionId, ServerMembership? m)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, acc.Id.ToString()),
            new(ClaimsExtensions.SessionClaim, sessionId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        };
        foreach (var r in acc.Roles) claims.Add(new Claim(ClaimTypes.Role, r.Role));
        if (m != null)
        {
            claims.Add(new Claim(ClaimsExtensions.ServerClaim, m.ServerId.ToString()));
            claims.Add(new Claim(ClaimsExtensions.MembershipClaim, m.Id.ToString()));
        }
        var expires = DateTimeOffset.UtcNow.AddMinutes(_o.AccessTokenMinutes);
        var jwt = new JwtSecurityToken(_o.Issuer, _o.Audience, claims, DateTime.UtcNow, expires.UtcDateTime,
            new SigningCredentials(Key, SecurityAlgorithms.HmacSha256));
        return (new JwtSecurityTokenHandler().WriteToken(jwt), expires);
    }

    public int RefreshDays => _o.RefreshTokenDays;
}

/// <summary>PBKDF2-HMAC-SHA512 (ASP.NET Core Identity v3 format, 100k iterations, per-password salt).</summary>
public sealed class PasswordService
{
    private readonly PasswordHasher<Account> _h = new();
    public string Hash(Account a, string password) => _h.HashPassword(a, password);

    public (bool ok, bool rehash) Verify(Account a, string password)
    {
        var r = _h.VerifyHashedPassword(a, a.PasswordHash, password);
        return (r != PasswordVerificationResult.Failed, r == PasswordVerificationResult.SuccessRehashNeeded);
    }
}

public interface IEmailSender
{
    Task SendAsync(string to, string subjectKey, string body, CancellationToken ct = default);
}

/// <summary>Development sender: keeps mail in memory (exposed via /dev/outbox in Development only).</summary>
public sealed class DevEmailSender(ILogger<DevEmailSender> log) : IEmailSender
{
    public static readonly List<(string To, string Subject, string Body, DateTimeOffset At)> Outbox = new();

    public Task SendAsync(string to, string subjectKey, string body, CancellationToken ct = default)
    {
        lock (Outbox) Outbox.Add((to, subjectKey, body, DateTimeOffset.UtcNow));
        log.LogInformation("DEV EMAIL to {To}: {Subject}", to, subjectKey);
        return Task.CompletedTask;
    }
}

public sealed class SmtpOptions
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public string User { get; set; } = "";
    public string Password { get; set; } = "";
    public string From { get; set; } = "no-reply@petstar.game";
}

public sealed class SmtpEmailSender(IOptions<SmtpOptions> opt) : IEmailSender
{
    public async Task SendAsync(string to, string subjectKey, string body, CancellationToken ct = default)
    {
        var o = opt.Value;
        using var client = new System.Net.Mail.SmtpClient(o.Host, o.Port)
        {
            EnableSsl = true,
            Credentials = new System.Net.NetworkCredential(o.User, o.Password),
        };
        using var msg = new System.Net.Mail.MailMessage(o.From, to, Subjects.GetValueOrDefault(subjectKey, "Pet Star"), body);
        await client.SendMailAsync(msg, ct);
    }

    private static readonly Dictionary<string, string> Subjects = new()
    {
        ["email.verify"] = "Pet Star: verify your email / Xác thực email",
        ["email.reset"] = "Pet Star: reset your password / Đặt lại mật khẩu",
    };
}

public sealed class AuditService(PetStarDb db, IHttpContextAccessor http)
{
    public void Add(string action, long? actor, string? targetType = null, string? targetId = null, int? serverId = null, object? data = null)
    {
        db.AuditLogs.Add(new AuditLog
        {
            ActorAccountId = actor, Action = action, TargetType = targetType, TargetId = targetId, ServerId = serverId,
            DataJson = System.Text.Json.JsonSerializer.Serialize(data ?? new { }, IdempotencyService.Json),
            Ip = http.HttpContext?.Connection.RemoteIpAddress?.ToString(), At = DateTimeOffset.UtcNow,
        });
    }
}
