namespace Aveline.Api.Modules.Media;

/// <summary>What one attempt to claim a single-use token nonce returned.</summary>
public enum MediaNonceClaim
{
    /// <summary>This caller is the first to present the nonce; the token may be used.</summary>
    Claimed,

    /// <summary>The nonce was already claimed, so the token has been replayed.</summary>
    AlreadyClaimed,

    /// <summary>
    /// The claim could not be made because the store is unavailable. This fails <b>closed</b>:
    /// without a claim there is no fetch. It is deliberately the opposite direction from the
    /// agent's rate limiter, which fails open (strategy §3.8; migration plan §7.7).
    /// </summary>
    Unavailable,
}

/// <summary>
/// The single-use nonce store behind <see cref="MediaScope.VisionAnalyze"/> tokens: one atomic
/// claim per nonce, expiring with the token.
/// </summary>
/// <remarks>
/// The production implementation is Redis (<c>SET … NX PX</c>, the precedent set by
/// <c>RedisDistributedJobLock</c>). The eviction policy matters: with <c>volatile-lru</c> the
/// TTL-bearing nonce is an eviction candidate, and an evicted nonce silently weakens the
/// single-use guarantee, so <c>noeviction</c> is a prerequisite on this unit's gate
/// (strategy §3.8).
/// </remarks>
public interface IMediaTokenNonceStore
{
    /// <summary>Attempts the one claim for <paramref name="nonce"/>.</summary>
    Task<MediaNonceClaim> TryClaimAsync(string nonce, TimeSpan ttl, CancellationToken ct = default);
}
