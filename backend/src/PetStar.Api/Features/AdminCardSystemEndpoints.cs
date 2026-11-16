using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PetStar.Api.Auth;
using PetStar.Api.Data;
using PetStar.Api.Infrastructure;
using PetStar.Rules;

namespace PetStar.Api.Features;

public sealed class AdminPackPatch
{
    public int? PriceAmount { get; set; }
    public bool? Enabled { get; set; }
    public DateTimeOffset? AvailableFrom { get; set; }
    public DateTimeOffset? AvailableUntil { get; set; }
    public string Reason { get; set; } = "";
}

public sealed class AdminPackOddsRequest
{
    public Dictionary<string, int> SlotTable { get; set; } = new();
    public List<string>? DivinePool { get; set; }
    public string Reason { get; set; } = "";
}

public sealed class AdminEventRequest
{
    public string PackTypeId { get; set; } = "";
    public int? ServerId { get; set; }
    public string NameVi { get; set; } = "";
    public string NameEn { get; set; } = "";
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public int? TotalLimit { get; set; }
    public int? PerPlayerLimit { get; set; }
}

public sealed class AdminEventPatch
{
    public DateTimeOffset? EndsAt { get; set; }
    public int? TotalLimit { get; set; }
    public int? PerPlayerLimit { get; set; }
    public bool? Cancelled { get; set; }
    public string Reason { get; set; } = "";
}

public sealed class AdminInviteRequest
{
    public long Uid { get; set; }
    public int ServerId { get; set; }
    public int Days { get; set; } = 7;
    public int MaxPacks { get; set; } = 1;
    public string Note { get; set; } = "";
}

public sealed class AdminRevokeRequest { public string Reason { get; set; } = ""; }

public sealed class AdminGrantRequest
{
    public long Uid { get; set; }
    public int ServerId { get; set; }
    public string ItemId { get; set; } = "";
    public long Amount { get; set; }
    public string Reason { get; set; } = "";
}

/// <summary>
/// Card system administration. RBAC (checked by the backend, every change audited in admin_audit_logs):
///  gm:    read-only views — packs, odds, openings, issued cards, Divine Pets, invitations, Rainbow Star flows, anomalies
///  admin: pack price / sale status, limited events, Star invitations (issue / revoke)
///  owner: odds tables and divine pools (versioned), material grants, VIP point grants
/// Nobody can grant roles here, mint cards, or skip validation (odds must sum to 100 %, pools must be valid).
/// </summary>
public static class AdminCardSystemEndpoints
{
    public static void Map(WebApplication app)
    {
        var gm = app.MapGroup("/api/admin/cardsys").RequireAuthorization("GameMaster").RequireRateLimiting("user");
        var admin = app.MapGroup("/api/admin/cardsys").RequireAuthorization("ServerAdmin").RequireRateLimiting("user");
        var owner = app.MapGroup("/api/admin/cardsys").RequireAuthorization("Owner").RequireRateLimiting("user");

        // ---------------------------------------------------------------- packs

        gm.MapGet("/packs", async (PetStarDb db, GameData data) =>
        {
            var rows = await db.PackTypes.AsNoTracking().ToListAsync();
            var sold = await db.PackPurchases.AsNoTracking().GroupBy(p => p.PackTypeId)
                .Select(g => new { g.Key, Qty = g.Sum(x => x.Quantity), Amount = g.Sum(x => x.Amount) }).ToDictionaryAsync(x => x.Key);
            var opened = await db.PackOpenings.AsNoTracking().GroupBy(o => o.PackTypeId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N);
            var list = new List<object>();
            foreach (var r in rows)
            {
                var (p, ver) = await data.Pack(db, r.Id);
                list.Add(new
                {
                    p.Id, p.Order, p.Theme, p.Names, p.Price, Enabled = r.Enabled, Version = ver, p.AvailableFrom, p.AvailableUntil, p.Requirements,
                    Odds = PackEndpoints.Odds(data.Catalog, p, ver),
                    SoldPacks = sold.TryGetValue(r.Id, out var s) ? s.Qty : 0, Revenue = s?.Amount ?? 0, Opened = opened.GetValueOrDefault(r.Id),
                });
            }
            return list;
        });

        gm.MapGet("/packs/{id}/versions", async (string id, PetStarDb db) =>
            await db.PackTypeVersions.AsNoTracking().Where(v => v.PackTypeId == id).OrderByDescending(v => v.Version)
                .Select(v => new { v.Version, v.Enabled, v.Reason, v.ChangedByAccountId, v.CreatedAt, v.DataJson }).ToListAsync());

        admin.MapPatch("/packs/{id}", async (string id, AdminPackPatch req, HttpContext ctx, PetStarDb db, GameData data, AuditService audit) =>
        {
            Reason(req.Reason);
            if (req.PriceAmount is int price && (price < 1 || price > 1_000_000)) throw ApiException.BadRequest("invalid_price");
            return await SaveVersion(db, data, audit, ctx, id, "cardsys.pack.update", req.Reason, p =>
            {
                if (req.PriceAmount is int pr) p.Price.Amount = pr;
                if (req.Enabled is bool en) p.Enabled = en;
                if (req.AvailableFrom != null) p.AvailableFrom = req.AvailableFrom;
                if (req.AvailableUntil != null) p.AvailableUntil = req.AvailableUntil;
            }, req);
        });

        owner.MapPut("/packs/{id}/odds", async (string id, AdminPackOddsRequest req, HttpContext ctx, PetStarDb db, GameData data, AuditService audit) =>
        {
            Reason(req.Reason);
            return await SaveVersion(db, data, audit, ctx, id, "cardsys.pack.odds", req.Reason, p =>
            {
                p.SlotTable = new Dictionary<string, int>(req.SlotTable);
                p.FinalSlotTable = new Dictionary<string, int>();
                if (req.DivinePool != null) p.DivinePool = req.DivinePool.Distinct().ToList();
            }, req);
        });

        gm.MapGet("/openings", async (int serverId, string? packTypeId, long? uid, int? limit, PetStarDb db) =>
        {
            var q = db.PackOpenings.AsNoTracking().Include(o => o.Results).Where(o => o.ServerId == serverId);
            if (!string.IsNullOrEmpty(packTypeId)) q = q.Where(o => o.PackTypeId == packTypeId);
            if (uid != null)
            {
                var mid = await db.Memberships.Where(m => m.AccountId == uid && m.ServerId == serverId).Select(m => (long?)m.Id).FirstOrDefaultAsync();
                q = q.Where(o => o.MembershipId == mid);
            }
            return await q.OrderByDescending(o => o.CreatedAt).Take(Math.Clamp(limit ?? 100, 1, 500))
                .Select(o => new { o.Id, o.MembershipId, o.PackTypeId, o.PackConfigVersion, o.CreatedAt, results = o.Results.OrderBy(r => r.Slot).Select(r => new { r.Slot, r.CardId, r.FormId, r.Rarity, r.Quality, r.Finish }) })
                .ToListAsync();
        });

        // issued cards: observed vs configured rates per pack (detects misconfiguration or tampering)
        gm.MapGet("/issued", async (int serverId, PetStarDb db, GameData data) =>
        {
            var rows = await db.PackOpeningResults.AsNoTracking()
                .Join(db.PackOpenings.AsNoTracking(), r => r.OpeningId, o => o.Id, (r, o) => new { o.ServerId, o.PackTypeId, o.PackConfigVersion, r.Rarity })
                .Where(x => x.ServerId == serverId)
                .GroupBy(x => new { x.PackTypeId, x.PackConfigVersion, x.Rarity }).Select(g => new { g.Key.PackTypeId, g.Key.PackConfigVersion, g.Key.Rarity, N = g.Count() })
                .ToListAsync();
            var divine = await db.Cards.AsNoTracking().Where(c => c.ServerId == serverId && c.Rarity == RarityDef.Divine)
                .GroupBy(c => c.FormId).Select(g => new { FormId = g.Key, N = g.Count() }).ToListAsync();
            return new
            {
                byPack = rows.GroupBy(r => new { r.PackTypeId, r.PackConfigVersion }).Select(g => new
                {
                    g.Key.PackTypeId, Version = g.Key.PackConfigVersion, Cards = g.Sum(x => x.N),
                    Observed = g.ToDictionary(x => x.Rarity, x => Math.Round(100.0 * x.N / g.Sum(y => y.N), 4)),
                }),
                divine,
                totalCards = await db.Cards.CountAsync(c => c.ServerId == serverId),
            };
        });

        gm.MapGet("/divine", async (int serverId, PetStarDb db, GameData data) =>
        {
            var counts = await db.Cards.AsNoTracking().Where(c => c.ServerId == serverId && c.Rarity == RarityDef.Divine)
                .GroupBy(c => c.FormId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N);
            return data.Catalog.Families.Where(f => f.Divine != null).Select(f => new
            {
                f.Id, Set = f.Divine, f.Element, f.Role, Form = f.Forms[0].Id, Names = f.Forms[0].Names, Issued = counts.GetValueOrDefault(f.Forms[0].Id),
                InPacks = data.Catalog.PackTypes.Where(p => p.DivinePool.Contains(f.Forms[0].Id)).Select(p => p.Id),
            });
        });

        gm.MapGet("/cards/{serial}/progress", async (string serial, PetStarDb db) =>
        {
            var card = await db.Cards.AsNoTracking().FirstOrDefaultAsync(c => c.Serial == serial.Trim().ToUpperInvariant()) ?? throw ApiException.NotFound("serial_not_found");
            return new
            {
                card.Id, card.Serial, card.FormId, card.Rarity, card.Stars, card.EnhanceLevel, card.Breakthrough, card.Awakened, card.SkillLevel, card.Level,
                engravings = await db.Engravings.AsNoTracking().Where(e => e.CardId == card.Id).ToListAsync(),
                events = await db.CardProgress.AsNoTracking().Where(e => e.CardId == card.Id).OrderBy(e => e.At).ToListAsync(),
            };
        });

        // ---------------------------------------------------------------- limited events

        gm.MapGet("/events", async (string? packTypeId, PetStarDb db) =>
            await db.LimitedEvents.AsNoTracking().Where(e => packTypeId == null || e.PackTypeId == packTypeId).OrderByDescending(e => e.StartsAt).ToListAsync());

        admin.MapPost("/events", async (AdminEventRequest req, HttpContext ctx, PetStarDb db, GameData data, AuditService audit) =>
        {
            var (p, _) = await data.Pack(db, req.PackTypeId);
            if (!p.Requirements.RequiresEvent) throw ApiException.BadRequest("pack_has_no_events");
            if (req.EndsAt <= req.StartsAt || req.EndsAt <= DateTimeOffset.UtcNow) throw ApiException.BadRequest("invalid_event_window");
            if (req.TotalLimit is <= 0 || req.PerPlayerLimit is <= 0) throw ApiException.BadRequest("invalid_event_limits");
            if (string.IsNullOrWhiteSpace(req.NameVi) || string.IsNullOrWhiteSpace(req.NameEn)) throw ApiException.BadRequest("event_name_required");
            if (req.ServerId != null && !await db.Servers.AnyAsync(s => s.Id == req.ServerId)) throw ApiException.NotFound("server_not_found");
            var now = DateTimeOffset.UtcNow;
            var e = new LimitedEvent
            {
                PackTypeId = p.Id, ServerId = req.ServerId, NameVi = req.NameVi.Trim(), NameEn = req.NameEn.Trim(), StartsAt = req.StartsAt, EndsAt = req.EndsAt,
                TotalLimit = req.TotalLimit, PerPlayerLimit = req.PerPlayerLimit, CreatedByAccountId = ctx.User.AccountId(), CreatedAt = now, UpdatedAt = now,
            };
            db.LimitedEvents.Add(e);
            await db.SaveChangesAsync();
            audit.Add("cardsys.event.create", ctx.User.AccountId(), "limited_event", e.Id.ToString(), req.ServerId, req);
            await db.SaveChangesAsync();
            return e;
        });

        admin.MapPatch("/events/{id:long}", async (long id, AdminEventPatch req, HttpContext ctx, PetStarDb db, AuditService audit) =>
        {
            Reason(req.Reason);
            var e = await db.LimitedEvents.FirstOrDefaultAsync(x => x.Id == id) ?? throw ApiException.NotFound("event_not_found");
            if (req.EndsAt != null) { if (req.EndsAt <= e.StartsAt) throw ApiException.BadRequest("invalid_event_window"); e.EndsAt = req.EndsAt.Value; }
            if (req.TotalLimit != null) { if (req.TotalLimit < e.Sold || req.TotalLimit <= 0) throw ApiException.BadRequest("invalid_event_limits"); e.TotalLimit = req.TotalLimit; }
            if (req.PerPlayerLimit != null) { if (req.PerPlayerLimit <= 0) throw ApiException.BadRequest("invalid_event_limits"); e.PerPlayerLimit = req.PerPlayerLimit; }
            if (req.Cancelled is bool c) e.Cancelled = c;   // stops sales; cards already obtained are never taken back
            e.UpdatedAt = DateTimeOffset.UtcNow;
            audit.Add("cardsys.event.update", ctx.User.AccountId(), "limited_event", id.ToString(), e.ServerId, req);
            await db.SaveChangesAsync();
            return e;
        });

        // ---------------------------------------------------------------- Star invitations

        gm.MapGet("/invitations", async (int serverId, long? uid, string? status, PetStarDb db) =>
        {
            var q = db.StarInvitations.AsNoTracking().Where(i => i.ServerId == serverId);
            if (uid != null) q = q.Where(i => db.Memberships.Any(m => m.Id == i.MembershipId && m.AccountId == uid));
            var now = DateTimeOffset.UtcNow;
            var list = await q.OrderByDescending(i => i.IssuedAt).Take(500).ToListAsync();
            return list.Select(i => new
            {
                i.Id, i.MembershipId, i.IssuedByAccountId, i.IssuedAt, i.ExpiresAt, i.MaxPacks, i.UsedPacks, i.Note, i.RevokedByAccountId, i.RevokedAt, i.RevokeReason,
                Status = InviteStatus(i, now),
            }).Where(i => status == null || i.Status == status);
        });

        admin.MapPost("/invitations", async (AdminInviteRequest req, HttpContext ctx, PetStarDb db, AuditService audit, NotificationService notes) =>
        {
            if (req.Days < 1 || req.Days > 90 || req.MaxPacks < 1 || req.MaxPacks > 50) throw ApiException.BadRequest("invalid_invitation");
            var m = await db.Memberships.AsNoTracking().FirstOrDefaultAsync(x => x.AccountId == req.Uid && x.ServerId == req.ServerId) ?? throw ApiException.NotFound("player_not_found");
            var now = DateTimeOffset.UtcNow;
            var inv = new StarInvitation
            {
                Id = Guid.NewGuid(), ServerId = req.ServerId, MembershipId = m.Id, IssuedByAccountId = ctx.User.AccountId(), IssuedAt = now,
                ExpiresAt = now.AddDays(req.Days), MaxPacks = req.MaxPacks, Note = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim(),
            };
            db.StarInvitations.Add(inv);
            audit.Add("cardsys.invitation.issue", ctx.User.AccountId(), "membership", m.Id.ToString(), req.ServerId, new { inv.Id, req.Days, req.MaxPacks, req.Note });
            await notes.Notify(m.Id, "admin_notice", new { kind = "star_invitation", inv.ExpiresAt, inv.MaxPacks }, "invite:" + inv.Id.ToString("N"));
            await db.SaveChangesAsync();
            return inv;
        });

        admin.MapPost("/invitations/{id:guid}/revoke", async (Guid id, AdminRevokeRequest req, HttpContext ctx, PetStarDb db, AuditService audit) =>
        {
            Reason(req.Reason);
            var inv = await db.StarInvitations.FirstOrDefaultAsync(i => i.Id == id) ?? throw ApiException.NotFound("invitation_not_found");
            if (inv.Status == "revoked") throw ApiException.Conflict("invitation_already_revoked");
            inv.Status = "revoked"; inv.RevokedAt = DateTimeOffset.UtcNow; inv.RevokedByAccountId = ctx.User.AccountId(); inv.RevokeReason = req.Reason.Trim();
            audit.Add("cardsys.invitation.revoke", ctx.User.AccountId(), "star_invitation", id.ToString(), inv.ServerId, req);
            await db.SaveChangesAsync();
            return inv;
        });

        // ---------------------------------------------------------------- Rainbow Star flows & anomalies

        gm.MapGet("/rainbow-stars", async (int serverId, int? days, PetStarDb db) =>
        {
            var since = DateTimeOffset.UtcNow.AddDays(-Math.Clamp(days ?? 7, 1, 90));
            var byReason = await db.Ledger.AsNoTracking().Where(l => l.ServerId == serverId && l.Currency == Currency.RainbowStar && l.At >= since)
                .GroupBy(l => l.Reason).Select(g => new { Reason = g.Key, Entries = g.Count(), In = g.Where(x => x.Delta > 0).Sum(x => (long?)x.Delta) ?? 0, Out = g.Where(x => x.Delta < 0).Sum(x => (long?)x.Delta) ?? 0 })
                .ToListAsync();
            var market = await db.MarketTransactions.AsNoTracking().Where(t => t.ServerId == serverId && t.Currency == Currency.RainbowStar && t.At >= since)
                .GroupBy(t => 1).Select(g => new { Trades = g.Count(), Volume = g.Sum(x => x.Price), Fees = g.Sum(x => x.Fee) }).FirstOrDefaultAsync();
            return new { since, byReason, market };
        });

        gm.MapGet("/anomalies", async (int serverId, PetStarDb db) =>
        {
            var since = DateTimeOffset.UtcNow.AddDays(-1);
            var heavyOpeners = await db.PackOpenings.AsNoTracking().Where(o => o.ServerId == serverId && o.CreatedAt >= since)
                .GroupBy(o => o.MembershipId).Select(g => new { MembershipId = g.Key, Openings = g.Count() }).Where(x => x.Openings >= 200)
                .OrderByDescending(x => x.Openings).Take(50).ToListAsync();
            var heavyUpgrades = await db.CardProgress.AsNoTracking().Where(e => e.At >= since)
                .Join(db.Cards.AsNoTracking().Where(c => c.ServerId == serverId), e => e.CardId, c => c.Id, (e, c) => new { c.OwnerMembershipId })
                .GroupBy(x => x.OwnerMembershipId).Select(g => new { MembershipId = g.Key, Upgrades = g.Count() }).Where(x => x.Upgrades >= 500)
                .OrderByDescending(x => x.Upgrades).Take(50).ToListAsync();
            // divine cards that changed hands more than 3 times in 7 days (possible laundering)
            var weekAgo = DateTimeOffset.UtcNow.AddDays(-7);
            var hotDivine = await db.CardOwnership.AsNoTracking().Where(h => h.ServerId == serverId && h.At >= weekAgo && h.FromMembershipId != null)
                .Join(db.Cards.AsNoTracking().Where(c => c.Rarity == RarityDef.Divine), h => h.CardId, c => c.Id, (h, c) => new { c.Id, c.Serial })
                .GroupBy(x => new { x.Id, x.Serial }).Select(g => new { CardId = g.Key.Id, g.Key.Serial, Transfers = g.Count() }).Where(x => x.Transfers > 3).ToListAsync();
            var suspicious = await db.EconomyAudit.AsNoTracking().Where(s => s.ServerId == serverId && s.Status == "open").CountAsync();
            return new { heavyOpeners, heavyUpgrades, hotDivine, openSuspiciousTrades = suspicious };
        });

        gm.MapGet("/materials", async (int serverId, PetStarDb db) =>
        {
            var held = await db.Items.AsNoTracking().Where(i => Progression.Materials.Contains(i.ItemId))
                .Join(db.Memberships.AsNoTracking().Where(m => m.ServerId == serverId), i => i.MembershipId, m => m.Id, (i, m) => i)
                .GroupBy(i => i.ItemId).Select(g => new { ItemId = g.Key, Holders = g.Count(), Total = g.Sum(x => x.Quantity) }).ToListAsync();
            var since = DateTimeOffset.UtcNow.AddDays(-7);
            var flows = await db.ItemLedger.AsNoTracking().Where(l => l.ServerId == serverId && Progression.Materials.Contains(l.ItemId) && l.At >= since)
                .GroupBy(l => new { l.ItemId, l.Reason }).Select(g => new { g.Key.ItemId, g.Key.Reason, Delta = g.Sum(x => x.Delta) }).ToListAsync();
            return new { held, flows };
        });

        owner.MapPost("/materials/grant", async (AdminGrantRequest req, HttpContext ctx, PetStarDb db, EconomyService eco, AuditService audit, FraudService fraud) =>
        {
            Reason(req.Reason);
            if (!Progression.Materials.Contains(req.ItemId) || req.Amount == 0 || Math.Abs(req.Amount) > 100_000) throw ApiException.BadRequest("invalid_grant");
            var m = await db.Memberships.AsNoTracking().FirstOrDefaultAsync(x => x.AccountId == req.Uid && x.ServerId == req.ServerId) ?? throw ApiException.NotFound("player_not_found");
            await using var tx = await db.Database.BeginTransactionAsync();
            var refId = "grant:" + Guid.NewGuid().ToString("N");
            var r = await eco.Item(m.Id, req.ServerId, req.ItemId, req.Amount, "admin_grant", refId);
            fraud.Record(req.ServerId, "admin_material_grant", "info", m.Id, null, refId, new { req.ItemId, req.Amount, req.Reason, by = ctx.User.AccountId() });
            audit.Add("cardsys.material.grant", ctx.User.AccountId(), "membership", m.Id.ToString(), req.ServerId, req);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return new { refId, balanceAfter = r.BalanceAfter };
        });

        owner.MapPost("/vip/grant", async (AdminGrantRequest req, HttpContext ctx, PetStarDb db, VipService vip, AuditService audit) =>
        {
            Reason(req.Reason);
            if (req.Amount <= 0 || req.Amount > 1_000_000) throw ApiException.BadRequest("invalid_grant");
            var m = await db.Memberships.AsNoTracking().FirstOrDefaultAsync(x => x.AccountId == req.Uid && x.ServerId == req.ServerId) ?? throw ApiException.NotFound("player_not_found");
            await using var tx = await db.Database.BeginTransactionAsync();
            int level = await vip.AddPoints(m.Id, req.ServerId, req.Amount);
            audit.Add("cardsys.vip.grant", ctx.User.AccountId(), "membership", m.Id.ToString(), req.ServerId, req);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return await vip.Get(m.Id);
        });

        gm.MapGet("/config-log", async (int? limit, PetStarDb db) =>
            await db.AuditLogs.AsNoTracking().Where(a => a.Action.StartsWith("cardsys.")).OrderByDescending(a => a.At).Take(Math.Clamp(limit ?? 200, 1, 1000)).ToListAsync());
    }

    private static string InviteStatus(StarInvitation i, DateTimeOffset now) =>
        i.Status == "revoked" ? "revoked" : i.UsedPacks >= i.MaxPacks ? "used" : i.ExpiresAt <= now ? "expired" : "active";

    private static void Reason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 10) throw ApiException.BadRequest("reason_required");
    }

    /// <summary>Applies a change to a pack definition as a NEW version (history kept, openings keep their version) after
    /// the same validation the catalog uses (tables sum to 100 %, divine pool consistent).</summary>
    private static async Task<object> SaveVersion(PetStarDb db, GameData data, AuditService audit, HttpContext ctx, string id, string action, string reason,
        Action<PackTypeDef> change, object request)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var row = await db.PackTypes.FromSqlRaw("SELECT * FROM pack_types WHERE id = {0} FOR UPDATE", id).FirstOrDefaultAsync() ?? throw ApiException.NotFound("pack_not_found");
        var def = JsonSerializer.Deserialize<PackTypeDef>(row.DataJson, IdempotencyService.Json)!;
        change(def);
        var probe = new GameCatalog
        {
            Elements = data.Catalog.Elements, Rarities = data.Catalog.Rarities, Qualities = data.Catalog.Qualities, Finishes = data.Catalog.Finishes,
            Families = data.Catalog.Families, Skills = data.Catalog.Skills, Passives = data.Catalog.Passives, PackTypes = new List<PackTypeDef> { def },
        };
        var problems = probe.Validate();
        if (problems.Count > 0) throw ApiException.BadRequest("invalid_pack_config", string.Join("; ", problems));
        row.Version++; row.Enabled = def.Enabled; row.DataJson = JsonSerializer.Serialize(def, IdempotencyService.Json); row.UpdatedAt = DateTimeOffset.UtcNow;
        db.PackTypeVersions.Add(new PackTypeVersion
        {
            PackTypeId = id, Version = row.Version, Enabled = row.Enabled, DataJson = row.DataJson, ChangedByAccountId = ctx.User.AccountId(),
            Reason = reason.Trim(), CreatedAt = DateTimeOffset.UtcNow,
        });
        audit.Add(action, ctx.User.AccountId(), "pack_type", id, null, new { version = row.Version, request });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return new { id, version = row.Version, enabled = row.Enabled, odds = PackEndpoints.Odds(data.Catalog, def, row.Version) };
    }
}
