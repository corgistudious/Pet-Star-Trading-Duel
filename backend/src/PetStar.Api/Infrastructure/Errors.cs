using Microsoft.AspNetCore.Diagnostics;

namespace PetStar.Api.Infrastructure;

/// <summary>Business error with a stable machine code (client maps codes to localized text).</summary>
public sealed class ApiException(int status, string code, string? detail = null) : Exception(detail ?? code)
{
    public int Status { get; } = status;
    public string Code { get; } = code;

    public static ApiException BadRequest(string code, string? detail = null) => new(400, code, detail);
    public static ApiException NotFound(string code = "not_found") => new(404, code);
    public static ApiException Conflict(string code, string? detail = null) => new(409, code, detail);
    public static ApiException Forbidden(string code = "forbidden") => new(403, code);
    public static ApiException Unprocessable(string code, string? detail = null) => new(422, code, detail);
}

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> log) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken ct)
    {
        int status; string code; string? detail;
        switch (ex)
        {
            case ApiException a: status = a.Status; code = a.Code; detail = a.Message; break;
            case Rules.Battle.BattleRuleException b: status = 422; code = b.Code; detail = b.Message; break;
            case BadHttpRequestException: status = 400; code = "bad_request"; detail = null; break;
            default:
                log.LogError(ex, "Unhandled error on {Path}", ctx.Request.Path);
                status = 500; code = "internal_error"; detail = null; break;
        }
        ctx.Response.StatusCode = status;
        await ctx.Response.WriteAsJsonAsync(new ErrorBody(code, detail, ctx.TraceIdentifier), ct);
        return true;
    }
}

public sealed record ErrorBody(string Code, string? Detail, string TraceId);
