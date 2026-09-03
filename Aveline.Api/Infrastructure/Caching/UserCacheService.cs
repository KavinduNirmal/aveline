using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace Aveline.Api.Infrastructure.Caching;

public class UserCacheService : IUserCacheService
{
    private readonly IDistributedCache _cache;
    private readonly ILogger<UserCacheService> _logger;
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(24);

    public UserCacheService(IDistributedCache cache, ILogger<UserCacheService> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    private static string GetCacheKey(string clerkId) => $"user:onboarding:{clerkId}";

    public async Task<UserOnboardingCacheItem?> GetUserAsync(string clerkId, CancellationToken cancellationToken = default)
    {
        try
        {
            var key = GetCacheKey(clerkId);
            var cachedJson = await _cache.GetStringAsync(key, cancellationToken);
            if (string.IsNullOrEmpty(cachedJson))
            {
                return null;
            }

            return JsonSerializer.Deserialize<UserOnboardingCacheItem>(cachedJson);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read user cache for clerkId={ClerkId}. Proceeding as cache miss.", clerkId);
            return null;
        }
    }

    public async Task SetUserAsync(string clerkId, UserOnboardingCacheItem item, TimeSpan? ttl = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var key = GetCacheKey(clerkId);
            var json = JsonSerializer.Serialize(item);
            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = ttl ?? DefaultTtl
            };
            await _cache.SetStringAsync(key, json, options, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write user cache for clerkId={ClerkId}.", clerkId);
        }
    }

    public async Task InvalidateAsync(string clerkId, CancellationToken cancellationToken = default)
    {
        try
        {
            var key = GetCacheKey(clerkId);
            await _cache.RemoveAsync(key, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to invalidate user cache for clerkId={ClerkId}.", clerkId);
        }
    }
}
