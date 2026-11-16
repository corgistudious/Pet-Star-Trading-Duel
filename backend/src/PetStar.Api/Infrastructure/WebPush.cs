using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using PetStar.Api.Data;
using PetStar.Api.Features;

namespace PetStar.Api.Infrastructure;

public sealed class WebPushOptions
{
    public string PublicKey { get; set; } = "";
    public string PrivateKey { get; set; } = "";
    public string Subject { get; set; } = "";
    public int MaxPerAccountPer10Min { get; set; } = 20;
    public bool Enabled => PublicKey.Length > 0 && PrivateKey.Length > 0 && Subject.Length > 0;
}

/// <summary>Push texts + category per notification type (database/seed/push_texts.json).</summary>
public sealed class PushTexts
{
    public sealed record Entry(string Category, string Url, string[] En, string[] Vi);
    public Dictionary<string, Entry> Types { get; } = new();
    public string[] Categories { get; private set; } = Array.Empty<string>();
    private Dictionary<string, Dictionary<string, string>> _currencies = new();

    public static PushTexts Load(string dir)
    {
        var n = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "push_texts.json")))!;
        var t = new PushTexts { Categories = n["categories"]!.AsArray().Select(x => x!.GetValue<string>()).ToArray() };
        foreach (var (k, v) in n["types"]!.AsObject())
        {
            var e = new Entry(v!["category"]!.GetValue<string>(), v["url"]!.GetValue<string>(),
                v["en"]!.AsArray().Select(x => x!.GetValue<string>()).ToArray(), v["vi"]!.AsArray().Select(x => x!.GetValue<string>()).ToArray());
            if (!t.Categories.Contains(e.Category) || e.En.Length != 2 || e.Vi.Length != 2 || !e.Url.StartsWith('/'))
                throw new InvalidDataException("push_texts: invalid entry " + k);
            t.Types[k] = e;
        }
        t._currencies = n["currencies"]!.Deserialize<Dictionary<string, Dictionary<string, string>>>()!;
        return t;
    }

    public string Currency(string lang, string id) => _currencies.GetValueOrDefault(lang)?.GetValueOrDefault(id) ?? id;

    /// <summary>Fills {placeholders} from the notification data. Unknown placeholders are dropped, never shown raw.</summary>
    public static string Fill(string template, IReadOnlyDictionary<string, string> values) =>
        System.Text.RegularExpressions.Regex.Replace(template, @"\{(\w+)\}", m => values.TryGetValue(m.Groups[1].Value, out var v) ? v : "");
}

/// <summary>Only well-known browser push services are accepted as endpoints. This keeps the server from being used
/// to send requests to arbitrary hosts (SSRF) through a forged subscription.</summary>
public static class PushEndpointPolicy
{
    private static readonly string[] Exact = { "fcm.googleapis.com", "updates.push.services.mozilla.com", "web.push.apple.com" };
    private static readonly string[] Suffix = { ".push.apple.com", ".notify.windows.com", ".push.services.mozilla.com" };

    public static bool IsAllowed(string endpoint) =>
        endpoint.Length <= 1024 && Uri.TryCreate(endpoint, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps && u.IsDefaultPort
        && string.IsNullOrEmpty(u.UserInfo) && (Exact.Contains(u.Host) || Suffix.Any(s => u.Host.EndsWith(s, StringComparison.Ordinal)));
}

/// <summary>DedupeKey identifies the stored notification row: the push is only sent once that row is committed.</summary>
public sealed record PushJob(long MembershipId, string Type, JsonNode? Data, string? DedupeKey = null, bool IgnorePresence = false)
{
    public DateTimeOffset EnqueuedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>Queue between request handlers and the sender. Enqueue never blocks a request; a full queue drops pushes
/// (the in-app notification row is already stored, so nothing is lost).</summary>
public sealed class PushQueue
{
    private readonly Channel<PushJob> _ch = Channel.CreateBounded<PushJob>(new BoundedChannelOptions(5000) { FullMode = BoundedChannelFullMode.DropWrite });
    public void Enqueue(PushJob job) => _ch.Writer.TryWrite(job);
    public ChannelReader<PushJob> Reader => _ch.Reader;
}

public sealed class WebPushSender(IHttpClientFactory http, WebPushOptions opt, ILogger<WebPushSender> log)
{
    private ECDsa? _vapid;
    private ECDsa Vapid => _vapid ??= ECDsa.Create(WebPushCrypto.Params(WebPushCrypto.FromB64Url(opt.PublicKey), WebPushCrypto.FromB64Url(opt.PrivateKey)));

    public enum Result { Delivered, Gone, Failed }

    public async Task<Result> Send(WebPushSubscription s, object payload, string topic, CancellationToken ct)
    {
        if (!PushEndpointPolicy.IsAllowed(s.Endpoint)) return Result.Gone;
        byte[] body = WebPushCrypto.Encrypt(JsonSerializer.SerializeToUtf8Bytes(payload, IdempotencyService.Json),
            WebPushCrypto.FromB64Url(s.P256dh), WebPushCrypto.FromB64Url(s.Auth));
        using var req = new HttpRequestMessage(HttpMethod.Post, s.Endpoint) { Content = new ByteArrayContent(body) };
        req.Content.Headers.ContentType = new("application/octet-stream");
        req.Content.Headers.ContentEncoding.Add("aes128gcm");
        req.Headers.TryAddWithoutValidation("TTL", "86400");
        req.Headers.TryAddWithoutValidation("Urgency", "normal");
        req.Headers.TryAddWithoutValidation("Topic", topic); // newer push with the same topic replaces an undelivered one
        req.Headers.TryAddWithoutValidation("Authorization", WebPushCrypto.VapidAuthorization(s.Endpoint, opt.Subject, Vapid, opt.PublicKey, DateTimeOffset.UtcNow));
        try
        {
            using var res = await http.CreateClient("webpush").SendAsync(req, ct);
            if (res.IsSuccessStatusCode) return Result.Delivered;
            if (res.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone) return Result.Gone; // unsubscribed / expired
            log.LogInformation("push rejected {Status} by {Host}", (int)res.StatusCode, req.RequestUri!.Host);
            return Result.Failed;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            log.LogInformation("push transport error to {Host}: {Msg}", req.RequestUri!.Host, ex.Message);
            return Result.Failed;
        }
    }
}

/// <summary>Turns stored notifications into Web Push messages for players who are NOT connected right now
/// (connected players already got the realtime in-app notification).</summary>
public sealed class PushDispatcher(IServiceProvider sp, PushQueue queue, WebPushOptions opt, ILogger<PushDispatcher> log) : BackgroundService
{
    private readonly ConcurrentDictionary<long, Queue<DateTimeOffset>> _recent = new();
    private readonly SemaphoreSlim _parallel = new(16);

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        await foreach (var job in queue.Reader.ReadAllAsync(stop))
        {
            if (!opt.Enabled) continue;
            // Run each job on its own so the commit-wait of one never delays the others.
            await _parallel.WaitAsync(stop);
            _ = Task.Run(async () =>
            {
                try { await Handle(job, stop); }
                catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning(ex, "push job failed ({Type})", job.Type); }
                finally { _parallel.Release(); }
            }, stop);
        }
    }

    public static TimeSpan CommitWait = TimeSpan.FromMilliseconds(1500);

    /// <summary>Notifications are written inside request transactions that may still roll back: wait, then send only
    /// if the notification row is visible (committed). One re-check later, then the push is dropped.</summary>
    private async Task<bool> Committed(PetStarDb db, PushJob job, CancellationToken ct)
    {
        if (job.DedupeKey == null) return true;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            var due = job.EnqueuedAt + CommitWait * (attempt == 0 ? 1 : 4);
            var wait = due - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
            if (await db.Notifications.AsNoTracking().AnyAsync(n => n.MembershipId == job.MembershipId && n.DedupeKey == job.DedupeKey, ct)) return true;
        }
        return false;
    }

    public async Task<int> Handle(PushJob job, CancellationToken ct)
    {
        using var scope = sp.CreateScope();
        var texts = scope.ServiceProvider.GetRequiredService<PushTexts>();
        if (!texts.Types.TryGetValue(job.Type, out var entry)) return 0;
        var db = scope.ServiceProvider.GetRequiredService<PetStarDb>();
        var hub = scope.ServiceProvider.GetRequiredService<RealtimeHub>();
        if (!await Committed(db, job, ct)) return 0;
        if (!job.IgnorePresence && await hub.IsConnectedAnywhere(job.MembershipId)) return 0;

        var m = await db.Memberships.AsNoTracking().FirstOrDefaultAsync(x => x.Id == job.MembershipId, ct);
        if (m == null) return 0;
        var subs = await db.PushSubscriptions.Where(s => s.AccountId == m.AccountId).ToListAsync(ct);
        subs = subs.Where(s => s.Categories.Split(',').Contains(entry.Category)).ToList();
        if (subs.Count == 0 || !Allow(m.AccountId, job.IgnorePresence)) return 0;

        var acc = await db.Accounts.AsNoTracking().FirstAsync(a => a.Id == m.AccountId, ct);
        var server = await db.Servers.AsNoTracking().FirstAsync(s => s.Id == m.ServerId, ct);
        var data = scope.ServiceProvider.GetRequiredService<GameData>();
        var payload = Compose(texts, entry, job, acc.Language == "vi" ? "vi" : "en", server.Code, data);

        var sender = scope.ServiceProvider.GetRequiredService<WebPushSender>();
        int delivered = 0;
        foreach (var s in subs)
        {
            var r = await sender.Send(s, payload, job.Type.Length > 32 ? job.Type[..32] : job.Type, ct);
            if (r == WebPushSender.Result.Delivered) { s.LastSuccessAt = DateTimeOffset.UtcNow; s.FailureCount = 0; delivered++; }
            else if (r == WebPushSender.Result.Gone || ++s.FailureCount >= 10) db.PushSubscriptions.Remove(s);
        }
        await db.SaveChangesAsync(ct);
        return delivered;
    }

    public static object Compose(PushTexts texts, PushTexts.Entry entry, PushJob job, string lang, string serverCode, GameData data)
    {
        var values = new Dictionary<string, string> { ["server"] = serverCode };
        if (job.Data is JsonObject o)
            foreach (var (k, v) in o)
                if (v is JsonValue jv) values[k] = jv.ToString();
        if (values.TryGetValue("formId", out var form)) values["pet"] = data.Catalog.Forms.TryGetValue(form, out var f) ? f.Name : form;
        else values["pet"] = lang == "vi" ? "Thẻ" : "Card";
        if (values.TryGetValue("currency", out var cur)) values["currency"] = texts.Currency(lang, cur);
        foreach (var k in new[] { "price", "net", "refunded" })
            if (values.TryGetValue(k, out var num) && long.TryParse(num, out var n)) values[k] = n.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
        var t = lang == "vi" ? entry.Vi : entry.En;
        string url = PushTexts.Fill(entry.Url, values);
        if (job.Type == "wishlist_match" && values.TryGetValue("refId", out var rid))
            url = values.GetValueOrDefault("kind") == "auction" ? $"/auctions/{rid}" : $"/market/{rid}";
        if (url.Contains("//") || !url.StartsWith('/') || url.EndsWith('/')) url = "/";
        return new { title = PushTexts.Fill(t[0], values), body = PushTexts.Fill(t[1], values), url, tag = job.Type, type = job.Type, server = serverCode };
    }

    /// <summary>Per-account cap so a burst (e.g. many outbids) never floods a phone. Test pushes are exempt.</summary>
    private bool Allow(long accountId, bool exempt)
    {
        if (exempt) return true;
        var q = _recent.GetOrAdd(accountId, _ => new Queue<DateTimeOffset>());
        lock (q)
        {
            var now = DateTimeOffset.UtcNow;
            while (q.Count > 0 && now - q.Peek() > TimeSpan.FromMinutes(10)) q.Dequeue();
            if (q.Count >= opt.MaxPerAccountPer10Min) return false;
            q.Enqueue(now);
            return true;
        }
    }
}
