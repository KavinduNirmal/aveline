using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Aveline.Api.Modules.Media;

/// <summary>
/// The Redis single-use nonce store: one <c>SET key value NX PX ttl</c> per claim, following the
/// repository's precedent for a TTL-bearing Redis key (<c>RedisDistributedJobLock.cs:40-44</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>A store failure fails closed.</b> The claim cannot be proven, so the request is refused and
/// logged at <see cref="LogLevel.Error"/>. This is deliberately the opposite direction from the
/// agent's rate limiter, which fails <em>open</em> (<c>app/middleware/rate_limit.py:113-115</c>):
/// for an access grant, an unverifiable single-use claim must not become a served asset
/// (strategy §3.8; migration plan §7.7).
/// </para>
/// <para>
/// The nonce is never logged. The key carries the <c>aveline:</c> prefix and a random nonce, and
/// its TTL is the token's remaining lifetime, so an evicted key cannot outlive the token — but
/// under <c>volatile-lru</c> it could be evicted early, which is why <c>noeviction</c> is a
/// prerequisite (strategy §3.8).
/// </para>
/// </remarks>
public sealed class RedisMediaTokenNonceStore : IMediaTokenNonceStore
{
    private const string KeyPrefix = "aveline:media-nonce:";

    private readonly IConnectionMultiplexer _multiplexer;
    private readonly ILogger<RedisMediaTokenNonceStore> _logger;

    public RedisMediaTokenNonceStore(
        IConnectionMultiplexer multiplexer,
        ILogger<RedisMediaTokenNonceStore> logger)
    {
        _multiplexer = multiplexer ?? throw new ArgumentNullException(nameof(multiplexer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<MediaNonceClaim> TryClaimAsync(
        string nonce, TimeSpan ttl, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nonce);

        try
        {
            var claimed = await _multiplexer.GetDatabase()
                .StringSetAsync(KeyPrefix + nonce, "1", ttl, When.NotExists)
                .ConfigureAwait(false);

            return claimed ? MediaNonceClaim.Claimed : MediaNonceClaim.AlreadyClaimed;
        }
        catch (Exception exception)
        {
            // No nonce value, no token value, in the log line: only the failure's shape.
            _logger.LogError(
                exception,
                "The media token nonce store is unavailable; refusing the single-use claim (fail closed).");
            return MediaNonceClaim.Unavailable;
        }
    }
}

/// <summary>
/// The safe default when Redis is not configured: every single-use claim is refused. A
/// <c>vision.analyze</c> token can therefore never be served on a host without a nonce store,
/// which is the fail-closed direction the strategy requires (strategy §3.8).
/// </summary>
public sealed class UnavailableMediaTokenNonceStore : IMediaTokenNonceStore
{
    private readonly ILogger<UnavailableMediaTokenNonceStore> _logger;

    public UnavailableMediaTokenNonceStore(ILogger<UnavailableMediaTokenNonceStore> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task<MediaNonceClaim> TryClaimAsync(string nonce, TimeSpan ttl, CancellationToken ct = default)
    {
        _logger.LogError(
            "A single-use media token was presented but no Redis connection is configured, so the "
            + "nonce cannot be claimed. Refusing the request (fail closed).");
        return Task.FromResult(MediaNonceClaim.Unavailable);
    }
}
