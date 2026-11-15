using Microsoft.EntityFrameworkCore;
using PetStar.Api.Data;
using PetStar.Api.Infrastructure;
using PetStar.Contracts;
using PetStar.Rules;

namespace PetStar.Api.Features;

public static class DeckEndpoints
{
    private const int MaxDecks = 10;

    public static void Map(WebApplication app)
    {
        var g = ServerEndpoints.GameGroup(app);

        g.MapGet("/decks", async (HttpContext ctx, PetStarDb db, GameData data) =>
        {
            var (_, mid) = ctx.User.Player();
            var decks = await db.Decks.AsNoTracking().Include(d => d.Slots).Where(d => d.MembershipId == mid).OrderBy(d => d.Id).ToListAsync();
            var ids = decks.SelectMany(d => d.Slots.Select(s => s.CardId)).Distinct().ToList();
            var cards = await db.Cards.AsNoTracking().Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id);
            return decks.Select(d => ToDto(d, cards, data));
        });

        g.MapPost("/decks", async (SaveDeckRequest req, HttpContext ctx, PetStarDb db, GameData data) =>
        {
            var (sid, mid) = ctx.User.Player();
            if (await db.Decks.CountAsync(d => d.MembershipId == mid) >= MaxDecks) throw ApiException.Conflict("deck_limit_reached");
            var deck = new Deck { MembershipId = mid, ServerId = sid };
            db.Decks.Add(deck);
            return await Save(db, data, deck, req, mid, sid);
        });

        g.MapPut("/decks/{id:long}", async (long id, SaveDeckRequest req, HttpContext ctx, PetStarDb db, GameData data) =>
        {
            var (sid, mid) = ctx.User.Player();
            var deck = await db.Decks.Include(d => d.Slots).FirstOrDefaultAsync(d => d.Id == id && d.MembershipId == mid) ?? throw ApiException.NotFound("deck_not_found");
            db.DeckSlots.RemoveRange(deck.Slots);
            deck.Slots.Clear();
            return await Save(db, data, deck, req, mid, sid);
        });

        g.MapPost("/decks/{id:long}/default", async (long id, HttpContext ctx, PetStarDb db) =>
        {
            var (_, mid) = ctx.User.Player();
            await using var tx = await db.Database.BeginTransactionAsync();
            await db.Decks.Where(d => d.MembershipId == mid && d.IsDefault).ExecuteUpdateAsync(u => u.SetProperty(d => d.IsDefault, false));
            int n = await db.Decks.Where(d => d.Id == id && d.MembershipId == mid).ExecuteUpdateAsync(u => u.SetProperty(d => d.IsDefault, true));
            if (n == 0) throw ApiException.NotFound("deck_not_found");
            await tx.CommitAsync();
            return Results.Ok(new { defaultDeckId = id });
        });

        g.MapDelete("/decks/{id:long}", async (long id, HttpContext ctx, PetStarDb db) =>
        {
            var (_, mid) = ctx.User.Player();
            int n = await db.Decks.Where(d => d.Id == id && d.MembershipId == mid).ExecuteDeleteAsync();
            return n == 0 ? throw ApiException.NotFound("deck_not_found") : Results.Ok(new { deleted = true });
        });
    }

    private static async Task<DeckDto> Save(PetStarDb db, GameData data, Deck deck, SaveDeckRequest req, long mid, int sid)
    {
        var name = (req.Name ?? "").Trim();
        if (name.Length < 1 || name.Length > 32) throw ApiException.BadRequest("invalid_deck_name");
        var ids = req.Slots.Select(s => s.CardId).ToList();
        var cards = await db.Cards.AsNoTracking().Where(c => ids.Contains(c.Id) && c.OwnerMembershipId == mid && c.ServerId == sid).ToDictionaryAsync(c => c.Id);
        if (cards.Count != ids.Distinct().Count()) throw ApiException.BadRequest("deck_card_not_owned");
        var err = DeckRules.Validate(data.Catalog, req.Slots.Select(s => new DeckCardInfo { CardId = s.CardId, Position = s.Position, FormId = cards.TryGetValue(s.CardId, out var c) ? c.FormId : "" }).ToList());
        if (err != null) throw ApiException.BadRequest(err);
        await using var tx = await db.Database.BeginTransactionAsync();
        if (req.IsDefault)
            await db.Decks.Where(d => d.MembershipId == mid && d.IsDefault && d.Id != deck.Id).ExecuteUpdateAsync(u => u.SetProperty(d => d.IsDefault, false));
        deck.Name = name;
        deck.IsDefault = req.IsDefault || !await db.Decks.AnyAsync(d => d.MembershipId == mid && d.IsDefault && d.Id != deck.Id);
        deck.UpdatedAt = DateTimeOffset.UtcNow;
        foreach (var s in req.Slots) deck.Slots.Add(new DeckSlot { Position = s.Position, CardId = s.CardId });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return ToDto(deck, cards, data);
    }

    public static DeckDto ToDto(Deck d, Dictionary<long, CardInstance> cards, GameData data) => new()
    {
        Id = d.Id, Name = d.Name, IsDefault = d.IsDefault,
        Slots = d.Slots.OrderBy(s => s.Position).Select(s => new DeckSlotDto { Position = s.Position, CardId = s.CardId }).ToList(),
        TotalPower = d.Slots.Sum(s => cards.TryGetValue(s.CardId, out var c) ? data.ToDto(c).Power : 0),
    };
}
