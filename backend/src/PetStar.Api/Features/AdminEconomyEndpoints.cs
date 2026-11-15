using Microsoft.EntityFrameworkCore;
using PetStar.Api.Auth;
using PetStar.Api.Data;
using PetStar.Api.Infrastructure;
using PetStar.Rules;

namespace PetStar.Api.Features;

public sealed class ResolveReportRequest { public string Action { get; set; } = "dismiss"; public string Reason { get; set; } = ""; public int? RestrictHours { get; set; } }
public sealed class RestrictionRequest { public long Uid { get; set; } public string Scope { get; set; } = "social"; public int Hours { get; set; } = 24; public string Reason { get; set; } = ""; }
public sealed class AdjustmentRequest { public long Uid { get; set; } public int ServerId { get; set; } public string Currency { get; set; } = ""; public long Delta { get; set; } public string Reason { get; set; } = ""; }
public sealed class FraudLockRequest { public bool Lock { get; set; } public string Reason { get; set; } = ""; }

/// <summary>
/// Economy / moderation administration. Role model (RBAC):
///  moderator: reports queue, feed moderation, social restrictions
///  gm:        + serial lookup, ownership audit, market/auction management, suspicious trades, fraud locks
///  admin:     + ledger viewer, economy overview (and server management in AdminEndpoints)
///  owner:     + currency adjustments (compensating ledger entries). Nobody else can create currency or cards.
/// Every sensitive action is written to admin_audit_logs / moderation_actions.
/// </summary>
public static class AdminEconomyEndpoints
{
    public static void Map(WebApplication app)
    {
        var mod = app.MapGroup("/api/admin").RequireAuthorization("Moderation").RequireRateLimiting("user");
        var gm = app.MapGroup("/api/admin").RequireAuthorization("GameMaster").RequireRateLimiting("user");
        var admin = app.MapGroup("/api/admin").RequireAuthorization("ServerAdmin").RequireRateLimiting("user");
        var owner = app.MapGroup("/api/admin").RequireAuthorization("Owner").RequireRateLimiting("user");

        // ---------------- moderation ----------------
        mod.MapGet("/reports", async (string? status, int? serverId, PetStarDb db) =>
            await db.Reports.AsNoTracking().Where(r => (status == null ? r.Status == "open" : r.Status == status) && (serverId == null || r.ServerId == serverId))
                .OrderBy(r => r.CreatedAt).Take(200).ToListAsync());

        mod.MapPost("/reports/{id:long}/resolve", async (long id, ResolveReportRequest req, HttpContext ctx, PetStarDb db, AuditService audit) =>
        {
            var r = await db.Reports.FirstOrDefaultAsync(x => x.Id == id) ?? throw ApiException.NotFound();
            if (r.Status != "open") throw ApiException.Conflict("report_already_handled");
            long actor = ctx.User.AccountId();
            switch (req.Action)
            {
                case "dismiss": r.Status = "dismissed"; break;
                case "hide":
                    if (r.TargetType == "post") await db.Posts.Where(p => p.Id == r.TargetId).ExecuteUpdateAsync(u => u.SetProperty(p => p.Status, "hidden"));
                    else if (r.TargetType == "comment") await db.Comments.Where(c => c.Id == r.TargetId).ExecuteUpdateAsync(u => u.SetProperty(c => c.Status, "hidden"));
                    else throw ApiException.BadRequest("hide_not_applicable");
                    r.Status = "actioned"; break;
                case "restrict":
                    long? accountId = r.TargetType switch
                    {
                        "user" => r.TargetId,
                        "post" => await db.Posts.Where(p => p.Id == r.TargetId).Join(db.Memberships, p => p.AuthorMembershipId, m => m.Id, (p, m) => (long?)m.AccountId).FirstOrDefaultAsync(),
                        "comment" => await db.Comments.Where(c => c.Id == r.TargetId).Join(db.Memberships, c => c.AuthorMembershipId, m => m.Id, (c, m) => (long?)m.AccountId).FirstOrDefaultAsync(),
                        _ => null,
                    };
                    if (accountId == null) throw ApiException.BadRequest("restrict_not_applicable");
                    db.Restrictions.Add(new AccountRestriction { AccountId = accountId.Value, Scope = "social", Reason = req.Reason, Until = DateTimeOffset.UtcNow.AddHours(req.RestrictHours ?? 24), CreatedBy = actor, CreatedAt = DateTimeOffset.UtcNow });
                    r.Status = "actioned"; break;
                default: throw ApiException.BadRequest("invalid_action");
            }
            r.HandledBy = actor; r.HandledAt = DateTimeOffset.UtcNow;
            db.ModerationActions.Add(new ModerationAction { ServerId = r.ServerId, ModeratorAccountId = actor, Action = req.Action, TargetType = r.TargetType, TargetId = r.TargetId, Reason = req.Reason, ReportId = r.Id, At = DateTimeOffset.UtcNow });
            audit.Add("moderation.report." + req.Action, actor, r.TargetType, r.TargetId.ToString(), r.ServerId, new { reportId = r.Id, req.Reason });
            await db.SaveChangesAsync();
            return r;
        });

        mod.MapPost("/moderation/posts/{id:long}/{action}", async (long id, string action, HttpContext ctx, PetStarDb db, AuditService audit) =>
        {
            var p = await db.Posts.FirstOrDefaultAsync(x => x.Id == id) ?? throw ApiException.NotFound();
            switch (action)
            {
                case "hide": p.Status = "hidden"; break;
                case "unhide": p.Status = "visible"; break;
                case "feature": p.Featured = true; break;
                case "unfeature": p.Featured = false; break;
                default: throw ApiException.BadRequest("invalid_action");
            }
            db.ModerationActions.Add(new ModerationAction { ServerId = p.ServerId, ModeratorAccountId = ctx.User.AccountId(), Action = action + "_post", TargetType = "post", TargetId = id, At = DateTimeOffset.UtcNow });
            audit.Add("moderation.post." + action, ctx.User.AccountId(), "post", id.ToString(), p.ServerId);
            await db.SaveChangesAsync();
            return Results.Ok(new { p.Id, p.Status, p.Featured });
        });

        mod.MapPost("/restrictions", async (RestrictionRequest req, HttpContext ctx, PetStarDb db, AuditService audit) =>
        {
            bool isGmPlus = ctx.User.IsInRole("gm") || ctx.User.IsInRole("admin") || ctx.User.IsInRole("owner");
            if (!new[] { "social", "market", "all" }.Contains(req.Scope)) throw ApiException.BadRequest("invalid_scope");
            if (req.Scope != "social" && !isGmPlus) throw ApiException.Forbidden("insufficient_role");
            if (req.Hours < 1 || req.Hours > 24 * 365) throw ApiException.BadRequest("invalid_duration");
            if (!await db.Accounts.AnyAsync(a => a.Id == req.Uid)) throw ApiException.NotFound("player_not_found");
            var r = new AccountRestriction { AccountId = req.Uid, Scope = req.Scope, Reason = req.Reason, Until = DateTimeOffset.UtcNow.AddHours(req.Hours), CreatedBy = ctx.User.AccountId(), CreatedAt = DateTimeOffset.UtcNow };
            db.Restrictions.Add(r);
            db.ModerationActions.Add(new ModerationAction { ModeratorAccountId = ctx.User.AccountId(), Action = "restrict", TargetType = "account", TargetId = req.Uid, Reason = req.Reason, At = DateTimeOffset.UtcNow });
            audit.Add("moderation.restrict", ctx.User.AccountId(), "account", req.Uid.ToString(), null, req);
            await db.SaveChangesAsync();
            return r;
        });

        mod.MapDelete("/restrictions/{id:long}", async (long id, HttpContext ctx, PetStarDb db, AuditService audit) =>
        {
            var r = await db.Restrictions.FirstOrDefaultAsync(x => x.Id == id) ?? throw ApiException.NotFound();
            r.Until = DateTimeOffset.UtcNow;
            audit.Add("moderation.unrestrict", ctx.User.AccountId(), "account", r.AccountId.ToString());
            await db.SaveChangesAsync();
            return Results.Ok(new { lifted = true });
        });

        // ---------------- game master: cards & market ----------------
        gm.MapGet("/cards/serial/{serial}", async (string serial, PetStarDb db) =>
        {
            serial = serial.Trim().ToUpperInvariant();
            var reg = await db.SerialRegistry.AsNoTracking().FirstOrDefaultAsync(r => r.Serial == serial) ?? throw ApiException.NotFound("serial_not_found");
            var card = reg.CardId == null ? null : await db.Cards.AsNoTracking().FirstOrDefaultAsync(c => c.Id == reg.CardId);
            if (card == null) return Results.Ok(new { registry = reg, card = (object?)null });
            var history = await db.CardOwnership.AsNoTracking().Where(h => h.CardId == card.Id).OrderBy(h => h.At)
                .Select(h => new { h.At, h.Reason, h.RefId, h.FromMembershipId, h.ToMembershipId }).ToListAsync();
            return Results.Ok(new
            {
                registry = reg, card, passport = await db.Passports.AsNoTracking().FirstOrDefaultAsync(p => p.CardId == card.Id),
                ownershipHistory = history,
                evolutions = await db.CardEvolutions.AsNoTracking().Where(e => e.CardId == card.Id).ToListAsync(),
                listings = await db.Listings.AsNoTracking().Where(l => l.CardId == card.Id).ToListAsync(),
                auctions = await db.Auctions.AsNoTracking().Where(a => a.CardId == card.Id).ToListAsync(),
                transactions = await db.MarketTransactions.AsNoTracking().Where(t => t.CardId == card.Id).ToListAsync(),
            });
        });

        // Ownership audit: every card must have exactly one owner, consistent with its last ownership history entry.
        gm.MapGet("/cards/ownership-audit", async (int serverId, PetStarDb db) =>
        {
            var mismatches = await db.Database.SqlQueryRaw<long>(
                "SELECT c.id AS \"Value\" FROM card_instances c JOIN LATERAL (SELECT to_membership_id FROM card_ownership_history h WHERE h.card_id = c.id ORDER BY h.at DESC, h.id DESC LIMIT 1) last ON true " +
                "WHERE c.server_id = {0} AND last.to_membership_id IS DISTINCT FROM c.owner_membership_id", serverId).ToListAsync();
            var dupSerials = await db.Database.SqlQueryRaw<string>("SELECT serial AS \"Value\" FROM card_instances GROUP BY serial HAVING count(*) > 1").ToListAsync();
            var crossServer = await db.Database.SqlQueryRaw<long>(
                "SELECT c.id AS \"Value\" FROM card_instances c JOIN server_memberships m ON m.id = c.owner_membership_id WHERE m.server_id <> c.server_id").ToListAsync();
            return new { serverId, ownerHistoryMismatches = mismatches, duplicateSerials = dupSerials, crossServerOwners = crossServer, ok = mismatches.Count == 0 && dupSerials.Count == 0 && crossServer.Count == 0 };
        });

        gm.MapGet("/market/listings", async (int serverId, string? status, PetStarDb db) =>
            await db.Listings.AsNoTracking().Where(l => l.ServerId == serverId && (status == null || l.Status == status)).OrderByDescending(l => l.Id).Take(200).ToListAsync());

        gm.MapPost("/market/listings/{id:long}/remove", async (long id, FraudLockRequest req, HttpContext ctx, PetStarDb db, NotificationService notes, AuditService audit) =>
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            var l = await db.Listings.FromSqlRaw("SELECT * FROM market_listings WHERE id = {0} FOR UPDATE", id).FirstOrDefaultAsync() ?? throw ApiException.NotFound();
            if (l.Status != ListingStatus.Active) throw ApiException.Conflict("listing_not_active");
            await MarketEndpoints.CloseListing(db, l, ListingStatus.Removed);
            await notes.Notify(l.SellerMembershipId, "admin_notice", new { kind = "listing_removed", listingId = id, reason = req.Reason }, "listing_removed:" + id);
            audit.Add("admin.market.remove_listing", ctx.User.AccountId(), "listing", id.ToString(), l.ServerId, req);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return Results.Ok(new { removed = true });
        });

        gm.MapGet("/auctions", async (int serverId, string? status, PetStarDb db) =>
            await db.Auctions.AsNoTracking().Where(a => a.ServerId == serverId && (status == null || a.Status == status)).OrderByDescending(a => a.Id).Take(200).ToListAsync());

        gm.MapGet("/economy/suspicious", async (int serverId, string? status, PetStarDb db) =>
            await db.EconomyAudit.AsNoTracking().Where(e => e.ServerId == serverId && e.Status == (status ?? "open")).OrderByDescending(e => e.Id).Take(200).ToListAsync());

        gm.MapPost("/economy/suspicious/{id:long}/review", async (long id, FraudLockRequest req, HttpContext ctx, PetStarDb db, AuditService audit) =>
        {
            var e = await db.EconomyAudit.FirstOrDefaultAsync(x => x.Id == id) ?? throw ApiException.NotFound();
            e.Status = req.Lock ? "reviewed" : "dismissed";
            audit.Add("admin.economy.review", ctx.User.AccountId(), "economy_event", id.ToString(), e.ServerId, req);
            await db.SaveChangesAsync();
            return e;
        });

        // Fraud lock: card cannot be traded, listed, auctioned, evolved, trained or used in new battles while under review.
        gm.MapPost("/cards/{id:long}/fraud-lock", async (long id, FraudLockRequest req, HttpContext ctx, PetStarDb db, AuditService audit) =>
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            var c = await db.Cards.FromSqlRaw("SELECT * FROM card_instances WHERE id = {0} FOR UPDATE", id).FirstOrDefaultAsync() ?? throw ApiException.NotFound();
            if (req.Lock)
            {
                if (c.BusyState != null && c.BusyState.StartsWith("listing:"))
                {
                    var l = await db.Listings.FromSqlRaw("SELECT * FROM market_listings WHERE id = {0} FOR UPDATE", long.Parse(c.BusyState[8..])).FirstAsync();
                    await MarketEndpoints.CloseListing(db, l, ListingStatus.Removed);
                    await db.Entry(c).ReloadAsync();
                }
                if (c.BusyState != null) throw ApiException.Conflict("card_busy", c.BusyState);
                c.BusyState = "fraud_review";
            }
            else if (c.BusyState == "fraud_review") c.BusyState = null;
            db.ModerationActions.Add(new ModerationAction { ServerId = c.ServerId, ModeratorAccountId = ctx.User.AccountId(), Action = req.Lock ? "fraud_lock" : "fraud_unlock", TargetType = "card", TargetId = id, Reason = req.Reason, At = DateTimeOffset.UtcNow });
            audit.Add(req.Lock ? "admin.card.fraud_lock" : "admin.card.fraud_unlock", ctx.User.AccountId(), "card", id.ToString(), c.ServerId, req);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return Results.Ok(new { c.Id, c.BusyState });
        });

        // ---------------- admin: ledger & economy overview ----------------
        admin.MapGet("/ledger", async (long uid, int serverId, string? currency, int? limit, PetStarDb db) =>
        {
            var m = await db.Memberships.AsNoTracking().FirstOrDefaultAsync(x => x.AccountId == uid && x.ServerId == serverId) ?? throw ApiException.NotFound("player_not_found");
            return await db.Ledger.AsNoTracking().Where(l => l.MembershipId == m.Id && (currency == null || l.Currency == currency))
                .OrderByDescending(l => l.Id).Take(Math.Clamp(limit ?? 200, 1, 1000)).ToListAsync();
        });

        admin.MapGet("/economy/overview", async (int serverId, PetStarDb db) =>
        {
            var day = DateTimeOffset.UtcNow.AddDays(-1);
            var wallets = await db.Wallets.AsNoTracking().Join(db.Memberships, w => w.MembershipId, m => m.Id, (w, m) => new { w, m.ServerId })
                .Where(x => x.ServerId == serverId).GroupBy(x => x.w.Currency).Select(g => new { currency = g.Key, total = g.Sum(x => x.w.Balance) }).ToListAsync();
            var held = await db.ReservedBalances.AsNoTracking().Where(r => r.ServerId == serverId && r.Status == "held").GroupBy(r => r.Currency).Select(g => new { currency = g.Key, total = g.Sum(r => r.Amount) }).ToListAsync();
            var flows = await db.Ledger.AsNoTracking().Where(l => l.ServerId == serverId && l.At >= day).GroupBy(l => new { l.Currency, l.Reason })
                .Select(g => new { g.Key.Currency, g.Key.Reason, sum = g.Sum(l => l.Delta), count = g.Count() }).ToListAsync();
            var fees = await db.SystemLedger.AsNoTracking().Where(s => s.ServerId == serverId).GroupBy(s => s.Currency).Select(g => new { currency = g.Key, total = g.Sum(s => s.Amount) }).ToListAsync();
            var volume = await db.MarketTransactions.AsNoTracking().Where(t => t.ServerId == serverId && t.At >= day).GroupBy(t => t.Currency).Select(g => new { currency = g.Key, trades = g.Count(), volume = g.Sum(t => t.Price), suspicious = g.Count(t => t.Suspicious) }).ToListAsync();
            var supply = await db.Cards.AsNoTracking().Where(c => c.ServerId == serverId).GroupBy(c => c.Rarity).Select(g => new { rarity = g.Key, count = g.Count() }).ToListAsync();
            return new
            {
                serverId, walletTotals = wallets, heldInAuctions = held, ledgerFlows24h = flows, feesSunk = fees, market24h = volume, cardSupplyByRarity = supply,
                activeListings = await db.Listings.CountAsync(l => l.ServerId == serverId && l.Status == ListingStatus.Active),
                activeAuctions = await db.Auctions.CountAsync(a => a.ServerId == serverId && a.Status == AuctionStatus.Active),
                openFraudEvents = await db.EconomyAudit.CountAsync(e => e.ServerId == serverId && e.Status == "open"),
            };
        });

        admin.MapGet("/market/stats", async (int serverId, int? days, PetStarDb db) =>
        {
            var since = DateTimeOffset.UtcNow.AddDays(-Math.Clamp(days ?? 7, 1, 90));
            return await db.MarketTransactions.AsNoTracking().Where(t => t.ServerId == serverId && t.At >= since && !t.Suspicious)
                .GroupBy(t => new { t.FormId, t.Rarity, t.Currency })
                .Select(g => new { g.Key.FormId, g.Key.Rarity, g.Key.Currency, trades = g.Count(), avg = g.Average(t => t.Price), min = g.Min(t => t.Price), max = g.Max(t => t.Price) })
                .OrderByDescending(x => x.trades).Take(200).ToListAsync();
        });

        // ---------------- owner only: compensating adjustments ----------------
        owner.MapPost("/economy/adjust", async (AdjustmentRequest req, HttpContext ctx, PetStarDb db, EconomyService eco, FraudService fraud, AuditService audit, NotificationService notes) =>
        {
            if (!Currency.All.Contains(req.Currency) || req.Delta == 0 || Math.Abs(req.Delta) > 10_000_000) throw ApiException.BadRequest("invalid_adjustment");
            if (string.IsNullOrWhiteSpace(req.Reason) || req.Reason.Length < 10) throw ApiException.BadRequest("reason_required");
            var m = await db.Memberships.AsNoTracking().FirstOrDefaultAsync(x => x.AccountId == req.Uid && x.ServerId == req.ServerId) ?? throw ApiException.NotFound("player_not_found");
            await using var tx = await db.Database.BeginTransactionAsync();
            var refId = "adj:" + Guid.NewGuid().ToString("N");
            var r = await eco.Currency(m.Id, req.ServerId, req.Currency, req.Delta, "admin_adjustment", refId, ctx.User.AccountId());
            fraud.Record(req.ServerId, "admin_adjustment", "info", m.Id, null, refId, new { req.Delta, req.Currency, req.Reason, by = ctx.User.AccountId() });
            audit.Add("owner.economy.adjust", ctx.User.AccountId(), "membership", m.Id.ToString(), req.ServerId, req);
            await notes.Notify(m.Id, "admin_notice", new { kind = "balance_adjusted", req.Currency, req.Delta }, refId);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return Results.Ok(new { refId, balanceAfter = r.BalanceAfter });
        });

        owner.MapPost("/mail", async (MailRequest req, HttpContext ctx, PetStarDb db, NotificationService notes, AuditService audit) =>
        {
            var m = await db.Memberships.AsNoTracking().FirstOrDefaultAsync(x => x.AccountId == req.Uid && x.ServerId == req.ServerId) ?? throw ApiException.NotFound("player_not_found");
            var key = "admin_mail:" + Guid.NewGuid().ToString("N");
            await notes.Mail(m.Id, req.TitleKey, req.Body, new { currencies = req.Currencies, items = req.Items, packs = req.Packs }, key, DateTimeOffset.UtcNow.AddDays(30));
            audit.Add("owner.mail.send", ctx.User.AccountId(), "membership", m.Id.ToString(), req.ServerId, req);
            await db.SaveChangesAsync();
            return Results.Ok(new { sent = true });
        });
    }
}

public sealed class MailRequest
{
    public long Uid { get; set; }
    public int ServerId { get; set; }
    public string TitleKey { get; set; } = "mail.admin";
    public string Body { get; set; } = "";
    public Dictionary<string, long> Currencies { get; set; } = new();
    public Dictionary<string, long> Items { get; set; } = new();
    public Dictionary<string, int> Packs { get; set; } = new();
}
