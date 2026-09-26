using Aveline.Api.Modules.Revenue;
using Aveline.Api.Modules.Revenue.Services;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace Aveline.Api.Tests;

/// <summary>
/// Revenue Ledger R3 (issue #344) — the console cache's degradation contract.
///
/// A cache is an optimisation and never a dependency. Two things must hold, and both are asserted
/// against a cache that throws rather than a mock that records: a failing cache still returns a
/// correct result, and the degradation is **visible** in `dataQuality.notes` rather than silent.
/// The second half is the one that usually goes missing.
/// </summary>
public class RevenueCacheTests
{
    /// <summary>A distributed cache that is entirely down.</summary>
    private sealed class BrokenCache : IDistributedCache
    {
        public byte[]? Get(string key) => throw new InvalidOperationException("cache is down");

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) =>
            throw new InvalidOperationException("cache is down");

        public void Refresh(string key) => throw new InvalidOperationException("cache is down");

        public Task RefreshAsync(string key, CancellationToken token = default) =>
            throw new InvalidOperationException("cache is down");

        public void Remove(string key) => throw new InvalidOperationException("cache is down");

        public Task RemoveAsync(string key, CancellationToken token = default) =>
            throw new InvalidOperationException("cache is down");

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) =>
            throw new InvalidOperationException("cache is down");

        public Task SetAsync(
            string key, byte[] value, DistributedCacheEntryOptions options,
            CancellationToken token = default) =>
            throw new InvalidOperationException("cache is down");
    }

    /// <summary>A working in-process cache, plus a count of how often the factory actually ran.</summary>
    private sealed class CountingCache : IDistributedCache
    {
        private readonly Dictionary<string, byte[]> _store = [];

        public int Writes { get; private set; }

        public byte[]? Get(string key) => _store.TryGetValue(key, out var value) ? value : null;

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) =>
            Task.FromResult(Get(key));

        public void Refresh(string key) { }

        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;

        public void Remove(string key) => _store.Remove(key);

        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            Remove(key);
            return Task.CompletedTask;
        }

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) =>
            _store[key] = value;

        public Task SetAsync(
            string key, byte[] value, DistributedCacheEntryOptions options,
            CancellationToken token = default)
        {
            _store[key] = value;
            Writes++;
            return Task.CompletedTask;
        }
    }

    private static RevenueCache CreateCache(IDistributedCache cache, int cacheSeconds = 60) =>
        new(cache, Options.Create(new RevenueOptions { CacheSeconds = cacheSeconds }));

    /// <summary>A cache that is down must not fail the request.</summary>
    [Fact]
    public async Task AThrowingCache_StillReturnsTheComputedValue()
    {
        var cache = CreateCache(new BrokenCache());
        var factoryRan = 0;

        var payload = await cache.GetOrCreateAsync(
            cache.KeyFor("overview", "-"),
            cache.ConsoleTtl,
            _ =>
            {
                factoryRan++;
                return Task.FromResult("{\"mrr\":14500}");
            },
            default);

        Assert.Equal("{\"mrr\":14500}", payload);
        Assert.Equal(1, factoryRan);
    }

    /// <summary>
    /// The degradation must be stated. The business-KPI cache already does this, and the reason is
    /// that a per-instance cache lets two replicas serve different figures — which an operator
    /// cannot diagnose from the response alone.
    /// </summary>
    [Fact]
    public void WithNoRedis_TheNotesSayTheCacheIsPerInstance()
    {
        var cache = CreateCache(new CountingCache());

        var notes = cache.Describe(redisConnectionString: null);

        Assert.Contains(
            notes,
            note => note.Contains("per-instance", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void WithRedisConfigured_ThereIsNothingToSay()
    {
        var cache = CreateCache(new CountingCache());

        Assert.Empty(cache.Describe("localhost:6379"));
    }

    [Fact]
    public void WithCacheNotes_AppendsRatherThanReplaces()
    {
        var cache = CreateCache(new CountingCache());
        var quality = new Modules.Revenue.DTOs.IncomeDataQualityDto(
            RevenueProviderSettlementAvailable: false,
            SubscriptionPricesConfigured: true,
            DerivedEntriesUnverified: 0,
            CheckedAt: DateTime.UtcNow,
            Notes: ["an existing note"]);

        var merged = cache.WithCacheNotes(quality, redisConnectionString: null);

        Assert.Equal(2, merged.Notes.Count);
        Assert.Contains("an existing note", merged.Notes);
        Assert.Contains(merged.Notes, note => note.Contains("per-instance", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Two callers hitting a cold key must produce **one** query, not two. Without single-flight a
    /// dashboard that fetches six endpoints at once multiplies its own database load.
    /// </summary>
    [Fact]
    public async Task TwoConcurrentMisses_RunTheFactoryOnce()
    {
        var cache = CreateCache(new CountingCache());
        var factoryRuns = 0;
        var gate = new TaskCompletionSource();

        async Task<string> Factory(CancellationToken _)
        {
            Interlocked.Increment(ref factoryRuns);
            // Hold the first caller inside the factory so the second genuinely overlaps it.
            await gate.Task;
            return "computed";
        }

        var first = cache.GetOrCreateAsync(cache.KeyFor("overview", "-"), cache.ConsoleTtl, Factory, default);
        var second = cache.GetOrCreateAsync(cache.KeyFor("overview", "-"), cache.ConsoleTtl, Factory, default);

        gate.SetResult();
        var results = await Task.WhenAll(first, second);

        Assert.All(results, result => Assert.Equal("computed", result));
        Assert.Equal(1, Volatile.Read(ref factoryRuns));
    }

    [Fact]
    public async Task AWarmKey_DoesNotRunTheFactoryAtAll()
    {
        var cache = CreateCache(new CountingCache());
        var factoryRuns = 0;

        Task<string> Factory(CancellationToken _)
        {
            factoryRuns++;
            return Task.FromResult("computed");
        }

        var key = cache.KeyFor("overview", "-");
        await cache.GetOrCreateAsync(key, cache.ConsoleTtl, Factory, default);
        await cache.GetOrCreateAsync(key, cache.ConsoleTtl, Factory, default);

        Assert.Equal(1, factoryRuns);
    }

    /// <summary>
    /// The header is `private` because these are authenticated admin responses: a shared proxy must
    /// never serve one operator's revenue figures to another.
    /// </summary>
    [Fact]
    public void TheCacheControlHeader_IsPrivate()
    {
        var cache = CreateCache(new CountingCache(), cacheSeconds: 45);

        Assert.Equal("private, max-age=45", cache.CacheControlHeader);
    }

    [Fact]
    public void AKey_IsNamespacedAndStable()
    {
        var cache = CreateCache(new CountingCache());

        var first = cache.KeyFor("timeseries", "2026-09-01|day");
        var second = cache.KeyFor("timeseries", "2026-09-01|day");

        Assert.Equal(first, second);
        Assert.StartsWith("aveline:revenue:", first);
        // An empty parameter set is a stable `-`, so a no-parameter endpoint has one key.
        Assert.EndsWith(":-", cache.KeyFor("overview", string.Empty));
    }
}
