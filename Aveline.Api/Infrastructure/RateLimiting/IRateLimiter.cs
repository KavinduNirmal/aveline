namespace Aveline.Api.Infrastructure.RateLimiting;

/// <summary>
/// Lightweight sliding-window attempt limiter keyed by a scope string (e.g. a client IP).
/// </summary>
public interface IRateLimiter
{
    /// <summary>
    /// Returns <c>true</c> when the caller may proceed, or <c>false</c> once the caller has
    /// exceeded <paramref name="limit"/> attempts within <paramref name="window"/>.
    /// </summary>
    Task<bool> TryAllowAsync(string scopeKey, int limit, TimeSpan window, CancellationToken cancellationToken = default);
}
