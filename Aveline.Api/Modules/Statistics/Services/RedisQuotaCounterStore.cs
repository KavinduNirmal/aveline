using StackExchange.Redis;

namespace Aveline.Api.Modules.Statistics.Services;

/// <summary>
/// Redis counter store. The increment and first-write expiry are one server-side script, so
/// two instances can never race a read-modify-write (BR-6.7 — unlike
/// <c>DistributedRateLimiter</c>, this path is billing-relevant and must be atomic).
/// </summary>
public sealed class RedisQuotaCounterStore(IConnectionMultiplexer multiplexer) : IQuotaCounterStore
{
    public const string KeyPrefix = "aveline:quota:";

    // KEYS[1] = counter key, ARGV[1] = delta, ARGV[2] = ttl milliseconds.
    private const string IncrementScript =
        """
        local v = redis.call('INCRBY', KEYS[1], ARGV[1])
        if v == tonumber(ARGV[1]) then
            redis.call('PEXPIRE', KEYS[1], ARGV[2])
        end
        return v
        """;

    public async Task<long> IncrementAsync(
        string key, long amount, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        var result = await multiplexer.GetDatabase().ScriptEvaluateAsync(
            IncrementScript,
            [KeyPrefix + key],
            [amount, (long)Math.Max(1, ttl.TotalMilliseconds)]);

        return (long)result;
    }

    public async Task<long> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        var value = await multiplexer.GetDatabase().StringGetAsync(KeyPrefix + key);
        return value.IsNull ? 0 : (long)value;
    }

    public Task<bool> TrySetFlagAsync(
        string key, TimeSpan ttl, CancellationToken cancellationToken = default) =>
        multiplexer.GetDatabase().StringSetAsync(KeyPrefix + key, "1", ttl, When.NotExists);
}
