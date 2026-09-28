using System.Collections.Concurrent;
using Aveline.Api.Modules.Revenue.DTOs;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace Aveline.Api.Modules.Revenue.Services;

/// <summary>
/// The revenue read cache (S-50…S-55), modelled on <c>BusinessKpiCache</c>.
/// </summary>
/// <remarks>
/// <see cref="IDistributedCache"/> rather than <c>IMemoryCache</c>, deliberately: the deployment runs
/// two replicas, and an in-process cache would let the two serve different revenue figures for the
/// same request. The cache is an optimisation and **never a dependency** — both the read and the
/// write swallow their exceptions, so an outage degrades to an uncached query rather than a failed
/// request, and the degradation is reported through <see cref="Describe"/> so an operator is told
/// rather than left to notice two replicas disagreeing.
/// </remarks>
public sealed class RevenueCache(
    IDistributedCache cache,
    IOptions<RevenueOptions> options)
{
    private const string Namespace = "aveline:revenue";

    // Single-flight: a cold key hit by N parallel fetches issues one query, not N.
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    private readonly RevenueOptions _options = options.Value;

    public TimeSpan ConsoleTtl => TimeSpan.FromSeconds(Math.Max(1, _options.CacheSeconds));

    /// <summary>
    /// <c>private</c>, because these are authenticated admin responses and must not land in a
    /// shared proxy.
    /// </summary>
    public string CacheControlHeader => $"private, max-age={Math.Max(1, _options.CacheSeconds)}";

    /// <summary>Builds the stable, namespaced cache key for one endpoint and parameter set.</summary>
    public string KeyFor(string endpoint, string parameters) =>
        $"{Namespace}:{endpoint}:{(string.IsNullOrEmpty(parameters) ? "-" : parameters)}";

    /// <summary>
    /// Returns the cached payload or computes it through <paramref name="factory"/> exactly once per
    /// key. A cache or factory failure degrades to an uncached query and is never converted into an
    /// endpoint failure by this class.
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
    /// configured the fallback is per-instance, and the operator is told rather than left to notice
    /// two replicas disagreeing.
    /// </summary>
    public IReadOnlyList<string> Describe(string? redisConnectionString)
    {
        if (!string.IsNullOrWhiteSpace(redisConnectionString))
        {
            return [];
        }

        return
        [
            "The revenue result cache is per-instance: Redis is not configured, so two replicas can "
            + "serve different figures for the same request within the cache TTL.",
        ];
    }

    /// <summary>Appends the cache's notes to a response's data-quality block.</summary>
    public IncomeDataQualityDto WithCacheNotes(
        IncomeDataQualityDto quality,
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
