using StackExchange.Redis;

namespace Aveline.Api.Common.Jobs;

/// <summary>
/// Redis-backed job lock. Acquisition uses <c>SET key value NX PX</c> through
/// <see cref="When.NotExists"/> so it is atomic across instances; release uses a
/// compare-and-delete script so a stale holder cannot evict a newer one.
/// </summary>
public sealed class RedisDistributedJobLock : IDistributedJobLock
{
    private const string KeyPrefix = "aveline:job-lock:";

    // KEYS[1] = lock key, ARGV[1] = holder token.
    private const string ReleaseScript =
        "if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('del', KEYS[1]) else return 0 end";

    private static readonly TimeSpan DefaultLease = TimeSpan.FromSeconds(300);

    private readonly IConnectionMultiplexer _multiplexer;
    private readonly TimeSpan _defaultLease;

    public RedisDistributedJobLock(IConnectionMultiplexer multiplexer, TimeSpan? defaultLease = null)
    {
        _multiplexer = multiplexer;
        _defaultLease = defaultLease ?? DefaultLease;
    }

    public async Task<IAsyncDisposable?> TryAcquireAsync(
        string jobName,
        TimeSpan? leaseDuration = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobName);

        var key = KeyPrefix + jobName;
        var token = Guid.NewGuid().ToString("N");
        var lease = leaseDuration ?? _defaultLease;

        var acquired = await _multiplexer.GetDatabase().StringSetAsync(
            key,
            token,
            lease,
            When.NotExists);

        return acquired ? new Handle(_multiplexer, key, token) : null;
    }

    private sealed class Handle(IConnectionMultiplexer multiplexer, string key, string token) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await multiplexer.GetDatabase().ScriptEvaluateAsync(
                ReleaseScript,
                [key],
                [token]);
        }
    }
}
