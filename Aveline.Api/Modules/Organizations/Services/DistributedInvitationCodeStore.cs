using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace Aveline.Api.Modules.Organizations.Services;

/// <summary>
/// <see cref="IInvitationCodeStore"/> over <see cref="IDistributedCache"/> (StackExchange
/// Redis in production with the configured instance prefix; in-memory distributed cache in
/// dev/tests). Codes expire via the cache TTL.
/// </summary>
public sealed class DistributedInvitationCodeStore : IInvitationCodeStore
{
    private readonly IDistributedCache _cache;
    private readonly ILogger<DistributedInvitationCodeStore> _logger;

    public DistributedInvitationCodeStore(
        IDistributedCache cache,
        ILogger<DistributedInvitationCodeStore> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    private static string Key(string code) => $"invite:code:{code}";

    public async Task StoreAsync(
        string code,
        Guid invitationId,
        TimeSpan ttl,
        CancellationToken cancellationToken = default)
    {
        await _cache.SetStringAsync(
            Key(code),
            invitationId.ToString("D"),
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = ttl,
            },
            cancellationToken);
    }

    public async Task<Guid?> GetAsync(string code, CancellationToken cancellationToken = default)
    {
        try
        {
            var value = await _cache.GetStringAsync(Key(code), cancellationToken);
            return string.IsNullOrEmpty(value) || !Guid.TryParse(value, out var id)
                ? null
                : id;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Invitation code cache read failed for code length {Length}. Treating as a miss (hash fallback applies).", code.Length);
            return null;
        }
    }

    public async Task RemoveAsync(string code, CancellationToken cancellationToken = default)
    {
        await _cache.RemoveAsync(Key(code), cancellationToken);
    }
}
