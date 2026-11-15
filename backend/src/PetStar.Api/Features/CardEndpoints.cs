using Microsoft.EntityFrameworkCore;
using PetStar.Api.Data;
using PetStar.Api.Infrastructure;
using PetStar.Contracts;
using PetStar.Rules;

namespace PetStar.Api.Features;

public static class CardEndpoints
{
    public static void Map(WebApplication app)
    {
        var g = ServerEndpoints.GameGroup(app);

        // Pet Home
        g.MapGet("/cards", async (string? rarity, string? element, string? quality, int? stage, bool? favorite, string? sort,
            int? page, int? pageSize, HttpContext ctx, PetStarDb db, GameData data) =>
        {
            var (sid, mid) = ctx.User.Player();
            var cat = data.Catalog;
            var q = db.Cards.AsNoTracking().Where(c => c.OwnerMembershipId == mid && c.ServerId == sid);
            if (!string.IsNullOrEmpty(rarity)) { var set = rarity.Split(','); q = q.Where(c => set.Contains(c.Rarity)); }
            if (!string.IsNullOrEmpty(quality)) { var set = quality.Split(','); q = q.Where(c => set.Contains(c.Quality)); }
            if (favorite == true) q = q.Where(c => c.IsFavorite);
            if (!string.IsNullOrEmpty(element) || stage.HasValue)
            {
                var els = element?.Split(',');
                var forms = cat.Forms.Values.Where(f => (els == null || els.Contains(f.Element)) && (!stage.HasValue || f.Stage == stage)).Select(f => f.Id).ToList();
                q = q.Where(c => forms.Contains(c.FormId));
            }
            var all = (await q.ToListAsync()).Select(c => data.ToDto(c)).ToList();
            var rarityOrder = cat.Rarities.ToDictionary(r => r.Id, r => r.Order);
            IEnumerable<CardDto> sorted = (sort ?? "newest") switch
            {
                "power" => all.OrderByDescending(c => c.Power),
                "level" => all.OrderByDescending(c => c.Level).ThenByDescending(c => c.Power),
                "rarity" => all.OrderByDescending(c => rarityOrder[c.Rarity]).ThenByDescending(c => c.Power),
                "name" => all.OrderBy(c => c.Name, StringComparer.Ordinal),
                _ => all.OrderByDescending(c => c.Id),
            };
            int ps = Math.Clamp(pageSize ?? 60, 1, 200), pg = Math.Max(1, page ?? 1);
            var copies = await db.Cards.AsNoTracking().Where(c => c.OwnerMembershipId == mid && c.ServerId == sid)
                .GroupBy(c => c.FormId).Select(x => new { x.Key, N = x.Count() }).ToDictionaryAsync(x => x.Key, x => x.N);
            return new CardPageDto { Items = sorted.Skip((pg - 1) * ps).Take(ps).ToList(), Total = all.Count, Page = pg, PageSize = ps, CopiesByForm = copies };
        });

        g.MapGet("/cards/{id:long}", async (long id, HttpContext ctx, PetStarDb db, GameData data) =>
            data.ToDto(await Owned(db, ctx, id)));

        g.MapPost("/cards/{id:long}/favorite", async (long id, FlagRequest req, HttpContext ctx, PetStarDb db, GameData data) =>
        {
            var c = await Owned(db, ctx, id, track: true);
            c.IsFavorite = req.Value; c.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            return data.ToDto(c);
        });

        g.MapPost("/cards/{id:long}/lock", async (long id, FlagRequest req, HttpContext ctx, PetStarDb db, GameData data) =>
        {
            var c = await Owned(db, ctx, id, track: true);
            c.IsLocked = req.Value; c.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            return data.ToDto(c);
        });

        g.MapGet("/cards/{id:long}/evolution-preview", async (long id, HttpContext ctx, PetStarDb db, GameData data, EconomyService eco) =>
        {
            var (_, mid) = ctx.User.Player();
            var c = await Owned(db, ctx, id);
            var form = data.Catalog.Form(c.FormId);
            var next = data.Catalog.NextForm(form);
            var essenceId = EvolutionRules.EssenceItemId(form.Element);
            long essence = await db.Items.Where(i => i.MembershipId == mid && i.ItemId == essenceId).Select(i => i.Quantity).FirstOrDefaultAsync();
            long beans = (await eco.Balances(mid))[Currency.MiniGreenBean];
            var step = next != null ? data.Catalog.EvolutionRules.ForStage(form.Stage) : new EvolutionStep();
            CardDto? nextDto = null;
            if (next != null)
            {
                var preview = new CardInstance { Id = c.Id, FormId = next.Id, Rarity = c.Rarity, Quality = c.Quality, Finish = c.Finish, Level = c.Level, Exp = c.Exp };
                nextDto = data.ToDto(preview);
            }
            return new EvolutionPreviewDto
            {
                Current = data.ToDto(c), Next = nextDto,
                Check = EvolutionRules.Check(data.Catalog, c.FormId, c.Level, essence, beans, c.BusyState != null).ToString(),
                RequiredLevel = step.MinLevel, EssenceItemId = essenceId, EssenceRequired = step.EssenceAmount, EssenceOwned = essence,
                BeansRequired = step.Beans, BeansOwned = beans,
            };
        });

        // Server-side evolution: same Card Instance ID, materials consumed exactly once (ledger ref = card + source form),
        // rarity unchanged, history + codex updated.
        g.MapPost("/cards/{id:long}/evolve", (long id, HttpContext ctx, PetStarDb db, GameData data, EconomyService eco, IdempotencyService idem) =>
            idem.Run(ctx, "cards.evolve", new { id }, async () =>
            {
                var (sid, mid) = ctx.User.Player();
                var c = await db.Cards.FromSqlRaw("SELECT * FROM card_instances WHERE id = {0} AND owner_membership_id = {1} AND server_id = {2} FOR UPDATE", id, mid, sid)
                    .FirstOrDefaultAsync() ?? throw ApiException.NotFound("card_not_found");
                var form = data.Catalog.Form(c.FormId);
                var next = data.Catalog.NextForm(form);
                var essenceId = EvolutionRules.EssenceItemId(form.Element);
                long essence = await db.Items.Where(i => i.MembershipId == mid && i.ItemId == essenceId).Select(i => i.Quantity).FirstOrDefaultAsync();
                long beans = (await eco.Balances(mid))[Currency.MiniGreenBean];
                var check = EvolutionRules.Check(data.Catalog, c.FormId, c.Level, essence, beans, c.BusyState != null);
                if (check != EvolutionCheck.Ok) throw ApiException.Conflict("evolution_" + check.ToString().ToLowerInvariant());
                var step = data.Catalog.EvolutionRules.ForStage(form.Stage);
                string refId = $"card:{c.Id}:{form.Id}";
                await eco.Item(mid, sid, essenceId, -step.EssenceAmount, "evolution", refId);
                await eco.Currency(mid, sid, Currency.MiniGreenBean, -step.Beans, "evolution", refId);
                var now = DateTimeOffset.UtcNow;
                db.CardEvolutions.Add(new CardEvolutionHistory
                {
                    CardId = c.Id, MembershipId = mid, FromFormId = form.Id, ToFormId = next!.Id, At = now,
                    CostJson = System.Text.Json.JsonSerializer.Serialize(new { essence = new { id = essenceId, amount = step.EssenceAmount }, beans = step.Beans }),
                });
                c.FormId = next.Id; c.UpdatedAt = now;
                await db.SaveChangesAsync();
                await GameData.DiscoverCodex(db, mid, next.Id, c.Id);
                return new { card = data.ToDto(c), fromFormId = form.Id, currencies = await eco.Balances(mid) };
            }));

        g.MapGet("/codex", async (HttpContext ctx, PetStarDb db, GameData data) =>
        {
            var (sid, mid) = ctx.User.Player();
            var found = await db.Codex.AsNoTracking().Where(x => x.MembershipId == mid).ToDictionaryAsync(x => x.FormId);
            var owned = await db.Cards.AsNoTracking().Where(c => c.OwnerMembershipId == mid && c.ServerId == sid)
                .GroupBy(c => c.FormId).Select(x => new { x.Key, N = x.Count() }).ToDictionaryAsync(x => x.Key, x => x.N);
            var entries = data.Catalog.Families.SelectMany(f => f.Forms).Select(f => new CodexEntryDto
            {
                FormId = f.Id, FamilyId = f.FamilyId, Stage = f.Stage, Discovered = found.ContainsKey(f.Id),
                DiscoveredAt = found.TryGetValue(f.Id, out var e) ? e.DiscoveredAt : null, Owned = owned.GetValueOrDefault(f.Id),
            }).ToList();
            return new CodexDto { Discovered = found.Count, Total = entries.Count, Completion = entries.Count == 0 ? 0 : Math.Round(100.0 * found.Count / entries.Count, 2), Entries = entries };
        });
    }

    private static async Task<CardInstance> Owned(PetStarDb db, HttpContext ctx, long id, bool track = false)
    {
        var (sid, mid) = ctx.User.Player();
        var q = track ? db.Cards : db.Cards.AsNoTracking();
        return await q.FirstOrDefaultAsync(c => c.Id == id && c.OwnerMembershipId == mid && c.ServerId == sid) ?? throw ApiException.NotFound("card_not_found");
    }
}
