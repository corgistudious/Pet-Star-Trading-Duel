using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using StackExchange.Redis;

namespace PetStar.Api.Infrastructure;

public sealed record RealtimeTicket(long AccountId, int ServerId, long MembershipId, DateTimeOffset Expires);

/// <summary>One-time WebSocket tickets (60 s). Redis-backed when configured so any API instance can accept them.</summary>
public sealed class TicketStore(IServiceProvider sp)
{
    private readonly ConcurrentDictionary<string, RealtimeTicket> _mem = new();
    private IDatabase? Redis => sp.GetService<IConnectionMultiplexer>()?.GetDatabase();

    public async Task<string> Issue(long accountId, int serverId, long membershipId)
    {
        var t = SecureRandomSource.Token(32);
        var ticket = new RealtimeTicket(accountId, serverId, membershipId, DateTimeOffset.UtcNow.AddSeconds(60));
        if (Redis is { } r) await r.StringSetAsync("ws_ticket:" + t, JsonSerializer.Serialize(ticket), TimeSpan.FromSeconds(60));
        else _mem[t] = ticket;
        return t;
    }

    public async Task<RealtimeTicket?> Consume(string ticket)
    {
        if (string.IsNullOrEmpty(ticket) || ticket.Length > 100) return null;
        if (Redis is { } r)
        {
            var v = await r.StringGetDeleteAsync("ws_ticket:" + ticket);
            return v.IsNullOrEmpty ? null : JsonSerializer.Deserialize<RealtimeTicket>(v.ToString());
        }
        return _mem.TryRemove(ticket, out var t) && t.Expires > DateTimeOffset.UtcNow ? t : null;
    }
}

/// <summary>
/// Server → client push over WebSocket (chat, notifications, live PvP). Actions always go through authenticated REST.
/// With Redis, messages fan out through pub/sub so every API instance delivers to its own connections.
/// </summary>
public sealed class RealtimeHub
{
    private const string Channel = "petstar:realtime";
    private readonly ConcurrentDictionary<long, ConcurrentDictionary<Guid, (WebSocket ws, int server)>> _byMember = new();
    private readonly ISubscriber? _sub;
    private readonly ILogger<RealtimeHub> _log;

    private sealed record Envelope(string Kind, long[]? Members, int? Server, string Payload);

    public RealtimeHub(IServiceProvider sp, ILogger<RealtimeHub> log)
    {
        _log = log;
        var redis = sp.GetService<IConnectionMultiplexer>();
        if (redis != null)
        {
            _sub = redis.GetSubscriber();
            _sub.Subscribe(RedisChannel.Literal(Channel), (ch, msg) =>
            {
                var env = JsonSerializer.Deserialize<Envelope>(msg.ToString());
                if (env != null) _ = DeliverLocal(env);
            });
        }
    }

    public int ConnectionCount => _byMember.Values.Sum(v => v.Count);
    public bool IsOnline(long membershipId) => _byMember.TryGetValue(membershipId, out var c) && !c.IsEmpty;

    public Task ToMembers(IEnumerable<long> members, string type, object data) =>
        Publish(new Envelope("members", members.Distinct().ToArray(), null, Serialize(type, data)));

    public Task ToServer(int serverId, string type, object data) =>
        Publish(new Envelope("server", null, serverId, Serialize(type, data)));

    private static string Serialize(string type, object data) =>
        JsonSerializer.Serialize(new { type, data, at = DateTimeOffset.UtcNow }, IdempotencyService.Json);

    private async Task Publish(Envelope env)
    {
        if (_sub != null) await _sub.PublishAsync(RedisChannel.Literal(Channel), JsonSerializer.Serialize(env));
        else await DeliverLocal(env);
    }

    private async Task DeliverLocal(Envelope env)
    {
        var bytes = Encoding.UTF8.GetBytes(env.Payload);
        IEnumerable<(WebSocket ws, int server)> targets = env.Kind == "server"
            ? _byMember.Values.SelectMany(d => d.Values).Where(x => x.server == env.Server)
            : (env.Members ?? Array.Empty<long>()).SelectMany(m => _byMember.TryGetValue(m, out var d) ? d.Values : Enumerable.Empty<(WebSocket, int)>());
        foreach (var (ws, _) in targets.ToList())
        {
            if (ws.State != WebSocketState.Open) continue;
            try { await ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None); }
            catch (Exception ex) { _log.LogDebug(ex, "ws send failed"); }
        }
    }

    /// <summary>Runs one connection until it closes. Clients may send "ping" (answered with "pong").</summary>
    public async Task Run(WebSocket ws, RealtimeTicket t, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        var conns = _byMember.GetOrAdd(t.MembershipId, _ => new());
        conns[id] = (ws, t.ServerId);
        var buffer = new byte[1024];
        try
        {
            await ws.SendAsync(Encoding.UTF8.GetBytes(Serialize("hello", new { t.ServerId })), WebSocketMessageType.Text, true, ct);
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var r = await ws.ReceiveAsync(buffer, ct);
                if (r.MessageType == WebSocketMessageType.Close) break;
                if (r.Count == 4 && Encoding.UTF8.GetString(buffer, 0, 4) == "ping")
                    await ws.SendAsync(Encoding.UTF8.GetBytes("pong"), WebSocketMessageType.Text, true, ct);
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException) { }
        finally
        {
            conns.TryRemove(id, out _);
            // complete the close handshake whether we or the client initiated it
            if (ws.State is WebSocketState.Open or WebSocketState.CloseReceived)
                try { await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None); } catch { /* ignore */ }
        }
    }
}
