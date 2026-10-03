using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace Aveline.Api.Infrastructure.RateLimiting;

/// <summary>
/// <see cref="IRateLimiter"/> over <see cref="IDistributedCache"/> (Redis in production,
/// in-memory in dev/tests). Uses a read-modify-write counter with a sliding window TTL. The
/// update is not atomic under high concurrency — acceptable for a brute-force guard, where a
/// rare under-count is preferable to blocking legitimate traffic.
/// </summary>
/// <remarks>
/// <para>
/// <b>This limiter fails open.</b> If the counter store cannot be read, the request is allowed so
/// that a cache outage does not take sign-ups, invitations and OTP verification down with it. The
/// consequence is that an outage silently disables every throttle at once. The underlying secrets
/// are high-entropy (6-digit CSPRNG OTPs with a hard 5-attempt cap; 12-character invitation codes
/// over a 32-character alphabet, ~60 bits), so fail-open does not by itself create a practical
/// brute-force path — but it is an availability-coupled weakness and a conscious trade (assessment
/// F-6.3, documented in <c>docs/ADR/ADR-030-rate-limiter-fail-open.md</c>).
/// </para>
/// <para>
/// Every failure is recorded on <c>aveline.rate_limiter.fail_open</c> so the condition is alertable
/// rather than invisible.
/// </para>
/// </remarks>
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
            // Alertable on purpose: an outage here means every throttle is off at once.
            RateLimiterMetrics.RecordFailOpen(scopeKey);
            _logger.LogWarning(ex, "Rate limiter unavailable for scope {Scope}. Failing open.", scopeKey);
            return true;
        }
    }
}
