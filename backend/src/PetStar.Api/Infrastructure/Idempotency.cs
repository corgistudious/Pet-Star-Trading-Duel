using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PetStar.Api.Data;

namespace PetStar.Api.Infrastructure;

/// <summary>
/// Runs a mutating operation exactly once per (account, Idempotency-Key). The key record is inserted inside the
/// same DB transaction as the business change, so: retries after a lost response return the stored result,
/// concurrent duplicates block on the unique key and then return the stored result, and a failed operation
/// leaves no record (the client may retry safely).
/// </summary>
public sealed partial class IdempotencyService(PetStarDb db)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [GeneratedRegex("^[A-Za-z0-9_-]{8,64}$")]
    private static partial Regex KeyFormat();

    public async Task<IResult> Run<T>(HttpContext ctx, string endpoint, object request, Func<Task<T>> work)
    {
        var key = ctx.Request.Headers["Idempotency-Key"].ToString();
        if (!KeyFormat().IsMatch(key)) throw ApiException.BadRequest("idempotency_key_required");
        long accountId = ctx.User.AccountId();
        string hash = SecureRandomSource.Sha256(endpoint + "|" + JsonSerializer.Serialize(request, Json));

        await using var tx = await db.Database.BeginTransactionAsync();
        int inserted = await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO idempotency_records (account_id, key, endpoint, request_hash, status_code, created_at) " +
            "VALUES ({0}, {1}, {2}, {3}, 0, now()) ON CONFLICT DO NOTHING", accountId, key, endpoint, hash);
        if (inserted == 0)
        {
            await tx.RollbackAsync();
            var rec = await db.Idempotency.AsNoTracking().SingleAsync(r => r.AccountId == accountId && r.Key == key);
            if (rec.Endpoint != endpoint || rec.RequestHash != hash) throw ApiException.Unprocessable("idempotency_key_reused");
            ctx.Response.Headers["Idempotent-Replay"] = "true";
            return Results.Content(rec.ResponseJson ?? "{}", "application/json", null, rec.StatusCode);
        }
        try
        {
            var result = await work();
            var json = JsonSerializer.Serialize(result, Json);
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE idempotency_records SET status_code = 200, response_json = {0}::jsonb WHERE account_id = {1} AND key = {2}",
                json, accountId, key);
            await tx.CommitAsync();
            return Results.Content(json, "application/json", null, 200);
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }
}

public static class ClaimsExtensions
{
    public const string ServerClaim = "srv";
    public const string MembershipClaim = "mid";
    public const string SessionClaim = "sid";

    public static long AccountId(this ClaimsPrincipal u) =>
        long.Parse(u.FindFirstValue(ClaimTypes.NameIdentifier) ?? u.FindFirstValue("sub") ?? throw ApiException.Forbidden("unauthenticated"));

    public static Guid SessionId(this ClaimsPrincipal u) => Guid.Parse(u.FindFirstValue(SessionClaim)!);

    public static (int serverId, long membershipId) Player(this ClaimsPrincipal u)
    {
        var s = u.FindFirstValue(ServerClaim);
        var m = u.FindFirstValue(MembershipClaim);
        if (s == null || m == null) throw ApiException.Forbidden("server_not_selected");
        return (int.Parse(s), long.Parse(m));
    }
}
