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

    private static string GetOnboardingCacheKey(string clerkId) => $"user:onboarding:{clerkId}";
    private static string GetProfileCacheKey(string clerkId) => $"user:profile:{clerkId}";

    public async Task<UserOnboardingCacheItem?> GetUserAsync(string clerkId, CancellationToken cancellationToken = default)
    {
        try
        {
            var key = GetOnboardingCacheKey(clerkId);
            var cachedJson = await _cache.GetStringAsync(key, cancellationToken);
            if (string.IsNullOrEmpty(cachedJson))
            {
                return null;
            }

            return JsonSerializer.Deserialize<UserOnboardingCacheItem>(cachedJson);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read user onboarding cache for clerkId={ClerkId}. Proceeding as cache miss.", clerkId);
            return null;
        }
    }

    public async Task SetUserAsync(string clerkId, UserOnboardingCacheItem item, TimeSpan? ttl = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var key = GetOnboardingCacheKey(clerkId);
            var json = JsonSerializer.Serialize(item);
            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = ttl ?? DefaultTtl
            };
            await _cache.SetStringAsync(key, json, options, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write user onboarding cache for clerkId={ClerkId}.", clerkId);
        }
    }

    public async Task<Aveline.Api.Modules.Shared.DTOs.UserDto?> GetUserProfileAsync(string clerkId, CancellationToken cancellationToken = default)
    {
        try
        {
            var key = GetProfileCacheKey(clerkId);
            var cachedJson = await _cache.GetStringAsync(key, cancellationToken);
            if (string.IsNullOrEmpty(cachedJson))
            {
                return null;
            }

            return JsonSerializer.Deserialize<Aveline.Api.Modules.Shared.DTOs.UserDto>(cachedJson);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read user profile cache for clerkId={ClerkId}. Proceeding as cache miss.", clerkId);
            return null;
        }
    }

    public async Task SetUserProfileAsync(string clerkId, Aveline.Api.Modules.Shared.DTOs.UserDto user, TimeSpan? ttl = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var key = GetProfileCacheKey(clerkId);
            var json = JsonSerializer.Serialize(user);
            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = ttl ?? DefaultTtl
            };
            await _cache.SetStringAsync(key, json, options, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write user profile cache for clerkId={ClerkId}.", clerkId);
        }
    }

    public async Task InvalidateAsync(string clerkId, CancellationToken cancellationToken = default)
    {
        try
        {
            var onboardingKey = GetOnboardingCacheKey(clerkId);
            var profileKey = GetProfileCacheKey(clerkId);

            await _cache.RemoveAsync(onboardingKey, cancellationToken);
            await _cache.RemoveAsync(profileKey, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to invalidate user cache for clerkId={ClerkId}.", clerkId);
        }
    }
}
