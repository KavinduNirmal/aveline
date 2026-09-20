using Aveline.Api.Configurations;
using Aveline.Api.Modules.Analytics;
using Aveline.Api.Modules.Analytics.Services;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Aveline.Api.Tests;

/// <summary>
/// Phase 1 of the Business KPIs plan (§5.4.7, DR-9): the console result cache. A cache hit
/// issues no query; a different parameter set is a different key; N concurrent cold-key calls
/// collapse into one query through the single-flight guard; a cache failure degrades to an
/// uncached query rather than a 500; and <c>RequireSharedCache</c> names the Redis fallback.
/// </summary>
public class BusinessKpiCacheTests
{
    private sealed class CountingFactory
    {
        public int Calls;

        public async Task<string> InvokeAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            await Task.Yield();
            return "payload";
        }
    }

    private static BusinessKpiCache Build(
        IDistributedCache? cache = null,
        Action<BusinessAnalyticsOptions>? configure = null)
    {
        var options = new BusinessAnalyticsOptions { CacheSeconds = 60, MetricCacheSeconds = 3600 };
        configure?.Invoke(options);
        return new BusinessKpiCache(
            cache ?? new MemoryDistributedCache(
                Microsoft.Extensions.Options.Options.Create(new MemoryDistributedCacheOptions())),
            Microsoft.Extensions.Options.Options.Create(options));
    }

    private sealed class ThrowingCache : IDistributedCache
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

    [Fact]
    public async Task TwoIdenticalCallsIssueOneQuery()
    {
        var cache = Build();
        var factory = new CountingFactory();
        var key = cache.KeyFor("growth", "from=A&to=B&granularity=day");

        var first = await cache.GetOrCreateAsync(key, cache.ConsoleTtl, factory.InvokeAsync);
        var second = await cache.GetOrCreateAsync(key, cache.ConsoleTtl, factory.InvokeAsync);

        Assert.Equal("payload", first);
        Assert.Equal("payload", second);
        Assert.Equal(1, factory.Calls);
    }

    [Fact]
    public async Task ADifferentParameterSetIssuesASecondQuery()
    {
        var cache = Build();
        var factory = new CountingFactory();

        await cache.GetOrCreateAsync(
            cache.KeyFor("growth", "from=A"), cache.ConsoleTtl, factory.InvokeAsync);
        await cache.GetOrCreateAsync(
            cache.KeyFor("growth", "from=B"), cache.ConsoleTtl, factory.InvokeAsync);

        Assert.Equal(2, factory.Calls);
    }

    [Fact]
    public async Task DifferentEndpointsWithTheSameParametersAreDifferentKeys()
    {
        var cache = Build();
        var factory = new CountingFactory();

        await cache.GetOrCreateAsync(
            cache.KeyFor("growth", "from=A"), cache.ConsoleTtl, factory.InvokeAsync);
        await cache.GetOrCreateAsync(
            cache.KeyFor("usage", "from=A"), cache.ConsoleTtl, factory.InvokeAsync);

        Assert.Equal(2, factory.Calls);
    }

    [Fact]
    public async Task ConcurrentColdKeyCallsCollapseIntoOneQuery()
    {
        var cache = Build();
        var calls = 0;
        var gate = new TaskCompletionSource();
        var key = cache.KeyFor("active-users", "from=A");

        async Task<string> SlowFactory(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref calls);
            await gate.Task;
            return "payload";
        }

        var tasks = Enumerable.Range(0, 12)
            .Select(_ => cache.GetOrCreateAsync(key, cache.ConsoleTtl, SlowFactory))
            .ToArray();

        gate.SetResult();
        var results = await Task.WhenAll(tasks);

        Assert.All(results, value => Assert.Equal("payload", value));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ACacheFailureDegradesToAnUncachedQuery()
    {
        var cache = Build(new ThrowingCache());
        var factory = new CountingFactory();
        var key = cache.KeyFor("growth", "from=A");

        var first = await cache.GetOrCreateAsync(key, cache.ConsoleTtl, factory.InvokeAsync);
        var second = await cache.GetOrCreateAsync(key, cache.ConsoleTtl, factory.InvokeAsync);

        Assert.Equal("payload", first);
        Assert.Equal("payload", second);
        // The cache never caches, so both calls query; the point is that neither throws.
        Assert.Equal(2, factory.Calls);
    }

    [Fact]
    public async Task AFactoryFailureDoesNotPoisonTheCache()
    {
        var cache = Build();
        var key = cache.KeyFor("growth", "from=A");
        var attempts = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(() => cache.GetOrCreateAsync(
            key, cache.ConsoleTtl, _ =>
            {
                attempts++;
                throw new InvalidOperationException("query failed");
            }));

        var recovered = await cache.GetOrCreateAsync(
            key, cache.ConsoleTtl, _ => Task.FromResult("recovered"));

        Assert.Equal(1, attempts);
        Assert.Equal("recovered", recovered);
    }

    [Fact]
    public void TheTwoTtlsComeFromOptions()
    {
        var cache = Build(configure: options =>
        {
            options.CacheSeconds = 45;
            options.MetricCacheSeconds = 900;
        });

        Assert.Equal(TimeSpan.FromSeconds(45), cache.ConsoleTtl);
        Assert.Equal(TimeSpan.FromSeconds(900), cache.GaugeTtl);
        Assert.Equal("private, max-age=45", cache.CacheControlHeader);
    }

    [Fact]
    public void TheKeyIsNamespacedAndStable()
    {
        var cache = Build();
        var first = cache.KeyFor("growth", "from=A");
        var second = cache.KeyFor("growth", "from=A");

        Assert.Equal(first, second);
        Assert.StartsWith("aveline:business-kpi:growth:", first, StringComparison.Ordinal);
        Assert.DoesNotContain(" ", first, StringComparison.Ordinal);
    }

    // ── RequireSharedCache ────────────────────────────────────────────────────────────────

    [Fact]
    public void RequireSharedCacheIsSatisfiedWhenRedisIsConfigured()
    {
        var error = BusinessKpiCache.ValidateSharedCache(
            new BusinessAnalyticsOptions { RequireSharedCache = true },
            "localhost:6379");

        Assert.Null(error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RequireSharedCacheFailsWithoutRedis(string? connectionString)
    {
        var error = BusinessKpiCache.ValidateSharedCache(
            new BusinessAnalyticsOptions { RequireSharedCache = true },
            connectionString);

        Assert.NotNull(error);
        Assert.Contains("Redis", error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RequireSharedCache", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TheGuardIsInertWhenRequireSharedCacheIsOff()
    {
        Assert.Null(BusinessKpiCache.ValidateSharedCache(
            new BusinessAnalyticsOptions { RequireSharedCache = false }, redisConnectionString: null));
    }

    [Fact]
    public void WithoutRedisTheConsoleIsToldTheCacheIsPerInstance()
    {
        var cache = Build(new MemoryDistributedCache(
            Microsoft.Extensions.Options.Options.Create(new MemoryDistributedCacheOptions())));

        var notes = cache.Describe(redisConnectionString: null);

        Assert.Contains(notes, note => note.Contains("per-instance", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void WithRedisNoPerInstanceNoteIsEmitted()
    {
        var cache = Build();

        var notes = cache.Describe(redisConnectionString: "localhost:6379");

        Assert.DoesNotContain(notes, note =>
            note.Contains("per-instance", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ResolveRedisConnectionStringReadsTheConfiguredKey()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Redis:ConnectionString"] = "localhost:6379",
            })
            .Build();

        Assert.Equal("localhost:6379", CacheConfiguration.ResolveRedisConnectionString(configuration));
    }
}
