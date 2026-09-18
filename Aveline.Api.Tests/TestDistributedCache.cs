using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Distributed;

namespace Aveline.Api.Tests;

/// <summary>
/// Minimal in-memory <see cref="IDistributedCache"/> for tests that need to exercise
/// cache reads/writes without a Redis instance.
/// </summary>
public sealed class TestDistributedCache : IDistributedCache
{
    private readonly ConcurrentDictionary<string, byte[]> _store = new();

    public byte[]? Get(string key) => _store.TryGetValue(key, out var value) ? value : null;

    public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
        => Task.FromResult(Get(key));

    public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
        => _store[key] = value;

    public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
    {
        Set(key, value, options);
        return Task.CompletedTask;
    }

    public void Refresh(string key) { }

    public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;

    public void Remove(string key) => _store.TryRemove(key, out _);

    public Task RemoveAsync(string key, CancellationToken token = default)
    {
        Remove(key);
        return Task.CompletedTask;
    }

    public int Count => _store.Count;
}
