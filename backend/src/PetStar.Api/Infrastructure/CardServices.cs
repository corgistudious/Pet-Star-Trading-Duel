using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using PetStar.Api.Data;
using PetStar.Api.Features;
using PetStar.Rules;

namespace PetStar.Api.Infrastructure;

public static class SerialGenerator
{
    public const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
    public const int Length = 12;

    /// <summary>12 chars from A-Z0-9 using the OS CSPRNG (uniform, no modulo bias): ~62 bits of entropy.</summary>
    public static string Next()
    {
        Span<char> c = stackalloc char[Length];
        for (int i = 0; i < Length; i++) c[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return new string(c);
    }

    public static bool IsValid(string? s) => s != null && s.Length == Length && s.All(ch => Alphabet.Contains(ch));
}

public sealed class ValuationConfig
{
    public required JsonObject Raw { get; init; }
    public long Base(string currency, string rarity) => Raw["base"]![currency]![rarity]!.GetValue<long>();
    public double Quality(string q) => Raw["quality"]![q]!.GetValue<double>();
    public double Finish(string f) => Raw["finish"]![f]!.GetValue<double>();
    public double Stage(PetFormDef f) => Raw["stage"]![f.FamilyStages == 1 ? "standalone" : f.Stage.ToString()]!.GetValue<double>();
    public double Origin(string o) => Raw["origin"]![o]?.GetValue<double>() ?? 1.0;
    public double M(string section, string key) => Raw[section]![key]!.GetValue<double>();
}

public sealed record ValueEstimate(string Currency, long Initial, long Estimated, int Samples, string Confidence);

/// <summary>Reference values. Initial = formula (rarity, quality, finish, stage, origin, real supply). Estimated = blend of
/// the initial value with trimmed completed-trade prices (suspicious trades excluded; listings never count).</summary>
public sealed class ValuationService(PetStarDb db, GameData data)
{
    private ValuationConfig C => data.Valuation;

    public long Initial(string currency, string formId, string rarity, string quality, string finish, string origin, int supply)
    {
        var form = data.Catalog.Form(formId);
        var sc = C.Raw["scarcity"]!;
        double target = sc["targetSupplyPerFormRarity"]!.GetValue<double>();
        double scarcity = Math.Clamp(Math.Pow(target / Math.Max(1, supply), sc["exponent"]!.GetValue<double>()), sc["min"]!.GetValue<double>(), sc["max"]!.GetValue<double>());
        double v = C.Base(currency, rarity) * C.Quality(quality) * C.Finish(finish) * C.Stage(form) * C.Origin(origin) * scarcity;
        return Math.Max(1, (long)Math.Round(v));
    }

    public double VariantMultiplier(string quality, string finish) => C.Quality(quality) * C.Finish(finish);

    public async Task<int> Supply(int serverId, string formId, string rarity) =>
        await db.Cards.CountAsync(c => c.ServerId == serverId && c.FormId == formId && c.Rarity == rarity);

    public async Task<ValueEstimate> Estimate(int serverId, CardInstance card, string currency, long? initialOverride = null)
    {
        int supply = await Supply(serverId, card.FormId, card.Rarity);
        long initial = initialOverride ?? Initial(currency, card.FormId, card.Rarity, card.Quality, card.Finish, card.Origin, supply);
        var m = C.Raw["market"]!;
        var since = DateTimeOffset.UtcNow.AddDays(-m["windowDays"]!.GetValue<int>());
        var rows = await db.PriceHistory.AsNoTracking()
            .Where(p => p.ServerId == serverId && p.FormId == card.FormId && p.Rarity == card.Rarity && p.Currency == currency && !p.Excluded && p.At >= since)
            .Join(db.MarketTransactions, p => p.TransactionId, t => t.Id, (p, t) => new { p.NormalizedPrice, t.BuyerMembershipId, t.SellerMembershipId })
            .ToListAsync();
        int traders = rows.SelectMany(r => new[] { r.BuyerMembershipId, r.SellerMembershipId }).Distinct().Count();
        if (rows.Count == 0 || traders < m["minDistinctTraders"]!.GetValue<int>())
            return new ValueEstimate(currency, initial, initial, rows.Count, rows.Count == 0 ? "initial_only" : "low");
        var sorted = rows.Select(r => (double)r.NormalizedPrice).OrderBy(x => x).ToList();
        int trim = (int)Math.Floor(sorted.Count * m["trimPct"]!.GetValue<double>());
        var core = sorted.Skip(trim).Take(Math.Max(1, sorted.Count - 2 * trim)).ToList();
        double marketNormalized = core.Average();
        double market = marketNormalized * VariantMultiplier(card.Quality, card.Finish);
        double w = Math.Min(1.0, rows.Count / m["fullConfidenceSamples"]!.GetValue<double>());
        long est = Math.Max(1, (long)Math.Round(w * market + (1 - w) * initial));
        string conf = rows.Count >= m["fullConfidenceSamples"]!.GetValue<int>() ? "high" : rows.Count >= 5 ? "medium" : "low";
        return new ValueEstimate(currency, initial, est, rows.Count, conf);
    }

    /// <summary>Upserts today's snapshot for every (form, rarity, currency) that has cards on the server.</summary>
    public async Task<int> SnapshotServer(int serverId)
    {
        var groups = await db.Cards.AsNoTracking().Where(c => c.ServerId == serverId)
            .GroupBy(c => new { c.FormId, c.Rarity }).Select(g => new { g.Key.FormId, g.Key.Rarity, N = g.Count() }).ToListAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        int n = 0;
        foreach (var g in groups)
            foreach (var cur in Currency.All)
            {
                var probe = new CardInstance { FormId = g.FormId, Rarity = g.Rarity, Quality = "B", Finish = "Normal", Origin = "pack" };
                var e = await Estimate(serverId, probe, cur);
                await db.Database.ExecuteSqlRawAsync(
                    "INSERT INTO card_valuation_snapshots (server_id, form_id, rarity, currency, day, initial_reference, estimated_value, sample_size, confidence, supply, computed_at) " +
                    "VALUES ({0},{1},{2},{3},{4},{5},{6},{7},{8},{9},now()) ON CONFLICT (server_id, form_id, rarity, currency, day) DO UPDATE SET " +
                    "initial_reference = EXCLUDED.initial_reference, estimated_value = EXCLUDED.estimated_value, sample_size = EXCLUDED.sample_size, confidence = EXCLUDED.confidence, supply = EXCLUDED.supply, computed_at = now()",
                    serverId, g.FormId, g.Rarity, cur, today, e.Initial, e.Estimated, e.Samples, e.Confidence, g.N);
                n++;
            }
        return n;
    }
}

public sealed record NewCard(string FormId, string Rarity, string Quality, string Finish);

/// <summary>The only way cards are created: serial reserved in the permanent registry (collision → retry), passport
/// written once, ownership history, codex discovery. Must run inside the caller's transaction.</summary>
public sealed class CardFactory(PetStarDb db, ValuationService valuation)
{
    public int SerialCollisions { get; private set; }

    public async Task<List<CardInstance>> Create(int serverId, long membershipId, IReadOnlyList<NewCard> specs, string origin, string originRef)
    {
        if (db.Database.CurrentTransaction == null) throw new InvalidOperationException("CardFactory requires a transaction");
        var now = DateTimeOffset.UtcNow;
        var cards = new List<CardInstance>();
        foreach (var s in specs)
        {
            cards.Add(new CardInstance
            {
                ServerId = serverId, OwnerMembershipId = membershipId, FormId = s.FormId, Rarity = s.Rarity, Quality = s.Quality, Finish = s.Finish,
                Level = 1, SkillLevel = 1, Origin = origin, OriginRef = originRef, CreatedAt = now, UpdatedAt = now, Serial = await ReserveSerial(),
            });
        }
        db.Cards.AddRange(cards);
        await db.SaveChangesAsync();
        foreach (var c in cards)
        {
            await db.Database.ExecuteSqlRawAsync("UPDATE card_serial_registry SET card_id = {0} WHERE serial = {1}", c.Id, c.Serial);
            int supply = await valuation.Supply(serverId, c.FormId, c.Rarity);
            db.Passports.Add(new CardPassport
            {
                CardId = c.Id, Serial = c.Serial, IssuedServerId = serverId, Origin = origin, OriginRef = originRef, FormIdAtIssue = c.FormId,
                Rarity = c.Rarity, Quality = c.Quality, Finish = c.Finish, IssuedAt = now,
                InitialValueBeans = valuation.Initial(Currency.MiniGreenBean, c.FormId, c.Rarity, c.Quality, c.Finish, origin, supply),
                InitialValueStars = valuation.Initial(Currency.RainbowStar, c.FormId, c.Rarity, c.Quality, c.Finish, origin, supply),
            });
            db.CardOwnership.Add(new CardOwnershipHistory { CardId = c.Id, ServerId = serverId, ToMembershipId = membershipId, Reason = origin, RefId = originRef, At = now });
        }
        await db.SaveChangesAsync();
        foreach (var c in cards) await GameData.DiscoverCodex(db, membershipId, c.FormId, c.Id);
        return cards;
    }

    private async Task<string> ReserveSerial()
    {
        for (int attempt = 0; attempt < 16; attempt++)
        {
            var serial = SerialGenerator.Next();
            int n = await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO card_serial_registry (serial, issued_at) VALUES ({0}, now()) ON CONFLICT DO NOTHING", serial);
            if (n == 1) return serial;
            SerialCollisions++;
        }
        throw new InvalidOperationException("serial space exhausted (should never happen)");
    }
}

/// <summary>Moves a card to a new owner (market sale / auction). Clears business locks, favorite/lock flags, decks,
/// showcase and albums of the previous owner; records history. Caller must hold FOR UPDATE on the card.</summary>
public static class CardTransfer
{
    public static async Task Transfer(PetStarDb db, CardInstance card, long toMembershipId, string reason, string refId)
    {
        long from = card.OwnerMembershipId;
        var now = DateTimeOffset.UtcNow;
        card.OwnerMembershipId = toMembershipId;
        card.BusyState = null; card.IsFavorite = false; card.IsLocked = false; card.UpdatedAt = now;
        db.CardOwnership.Add(new CardOwnershipHistory { CardId = card.Id, ServerId = card.ServerId, FromMembershipId = from, ToMembershipId = toMembershipId, Reason = reason, RefId = refId, At = now });
        await db.SaveChangesAsync();
        await db.DeckSlots.Where(s => s.CardId == card.Id).ExecuteDeleteAsync();
        await db.Showcase.Where(s => s.CardId == card.Id).ExecuteDeleteAsync();
        await db.CollectionItems.Where(i => i.CardId == card.Id).ExecuteDeleteAsync();
        await GameData.DiscoverCodex(db, toMembershipId, card.FormId, card.Id);
    }
}
