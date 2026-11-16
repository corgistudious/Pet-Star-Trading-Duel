using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PetStar.Api.Data;
using PetStar.Api.Features;
using PetStar.Rules;

namespace PetStar.Api.Infrastructure;

/// <summary>Notification Center. Each notification has a dedupe key (unique per member), so retries never duplicate.</summary>
public sealed class NotificationService(PetStarDb db, RealtimeHub hub, PushQueue push)
{
    public async Task Notify(long membershipId, string type, object data, string dedupeKey)
    {
        int n = await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO notifications (membership_id, type, data_json, dedupe_key, created_at) VALUES ({0}, {1}, {2}::jsonb, {3}, now()) ON CONFLICT DO NOTHING",
            membershipId, type, JsonSerializer.Serialize(data, IdempotencyService.Json), dedupeKey);
        if (n == 1)
        {
            await hub.ToMembers(new[] { membershipId }, "notification", new { type, data });   // live in-app (best effort)
            push.Enqueue(new PushJob(membershipId, type, JsonSerializer.SerializeToNode(data, IdempotencyService.Json), dedupeKey)); // Web Push if offline
        }
    }

    public Task Mail(long membershipId, string titleKey, string body, object attachments, string dedupeKey, DateTimeOffset? expires = null) =>
        db.Database.ExecuteSqlRawAsync(
            "INSERT INTO mailbox_messages (membership_id, title_key, body, attachments_json, dedupe_key, created_at, expires_at) VALUES ({0}, {1}, {2}, {3}::jsonb, {4}, now(), {5}) ON CONFLICT DO NOTHING",
            membershipId, titleKey, body, JsonSerializer.Serialize(attachments, IdempotencyService.Json), dedupeKey,
            (object?)expires ?? DBNull.Value);
}

/// <summary>Achievements + discovery/collection rewards. Unlock rows are unique; rewards use ledger ref achievement:{id}
/// (unique), so they can never be granted twice — trading cards back and forth cannot farm them because codex
/// discovery is permanent.</summary>
public sealed class AchievementService(PetStarDb db, GameData data, EconomyService eco, NotificationService notes)
{
    public async Task<List<string>> Evaluate(long membershipId, int serverId, params string[] triggers)
    {
        var unlocked = new List<string>();
        var have = await db.Achievements.AsNoTracking().Where(a => a.MembershipId == membershipId).Select(a => a.AchievementId).ToListAsync();
        var candidates = data.Achievements.Items.Where(a => triggers.Contains(a.Trigger) && !have.Contains(a.Id)).ToList();
        if (candidates.Count == 0) return unlocked;
        var counts = new Dictionary<string, int>();
        async Task<int> Count(AchievementDef a)
        {
            string key = a.Trigger + ":" + a.Element;
            if (counts.TryGetValue(key, out var v)) return v;
            v = a.Trigger switch
            {
                "pack_opened" => await db.PackOpenings.CountAsync(o => o.MembershipId == membershipId),
                "rare_obtained" => await db.Cards.CountAsync(c => c.OwnerMembershipId == membershipId && new[] { "SR", "SSR", "UR", "LR", "MR", "GR", "EX", "Secret" }.Contains(c.Rarity)) > 0 ? 1 : 0,
                "evolution" => await db.CardEvolutions.CountAsync(e => e.MembershipId == membershipId),
                "duel_win" => await db.BattleResults.CountAsync(r => r.WinnerMembershipId == membershipId),
                "ranked_win" => await db.RankedRecords.Where(r => r.MembershipId == membershipId).SumAsync(r => (int?)r.Wins) ?? 0,
                "market_sale" => await db.MarketTransactions.CountAsync(t => t.SellerMembershipId == membershipId),
                "market_purchase" => await db.MarketTransactions.CountAsync(t => t.BuyerMembershipId == membershipId && t.Kind == "listing"),
                "auction_win" => await db.MarketTransactions.CountAsync(t => t.BuyerMembershipId == membershipId && t.Kind == "auction"),
                "feed_post" => await db.Posts.CountAsync(p => p.AuthorMembershipId == membershipId),
                "codex_count" => await db.Codex.CountAsync(c => c.MembershipId == membershipId),
                "family_complete" => await CompletedFamilies(membershipId),
                "element_complete" => await ElementComplete(membershipId, a.Element!) ? 1 : 0,
                "rare_owned_distinct" => await db.Cards.Where(c => c.OwnerMembershipId == membershipId && new[] { "SSR", "UR", "LR", "MR", "GR", "EX", "Secret" }.Contains(c.Rarity)).Select(c => c.FormId).Distinct().CountAsync(),
                _ => 0,
            };
            counts[key] = v;
            return v;
        }
        foreach (var a in candidates)
        {
            if (await Count(a) < a.Threshold) continue;
            int n = await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO achievement_progress (membership_id, achievement_id, unlocked_at) VALUES ({0}, {1}, now()) ON CONFLICT DO NOTHING", membershipId, a.Id);
            if (n == 0) continue;
            string refId = "achievement:" + a.Id;
            foreach (var (cur, amt) in a.Currencies) await eco.Currency(membershipId, serverId, cur, amt, "achievement_reward", refId, tolerateDuplicate: true);
            foreach (var (pack, qty) in a.Packs) await eco.Packs(membershipId, serverId, pack, qty, "achievement_reward", refId, tolerateDuplicate: true);
            await notes.Notify(membershipId, "achievement_unlocked", new { achievementId = a.Id, a.Currencies, a.Packs }, refId);
            unlocked.Add(a.Id);
        }
        return unlocked;
    }

    private async Task<int> CompletedFamilies(long membershipId)
    {
        var found = (await db.Codex.AsNoTracking().Where(c => c.MembershipId == membershipId).Select(c => c.FormId).ToListAsync()).ToHashSet();
        return data.Catalog.Families.Count(f => f.Forms.All(x => found.Contains(x.Id)));
    }

    private async Task<bool> ElementComplete(long membershipId, string element)
    {
        var found = (await db.Codex.AsNoTracking().Where(c => c.MembershipId == membershipId).Select(c => c.FormId).ToListAsync()).ToHashSet();
        return data.Catalog.Forms.Values.Where(f => f.Element == element).All(f => found.Contains(f.Id));
    }
}

public sealed record FraudAssessment(bool Suspicious, List<string> Reasons);

/// <summary>Market integrity signals: repeated pair trading, round trips (wash trading), linked accounts (shared IP),
/// and price anomalies. Suspicious trades are completed (the buyer paid) but excluded from valuation and queued for review.</summary>
public sealed class FraudService(PetStarDb db, GameData data)
{
    public async Task<FraudAssessment> AssessTrade(int serverId, long sellerMid, long buyerMid, long cardId, long price, long initialValue)
    {
        var f = data.Valuation.Raw["fraud"]!;
        var reasons = new List<string>();
        var now = DateTimeOffset.UtcNow;
        var pairSince = now.AddDays(-f["pairTradesWindowDays"]!.GetValue<int>());
        int pair = await db.MarketTransactions.CountAsync(t => t.At >= pairSince &&
            ((t.SellerMembershipId == sellerMid && t.BuyerMembershipId == buyerMid) || (t.SellerMembershipId == buyerMid && t.BuyerMembershipId == sellerMid)));
        if (pair >= f["maxPairTradesBeforeFlag"]!.GetValue<int>()) reasons.Add("repeated_pair_trading");
        var rtSince = now.AddDays(-f["roundTripWindowDays"]!.GetValue<int>());
        if (await db.CardOwnership.AnyAsync(h => h.CardId == cardId && h.At >= rtSince && h.FromMembershipId == buyerMid && h.ToMembershipId == sellerMid))
            reasons.Add("round_trip");
        if (await LinkedAccounts(sellerMid, buyerMid)) reasons.Add("linked_accounts");
        double maxDev = data.Valuation.M("market", "maxDeviationFromInitial");
        if (initialValue > 0 && (price > initialValue * maxDev || price * maxDev < initialValue)) reasons.Add("price_anomaly");
        return new FraudAssessment(reasons.Count > 0, reasons);
    }

    /// <summary>Two memberships whose accounts used the same public IP recently (loopback/private ignored).</summary>
    public async Task<bool> LinkedAccounts(long midA, long midB)
    {
        var f = data.Valuation.Raw["fraud"]!;
        var since = DateTimeOffset.UtcNow.AddDays(-f["sharedIpWindowDays"]!.GetValue<int>());
        var accounts = await db.Memberships.AsNoTracking().Where(m => m.Id == midA || m.Id == midB).Select(m => m.AccountId).ToListAsync();
        if (accounts.Count < 2 || accounts[0] == accounts[1]) return accounts.Count == 2;
        var ips = await db.Sessions.AsNoTracking().Where(s => accounts.Contains(s.AccountId) && s.LastUsedAt >= since && s.Ip != null)
            .Select(s => new { s.AccountId, s.Ip }).Distinct().ToListAsync();
        var shared = ips.GroupBy(x => x.Ip).Where(g => g.Select(x => x.AccountId).Distinct().Count() > 1).Select(g => g.Key!);
        return shared.Any(ip => IPAddress.TryParse(ip, out var a) && !IPAddress.IsLoopback(a) && !IsPrivate(a));
    }

    private static bool IsPrivate(IPAddress a)
    {
        var b = a.MapToIPv4().GetAddressBytes();
        return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 100 && b[1] >= 64 && b[1] <= 127);
    }

    public void Record(int serverId, string type, string severity, long? membershipId, long? cardId, string? refId, object detail) =>
        db.EconomyAudit.Add(new EconomyAuditEvent
        {
            ServerId = serverId, Type = type, Severity = severity, MembershipId = membershipId, CardId = cardId, RefId = refId,
            DataJson = JsonSerializer.Serialize(detail, IdempotencyService.Json), At = DateTimeOffset.UtcNow,
        });
}

/// <summary>Moderation restrictions + basic content filtering.</summary>
public sealed partial class ContentGuard(PetStarDb db)
{
    // Small built-in list (en/vi); production should plug in a maintained moderation list/service.
    private static readonly string[] Blocked = { "fuck", "shit", "bitch", "nigger", "faggot", "đụ", "địt", "đéo", "lồn", "cặc", "đĩ" };

    [GeneratedRegex(@"(https?://|www\.|\b[\w-]+\.(com|net|org|io|gg|vn|xyz|ru)\b)", RegexOptions.IgnoreCase)]
    private static partial Regex LinkPattern();

    [GeneratedRegex(@"([^\s])\1{9,}")]
    private static partial Regex RepeatedChar();

    public static string Clean(string? text, int max, bool required)
    {
        text = (text ?? "").Trim();
        if (required && text.Length == 0) throw ApiException.BadRequest("text_required");
        if (text.Length > max) throw ApiException.BadRequest("text_too_long");
        if (text.Any(c => char.IsControl(c) && c != '\n')) throw ApiException.BadRequest("text_invalid");
        var lower = text.ToLowerInvariant();
        if (Blocked.Any(w => lower.Contains(w))) throw ApiException.BadRequest("text_inappropriate");
        if (LinkPattern().IsMatch(text)) throw ApiException.BadRequest("text_links_not_allowed");   // also blocks off-platform trading links
        if (RepeatedChar().IsMatch(text)) throw ApiException.BadRequest("text_spam");
        return text;
    }

    public async Task EnsureAllowed(long accountId, string scope)
    {
        var now = DateTimeOffset.UtcNow;
        if (await db.Restrictions.AnyAsync(r => r.AccountId == accountId && r.Until > now && (r.Scope == scope || r.Scope == "all")))
            throw ApiException.Forbidden("account_restricted_" + scope);
    }
}
