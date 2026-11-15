using PetStar.Api.Auth;
using PetStar.Api.Infrastructure;

namespace PetStar.Api.Features;

/// <summary>Daily (00:00 UTC) and weekly (Monday 00:00 UTC) missions. Progress is derived server-side.</summary>
public static class MissionEndpoints
{
    public static void Map(WebApplication app)
    {
        var g = ServerEndpoints.GameGroup(app);

        g.MapGet("/missions", async (HttpContext ctx, MissionService missions) =>
        {
            var (_, mid) = ctx.User.Player();
            var now = DateTimeOffset.UtcNow;
            var (daily, weekly) = await missions.View(mid, now);
            return new { serverTime = now, daily, weekly };
        });

        g.MapPost("/missions/{id}/claim", async (string id, HttpContext ctx, MissionService missions, EconomyService eco, CardFactory factory, GameData data) =>
        {
            var (sid, mid) = ctx.User.Player();
            var view = await missions.Claim(eco, factory, data, mid, sid, id, DateTimeOffset.UtcNow);
            return Results.Ok(new { mission = view, currencies = await eco.Balances(mid) });
        });
    }
}
