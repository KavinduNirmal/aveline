using System.Collections.Concurrent;

namespace Aveline.Api.Common.Jobs;

/// <summary>
/// Process-local job lock used when Redis is not configured (local development, tests).
/// The clock is injectable so lease expiry is deterministic in tests.
/// </summary>
public sealed class InMemoryDistributedJobLock : IDistributedJobLock
{
    private static readonly TimeSpan DefaultLease = TimeSpan.FromSeconds(300);

    private readonly ConcurrentDictionary<string, Entry> _locks = new(StringComparer.Ordinal);
    private readonly TimeSpan _defaultLease;
    private readonly TimeProvider _timeProvider;

    public InMemoryDistributedJobLock(TimeSpan? defaultLease = null, TimeProvider? timeProvider = null)
    {
        _defaultLease = defaultLease ?? DefaultLease;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<IAsyncDisposable?> TryAcquireAsync(
        string jobName,
        TimeSpan? leaseDuration = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobName);
        cancellationToken.ThrowIfCancellationRequested();

        var lease = leaseDuration ?? _defaultLease;
        var token = Guid.NewGuid().ToString("N");
        var expiresAt = _timeProvider.GetUtcNow() + lease;

        // Re-check on contention: an expired entry may be replaced, and a racing writer
        // may have changed the value between our read and write.
        while (true)
        {
            if (_locks.TryGetValue(jobName, out var existing))
            {
                if (existing.ExpiresAt > _timeProvider.GetUtcNow())
                {
                    return Task.FromResult<IAsyncDisposable?>(null);
                }

                if (_locks.TryUpdate(jobName, new Entry(token, expiresAt), existing))
                {
                    return Task.FromResult<IAsyncDisposable?>(new Handle(this, jobName, token));
                }

                continue;
            }

            if (_locks.TryAdd(jobName, new Entry(token, expiresAt)))
            {
                return Task.FromResult<IAsyncDisposable?>(new Handle(this, jobName, token));
            }
        }
    }

    private void Release(string jobName, string token)
    {
        // Compare-and-remove: a stale handle must never evict the current holder.
        if (_locks.TryGetValue(jobName, out var current) && current.Token == token)
        {
            _locks.TryRemove(new KeyValuePair<string, Entry>(jobName, current));
        }
    }

    private readonly record struct Entry(string Token, DateTimeOffset ExpiresAt);

    private sealed class Handle(InMemoryDistributedJobLock owner, string jobName, string token) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            owner.Release(jobName, token);
            return ValueTask.CompletedTask;
        }
    }
}
