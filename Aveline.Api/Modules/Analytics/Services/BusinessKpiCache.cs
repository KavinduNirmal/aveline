using System.Collections.Concurrent;
using Aveline.Api.Modules.Analytics.DTOs;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace Aveline.Api.Modules.Analytics.Services;

/// <summary>
/// The business-KPI result cache. <see cref="IDistributedCache"/> rather than
/// <c>IMemoryCache</c>, deliberately: the deployment runs two replicas, and an in-process cache
/// would let the two instances serve different figures for the same request.
/// </summary>
/// <remarks>
/// Two TTLs, because two classes of caller have different tolerances (DR-9): 60 s for the six
/// console endpoints a human is reading, and 3600 s for the platform-wide gauges the
/// 30-second <c>SystemMetricCollector</c> tick reads.
/// </remarks>
public sealed class BusinessKpiCache(
    IDistributedCache cache,
    IOptions<BusinessAnalyticsOptions> options)
{
    private const string Namespace = "aveline:business-kpi";

    // Single-flight: a cold key hit by N parallel dashboard fetches issues one query, not N.
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    private readonly BusinessAnalyticsOptions _options = options.Value;

    public TimeSpan ConsoleTtl => TimeSpan.FromSeconds(Math.Max(1, _options.CacheSeconds));

    public TimeSpan GaugeTtl => TimeSpan.FromSeconds(Math.Max(1, _options.MetricCacheSeconds));

    /// <summary>
    /// <c>private</c>, because these are authenticated admin responses and must not land in a
    /// shared proxy.
    /// </summary>
    public string CacheControlHeader => $"private, max-age={Math.Max(1, _options.CacheSeconds)}";

    /// <summary>Builds the stable, namespaced cache key for one endpoint and parameter set.</summary>
    public string KeyFor(string endpoint, string parameters) =>
        $"{Namespace}:{endpoint}:{(string.IsNullOrEmpty(parameters) ? "-" : parameters)}";

    /// <summary>
    /// Returns the cached payload or computes it through <paramref name="factory"/> exactly once
    /// per key. A cache or factory failure degrades to an uncached query and is never converted
    /// into an endpoint failure by this class.
    /// </summary>
    public async Task<string> GetOrCreateAsync(
        string key,
        TimeSpan ttl,
        Func<CancellationToken, Task<string>> factory,
        CancellationToken cancellationToken = default)
    {
        var cached = await TryGetAsync(key, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var gate = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            // The winner may have just populated the key while this caller waited.
            cached = await TryGetAsync(key, cancellationToken);
            if (cached is not null)
            {
                return cached;
            }

            var computed = await factory(cancellationToken);
            await TrySetAsync(key, computed, ttl, cancellationToken);
            return computed;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// The data-quality notes that describe the cache backing this instance. When Redis is not
    /// configured the fallback is per-instance, and the operator is told rather than left to
    /// notice two replicas disagreeing.
    /// </summary>
    public IReadOnlyList<string> Describe(string? redisConnectionString)
    {
        if (!string.IsNullOrWhiteSpace(redisConnectionString))
        {
            return [];
        }

        return
        [
            "The business-KPI result cache is per-instance: Redis is not configured, so two " +
            "replicas can serve different figures for the same request within the cache TTL.",
        ];
    }

    /// <summary>
    /// Fails startup loudly when a shared cache is required but the deployment would silently
    /// fall back to the in-memory implementation. Returns <c>null</c> when the configuration is
    /// acceptable.
    /// </summary>
    public static string? ValidateSharedCache(
        BusinessAnalyticsOptions options,
        string? redisConnectionString)
    {
        if (!options.RequireSharedCache || !string.IsNullOrWhiteSpace(redisConnectionString))
        {
            return null;
        }

        return "BusinessAnalytics:RequireSharedCache is true but no Redis connection string is " +
               "configured. IDistributedCache would fall back to a per-instance in-memory cache, " +
               "and the deployment's replicas would serve different business-KPI figures for the " +
               "same request. Configure Redis or set RequireSharedCache to false.";
    }

    /// <summary>Maps a data-quality DTO to its notes plus the cache notes for this instance.</summary>
    public BusinessDataQualityDto WithCacheNotes(
        BusinessDataQualityDto quality,
        string? redisConnectionString)
    {
        var notes = Describe(redisConnectionString);
        return notes.Count == 0 ? quality : quality with { Notes = [.. quality.Notes, .. notes] };
    }

    private async Task<string?> TryGetAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            return await cache.GetStringAsync(key, cancellationToken);
        }
        catch (Exception)
        {
            // A cache read is an optimisation, never a dependency.
            return null;
        }
    }

    private async Task TrySetAsync(
        string key,
        string value,
        TimeSpan ttl,
        CancellationToken cancellationToken)
    {
        try
        {
            await cache.SetStringAsync(
                key,
                value,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl },
                cancellationToken);
        }
        catch (Exception)
        {
            // As above: never fail the request because the cache is down.
        }
    }
}
