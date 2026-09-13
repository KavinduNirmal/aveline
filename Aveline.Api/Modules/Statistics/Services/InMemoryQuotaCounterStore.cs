using System.Collections.Concurrent;

namespace Aveline.Api.Modules.Statistics.Services;

/// <summary>
/// Process-local quota counter store used when Redis is not configured (local development,
/// tests). The clock is injectable and entries expire on read, mirroring
/// <c>InMemoryDistributedJobLock</c>. It is deliberately not shared across instances; the
/// production path is <see cref="RedisQuotaCounterStore"/>.
/// </summary>
public sealed class InMemoryQuotaCounterStore(TimeProvider? timeProvider = null) : IQuotaCounterStore
{
    private readonly ConcurrentDictionary<string, Entry> _counters = new(StringComparer.Ordinal);
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public Task<long> IncrementAsync(
        string key, long amount, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        var now = _time.GetUtcNow();
        var entry = _counters.AddOrUpdate(
            key,
            _ => new Entry(amount, now + ttl),
            (_, existing) => existing.ExpiresAt <= now
                ? new Entry(amount, now + ttl)
                : existing with { Value = existing.Value + amount });

        return Task.FromResult(entry.Value);
    }

    public Task<long> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        if (_counters.TryGetValue(key, out var entry) && entry.ExpiresAt > _time.GetUtcNow())
        {
            return Task.FromResult(entry.Value);
        }

        return Task.FromResult(0L);
    }

    public Task<bool> TrySetFlagAsync(
        string key, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        var now = _time.GetUtcNow();
        if (_counters.TryGetValue(key, out var existing) && existing.ExpiresAt > now)
        {
            return Task.FromResult(false);
        }

        var added = _counters.TryAdd(key, new Entry(1, now + ttl));
        if (!added)
        {
            // Another writer won the race; replace only if the existing entry expired.
            added = _counters.TryUpdate(key, new Entry(1, now + ttl), existing);
        }

        return Task.FromResult(added);
    }

    private readonly record struct Entry(long Value, DateTimeOffset ExpiresAt);
}
