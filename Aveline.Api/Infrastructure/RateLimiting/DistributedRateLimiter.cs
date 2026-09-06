using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace Aveline.Api.Infrastructure.RateLimiting;

/// <summary>
/// <see cref="IRateLimiter"/> over <see cref="IDistributedCache"/> (Redis in production,
/// in-memory in dev/tests). Uses a read-modify-write counter with a sliding window TTL. The
/// update is not atomic under high concurrency — acceptable for a brute-force guard, where a
/// rare under-count is preferable to blocking legitimate traffic.
/// </summary>
public sealed class DistributedRateLimiter : IRateLimiter
{
    private readonly IDistributedCache _cache;
    private readonly ILogger<DistributedRateLimiter> _logger;

    public DistributedRateLimiter(
        IDistributedCache cache,
        ILogger<DistributedRateLimiter> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    private static string Key(string scope) => $"rate:{scope}";

    public async Task<bool> TryAllowAsync(
        string scopeKey,
        int limit,
        TimeSpan window,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
        {
            return false;
        }

        var key = Key(scopeKey);

        try
        {
            var raw = await _cache.GetStringAsync(key, cancellationToken);
            var count = int.TryParse(raw, out var parsed) ? parsed : 0;

            if (count >= limit)
            {
                return false;
            }

            await _cache.SetStringAsync(
                key,
                (count + 1).ToString(),
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = window,
                },
                cancellationToken);

            return true;
        }
        catch (Exception ex)
        {
            // If the cache is unavailable, fail open so legitimate sign-ups are not blocked.
            _logger.LogWarning(ex, "Rate limiter unavailable for scope {Scope}. Failing open.", scopeKey);
            return true;
        }
    }
}
