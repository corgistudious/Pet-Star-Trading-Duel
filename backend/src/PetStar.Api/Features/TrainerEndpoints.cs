using Microsoft.EntityFrameworkCore;
using PetStar.Api.Data;
using PetStar.Api.Infrastructure;

namespace PetStar.Api.Features;

public sealed class ChooseTrainerRequest { public string CharacterId { get; set; } = ""; }
public sealed class CompanionRequest { public long? CardId { get; set; } }
public sealed class OutfitRequest { public string OutfitId { get; set; } = ""; }

/// <summary>Trainer character (male/female chibi), outfit and companion Pet. The character is chosen once at first
/// login; changing it is not offered until a policy is decided (trainers.json → changePolicy). Outfits are cosmetic only.</summary>
public static class TrainerEndpoints
{
    public static void Map(WebApplication app)
    {
        var g = ServerEndpoints.GameGroup(app);

        g.MapGet("/trainer/catalog", (TeamRulesProvider rp) => rp.Trainers);

        g.MapGet("/trainer", async (HttpContext ctx, PetStarDb db) =>
        {
            var (_, mid) = ctx.User.Player();
            var p = await db.TrainerProfiles.AsNoTracking().FirstOrDefaultAsync(x => x.MembershipId == mid);
            if (p == null) return Results.Ok(new { chosen = false });
            var owned = await db.TrainerOutfits.AsNoTracking().Where(o => o.MembershipId == mid).Select(o => o.OutfitId).ToListAsync();
            var companion = p.CompanionCardId is long cid ? await db.Cards.AsNoTracking().Where(c => c.Id == cid && c.OwnerMembershipId == mid).Select(c => new { c.Id, c.FormId, c.Rarity, c.Level }).FirstOrDefaultAsync() : null;
            return Results.Ok(new { chosen = true, p.CharacterId, p.AppearanceId, p.OutfitId, ownedOutfits = owned, companion });
        });

        g.MapPost("/trainer", async (ChooseTrainerRequest req, HttpContext ctx, PetStarDb db, TeamRulesProvider rp) =>
        {
            var (sid, mid) = ctx.User.Player();
            var ch = rp.Trainers.Characters.FirstOrDefault(c => c.Id == req.CharacterId) ?? throw ApiException.BadRequest("unknown_character");
            await using var tx = await db.Database.BeginTransactionAsync();
            if (await db.TrainerProfiles.AnyAsync(x => x.MembershipId == mid)) throw ApiException.Conflict("trainer_already_chosen");
            var now = DateTimeOffset.UtcNow;
            db.TrainerProfiles.Add(new TrainerProfile { MembershipId = mid, ServerId = sid, CharacterId = ch.Id, AppearanceId = ch.DefaultAppearance, OutfitId = ch.DefaultOutfit, CreatedAt = now, UpdatedAt = now });
            db.TrainerOutfits.Add(new TrainerOutfitOwned { MembershipId = mid, OutfitId = ch.DefaultOutfit, Source = "default", AcquiredAt = now });
            try { await db.SaveChangesAsync(); } catch (DbUpdateException) { throw ApiException.Conflict("trainer_already_chosen"); }
            await tx.CommitAsync();
            return Results.Ok(new { chosen = true, characterId = ch.Id, appearanceId = ch.DefaultAppearance, outfitId = ch.DefaultOutfit });
        });

        g.MapPut("/trainer/companion", async (CompanionRequest req, HttpContext ctx, PetStarDb db) =>
        {
            var (sid, mid) = ctx.User.Player();
            var p = await db.TrainerProfiles.FirstOrDefaultAsync(x => x.MembershipId == mid) ?? throw ApiException.Conflict("trainer_not_chosen");
            if (req.CardId is long cid && !await db.Cards.AnyAsync(c => c.Id == cid && c.OwnerMembershipId == mid && c.ServerId == sid)) throw ApiException.Conflict("card_not_owned");
            p.CompanionCardId = req.CardId; p.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(new { companionCardId = p.CompanionCardId });
        });

        g.MapPut("/trainer/outfit", async (OutfitRequest req, HttpContext ctx, PetStarDb db, TeamRulesProvider rp) =>
        {
            var (_, mid) = ctx.User.Player();
            var p = await db.TrainerProfiles.FirstOrDefaultAsync(x => x.MembershipId == mid) ?? throw ApiException.Conflict("trainer_not_chosen");
            var o = rp.Trainers.Outfits.FirstOrDefault(x => x.Id == req.OutfitId) ?? throw ApiException.BadRequest("unknown_outfit");
            if (o.CharacterId != "*" && o.CharacterId != p.CharacterId) throw ApiException.Conflict("outfit_wrong_character");
            if (!await db.TrainerOutfits.AnyAsync(x => x.MembershipId == mid && x.OutfitId == o.Id)) throw ApiException.Conflict("outfit_not_owned");
            p.OutfitId = o.Id; p.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(new { outfitId = p.OutfitId });
        });
    }
}
