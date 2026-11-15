using System.Collections.Concurrent;
using StackExchange.Redis;

namespace PetStar.Api.Infrastructure;

/// <summary>Online-player tracking per server (sorted set of membership ids scored by last-seen unix time).</summary>
public interface IPresence
{
    Task Touch(int serverId, long membershipId);
    Task<int> OnlineCount(int serverId);
}

public sealed class RedisPresence(IConnectionMultiplexer redis) : IPresence
{
    private const int WindowSeconds = 120;

    public async Task Touch(int serverId, long membershipId)
    {
        var db = redis.GetDatabase();
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await db.SortedSetAddAsync($"presence:{serverId}", membershipId, now, flags: CommandFlags.FireAndForget);
    }

    public async Task<int> OnlineCount(int serverId)
    {
        var db = redis.GetDatabase();
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await db.SortedSetRemoveRangeByScoreAsync($"presence:{serverId}", 0, now - WindowSeconds);
        return (int)await db.SortedSetLengthAsync($"presence:{serverId}");
    }
}

/// <summary>Fallback for single-instance development when Redis is not configured.</summary>
public sealed class MemoryPresence : IPresence
{
    private readonly ConcurrentDictionary<(int, long), DateTimeOffset> _seen = new();
    public Task Touch(int serverId, long membershipId) { _seen[(serverId, membershipId)] = DateTimeOffset.UtcNow; return Task.CompletedTask; }
    public Task<int> OnlineCount(int serverId)
    {
        var cutoff = DateTimeOffset.UtcNow.AddSeconds(-120);
        return Task.FromResult(_seen.Count(kv => kv.Key.Item1 == serverId && kv.Value > cutoff));
    }
}
