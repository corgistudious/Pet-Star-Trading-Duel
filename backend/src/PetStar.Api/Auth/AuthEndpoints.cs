using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using PetStar.Api.Data;
using PetStar.Api.Infrastructure;
using PetStar.Contracts;

namespace PetStar.Api.Auth;

public static class AuthEndpoints
{
    private const int MaxFailedLogins = 5;
    private static readonly TimeSpan Lockout = TimeSpan.FromMinutes(15);
    private static readonly string[] Languages = { "en", "vi" };

    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/auth").RequireRateLimiting("auth");

        g.MapPost("/register", async (RegisterRequest req, PetStarDb db, PasswordService pw, TokenService tokens,
            IEmailSender mail, AuditService audit, HttpContext ctx) =>
        {
            var email = ValidateEmail(req.Email);
            ValidatePassword(req.Password);
            var name = ValidateDisplayName(req.DisplayName);
            var norm = email.ToUpperInvariant();
            if (await db.Accounts.AnyAsync(a => a.EmailNormalized == norm)) throw ApiException.Conflict("email_taken");
            var acc = new Account
            {
                Email = email, EmailNormalized = norm, DisplayName = name,
                Language = Languages.Contains(req.Language) ? req.Language : "en", CreatedAt = DateTimeOffset.UtcNow,
            };
            acc.PasswordHash = pw.Hash(acc, req.Password);
            db.Accounts.Add(acc);
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateException) { throw ApiException.Conflict("email_taken"); }
            await SendEmailToken(db, mail, acc, "verify");
            audit.Add("auth.register", acc.Id, "account", acc.Id.ToString());
            var resp = await IssueSession(db, tokens, acc, null, ctx, null);
            return Results.Ok(resp);
        });

        g.MapPost("/login", async (LoginRequest req, PetStarDb db, PasswordService pw, TokenService tokens, AuditService audit, HttpContext ctx) =>
        {
            var norm = (req.Email ?? "").Trim().ToUpperInvariant();
            var acc = await db.Accounts.Include(a => a.Roles).FirstOrDefaultAsync(a => a.EmailNormalized == norm);
            var now = DateTimeOffset.UtcNow;
            if (acc == null) throw new ApiException(401, "invalid_credentials");
            if (acc.LockoutUntil > now) throw new ApiException(429, "account_locked_temporarily");
            var (ok, rehash) = pw.Verify(acc, req.Password ?? "");
            if (!ok)
            {
                acc.FailedLoginCount++;
                if (acc.FailedLoginCount >= MaxFailedLogins) { acc.LockoutUntil = now + Lockout; acc.FailedLoginCount = 0; audit.Add("auth.lockout", acc.Id); }
                audit.Add("auth.login_failed", acc.Id);
                await db.SaveChangesAsync();
                throw new ApiException(401, "invalid_credentials");
            }
            EnsureNotBanned(acc);
            if (rehash) acc.PasswordHash = pw.Hash(acc, req.Password!);
            acc.FailedLoginCount = 0;
            acc.LastLoginAt = now;
            audit.Add("auth.login", acc.Id, data: new { req.Platform });
            return Results.Ok(await IssueSession(db, tokens, acc, null, ctx, req));
        });

        g.MapPost("/refresh", async (RefreshRequest req, PetStarDb db, TokenService tokens, AuditService audit, HttpContext ctx) =>
        {
            var hash = SecureRandomSource.Sha256(req.RefreshToken ?? "");
            await using var tx = await db.Database.BeginTransactionAsync();
            var s = await db.Sessions.FromSqlRaw("SELECT * FROM sessions WHERE refresh_token_hash = {0} FOR UPDATE", hash).FirstOrDefaultAsync();
            var now = DateTimeOffset.UtcNow;
            if (s == null) throw new ApiException(401, "invalid_refresh_token");
            if (s.RevokedAt != null)
            {
                // reuse of a rotated token => token theft suspected: revoke the whole session family for this account
                if (s.RevokeReason == "rotated")
                {
                    await db.Sessions.Where(x => x.AccountId == s.AccountId && x.RevokedAt == null)
                        .ExecuteUpdateAsync(u => u.SetProperty(x => x.RevokedAt, now).SetProperty(x => x.RevokeReason, "refresh_reuse"));
                    SessionValidator.ForgetAccount(s.AccountId);
                    audit.Add("auth.refresh_reuse_detected", s.AccountId);
                    await db.SaveChangesAsync();
                    await tx.CommitAsync();
                }
                throw new ApiException(401, "invalid_refresh_token");
            }
            if (s.ExpiresAt < now) throw new ApiException(401, "refresh_token_expired");
            var acc = await db.Accounts.Include(a => a.Roles).FirstAsync(a => a.Id == s.AccountId);
            EnsureNotBanned(acc);
            // rotate: old session row revoked, new row issued (same device metadata)
            s.RevokedAt = now; s.RevokeReason = "rotated";
            var serverId = ctx.Request.Headers["X-Server-Id"].ToString();
            ServerMembership? m = null;
            if (int.TryParse(serverId, out var sid))
                m = await db.Memberships.FirstOrDefaultAsync(x => x.AccountId == acc.Id && x.ServerId == sid);
            var resp = await IssueSession(db, tokens, acc, m, ctx, new LoginRequest { Platform = s.Platform, DeviceName = s.DeviceName });
            await tx.CommitAsync();
            return Results.Ok(resp);
        });

        g.MapPost("/verify-email", async (TokenRequest req, PetStarDb db, AuditService audit) =>
        {
            var t = await ConsumeEmailToken(db, req.Token, "verify");
            var acc = await db.Accounts.FirstAsync(a => a.Id == t.AccountId);
            acc.EmailVerified = true;
            audit.Add("auth.email_verified", acc.Id);
            await db.SaveChangesAsync();
            return Results.Ok(new { verified = true });
        });

        g.MapPost("/forgot-password", async (EmailRequest req, PetStarDb db, IEmailSender mail) =>
        {
            var norm = (req.Email ?? "").Trim().ToUpperInvariant();
            var acc = await db.Accounts.FirstOrDefaultAsync(a => a.EmailNormalized == norm);
            if (acc != null) await SendEmailToken(db, mail, acc, "reset");
            // identical response whether or not the account exists (no account enumeration)
            return Results.Ok(new { sent = true });
        });

        g.MapPost("/reset-password", async (ResetPasswordRequest req, PetStarDb db, PasswordService pw, AuditService audit) =>
        {
            ValidatePassword(req.NewPassword);
            var t = await ConsumeEmailToken(db, req.Token, "reset");
            var acc = await db.Accounts.FirstAsync(a => a.Id == t.AccountId);
            acc.PasswordHash = pw.Hash(acc, req.NewPassword);
            acc.FailedLoginCount = 0; acc.LockoutUntil = null;
            var now = DateTimeOffset.UtcNow;
            await db.Sessions.Where(x => x.AccountId == acc.Id && x.RevokedAt == null)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.RevokedAt, now).SetProperty(x => x.RevokeReason, "password_reset"));
            SessionValidator.ForgetAccount(acc.Id);
            audit.Add("auth.password_reset", acc.Id);
            await db.SaveChangesAsync();
            return Results.Ok(new { reset = true });
        });

        var authed = app.MapGroup("/api").RequireAuthorization().RequireRateLimiting("user");

        authed.MapPost("/auth/resend-verification", async (HttpContext ctx, PetStarDb db, IEmailSender mail) =>
        {
            var acc = await db.Accounts.FirstAsync(a => a.Id == ctx.User.AccountId());
            if (!acc.EmailVerified) await SendEmailToken(db, mail, acc, "verify");
            return Results.Ok(new { sent = !acc.EmailVerified });
        });

        authed.MapPost("/auth/logout", async (HttpContext ctx, PetStarDb db, AuditService audit) =>
        {
            var sid = ctx.User.SessionId();
            var now = DateTimeOffset.UtcNow;
            await db.Sessions.Where(s => s.Id == sid && s.RevokedAt == null)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.RevokedAt, now).SetProperty(x => x.RevokeReason, "logout"));
            SessionValidator.Forget(sid);
            audit.Add("auth.logout", ctx.User.AccountId());
            await db.SaveChangesAsync();
            return Results.Ok(new { loggedOut = true });
        });

        authed.MapGet("/auth/sessions", async (HttpContext ctx, PetStarDb db) =>
        {
            var uid = ctx.User.AccountId(); var current = ctx.User.SessionId(); var now = DateTimeOffset.UtcNow;
            var list = await db.Sessions.AsNoTracking().Where(s => s.AccountId == uid && s.RevokedAt == null && s.ExpiresAt > now)
                .OrderByDescending(s => s.LastUsedAt).ToListAsync();
            return list.Select(s => new SessionDto { Id = s.Id, Platform = s.Platform, DeviceName = s.DeviceName, CreatedAt = s.CreatedAt, LastUsedAt = s.LastUsedAt, Current = s.Id == current });
        });

        authed.MapDelete("/auth/sessions/{id:guid}", async (Guid id, HttpContext ctx, PetStarDb db) =>
        {
            var uid = ctx.User.AccountId(); var now = DateTimeOffset.UtcNow;
            int n = await db.Sessions.Where(s => s.Id == id && s.AccountId == uid && s.RevokedAt == null)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.RevokedAt, now).SetProperty(x => x.RevokeReason, "user_revoked"));
            SessionValidator.Forget(id);
            return n == 0 ? throw ApiException.NotFound() : Results.Ok(new { revoked = true });
        });

        authed.MapGet("/me", async (HttpContext ctx, PetStarDb db) =>
        {
            var acc = await db.Accounts.Include(a => a.Roles).AsNoTracking().FirstAsync(a => a.Id == ctx.User.AccountId());
            return ToProfile(acc);
        });

        authed.MapPatch("/me", async (UpdateProfileRequest req, HttpContext ctx, PetStarDb db) =>
        {
            var acc = await db.Accounts.Include(a => a.Roles).FirstAsync(a => a.Id == ctx.User.AccountId());
            if (req.DisplayName != null) acc.DisplayName = ValidateDisplayName(req.DisplayName);
            if (req.Language != null) acc.Language = Languages.Contains(req.Language) ? req.Language : throw ApiException.BadRequest("unsupported_language");
            if (req.AvatarId != null) acc.AvatarId = Cosmetic(req.AvatarId);
            if (req.AvatarFrameId != null) acc.AvatarFrameId = Cosmetic(req.AvatarFrameId);
            if (req.TitleId != null) acc.TitleId = Cosmetic(req.TitleId);
            await db.SaveChangesAsync();
            return ToProfile(acc);
        });
    }

    private static string Cosmetic(string id) =>
        System.Text.RegularExpressions.Regex.IsMatch(id, "^[a-z0-9_]{1,48}$") ? id : throw ApiException.BadRequest("invalid_cosmetic");

    public static ProfileDto ToProfile(Account a) => new()
    {
        Uid = a.Id, Email = a.Email, EmailVerified = a.EmailVerified, DisplayName = a.DisplayName, Language = a.Language,
        AvatarId = a.AvatarId, AvatarFrameId = a.AvatarFrameId, TitleId = a.TitleId, Roles = a.Roles.Select(r => r.Role).ToList(),
    };

    private static void EnsureNotBanned(Account acc)
    {
        if (acc.Status == AccountStatus.Banned && (acc.BannedUntil == null || acc.BannedUntil > DateTimeOffset.UtcNow))
            throw new ApiException(403, "account_banned", acc.BanReason);
    }

    public static async Task<AuthResponse> IssueSession(PetStarDb db, TokenService tokens, Account acc, ServerMembership? m,
        HttpContext ctx, LoginRequest? meta)
    {
        var refresh = SecureRandomSource.Token(48);
        var now = DateTimeOffset.UtcNow;
        var s = new Session
        {
            Id = Guid.NewGuid(), AccountId = acc.Id, RefreshTokenHash = SecureRandomSource.Sha256(refresh), CreatedAt = now,
            LastUsedAt = now, ExpiresAt = now.AddDays(tokens.RefreshDays), Platform = Trim(meta?.Platform, 16),
            DeviceName = Trim(meta?.DeviceName, 64), Ip = ctx.Connection.RemoteIpAddress?.ToString(),
        };
        db.Sessions.Add(s);
        await db.SaveChangesAsync();
        var (token, exp) = tokens.CreateAccessToken(acc, s.Id, m);
        return new AuthResponse { Uid = acc.Id, AccessToken = token, AccessTokenExpiresAt = exp, RefreshToken = refresh, EmailVerified = acc.EmailVerified, ServerId = m?.ServerId };
    }

    private static string? Trim(string? s, int n) => s == null ? null : s.Length <= n ? s : s[..n];

    /// <summary>Public web origin used in e-mail links (App:WebBaseUrl, else the first CORS origin). Set at startup.</summary>
    public static string? WebBaseUrl;

    /// <summary>Bilingual body with an HTTPS link for the web client, the app deep link and the raw code.
    /// The last line stays "Token: ..." (support staff and tests read it).</summary>
    public static string EmailBody(string purpose, string token, string? web)
    {
        bool reset = purpose == "reset";
        string q = (reset ? "reset" : "verify") + "=" + Uri.EscapeDataString(token);
        string link = string.IsNullOrEmpty(web) ? "" : $"{web.TrimEnd('/')}/?{q}";
        var vi = reset ? "Mở liên kết dưới đây để đặt lại mật khẩu Pet Star (hết hạn sau 1 giờ):" : "Mở liên kết dưới đây để xác thực email Pet Star (hết hạn sau 48 giờ):";
        var en = reset ? "Open the link below to reset your Pet Star password (expires in 1 hour):" : "Open the link below to verify your Pet Star email (expires in 48 hours):";
        var ignore = "Nếu không phải bạn yêu cầu, hãy bỏ qua email này. / If you did not request this, ignore this e-mail.";
        return $"{vi}\n{link}\n\n{en}\n{link}\n\n{ignore}\n\nApp: petstar://auth/{purpose}?token={token}\nToken: {token}";
    }

    private static async Task SendEmailToken(PetStarDb db, IEmailSender mail, Account acc, string purpose)
    {
        var token = SecureRandomSource.Token(32);
        db.EmailTokens.Add(new EmailToken
        {
            Id = Guid.NewGuid(), AccountId = acc.Id, Purpose = purpose, TokenHash = SecureRandomSource.Sha256(token),
            CreatedAt = DateTimeOffset.UtcNow, ExpiresAt = DateTimeOffset.UtcNow.AddHours(purpose == "reset" ? 1 : 48),
        });
        await db.SaveChangesAsync();
        await mail.SendAsync(acc.Email, purpose == "reset" ? "email.reset" : "email.verify", EmailBody(purpose, token, WebBaseUrl));
    }

    private static async Task<EmailToken> ConsumeEmailToken(PetStarDb db, string token, string purpose)
    {
        var hash = SecureRandomSource.Sha256(token ?? "");
        var now = DateTimeOffset.UtcNow;
        var t = await db.EmailTokens.FirstOrDefaultAsync(x => x.TokenHash == hash && x.Purpose == purpose);
        if (t == null || t.UsedAt != null || t.ExpiresAt < now) throw ApiException.BadRequest("invalid_or_expired_token");
        // single use, enforced atomically
        int n = await db.EmailTokens.Where(x => x.Id == t.Id && x.UsedAt == null).ExecuteUpdateAsync(u => u.SetProperty(x => x.UsedAt, now));
        if (n == 0) throw ApiException.BadRequest("invalid_or_expired_token");
        return t;
    }

    private static string ValidateEmail(string? email)
    {
        email = (email ?? "").Trim();
        if (email.Length > 254 || !MailAddress.TryCreate(email, out var addr) || addr.Address != email) throw ApiException.BadRequest("invalid_email");
        return email;
    }

    private static void ValidatePassword(string? pw)
    {
        if (pw == null || pw.Length < 8 || pw.Length > 128) throw ApiException.BadRequest("weak_password", "8-128 characters");
        if (!pw.Any(char.IsLetter) || !pw.Any(char.IsDigit)) throw ApiException.BadRequest("weak_password", "letters and digits required");
    }

    public static string ValidateDisplayName(string? n)
    {
        n = (n ?? "").Trim();
        if (n.Length < 3 || n.Length > 24 || n.Any(char.IsControl)) throw ApiException.BadRequest("invalid_display_name");
        return n;
    }
}
