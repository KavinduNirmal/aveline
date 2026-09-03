using Aveline.Api.Modules.Shared.DTOs;

namespace Aveline.Api.Infrastructure.Caching;

public interface IUserCacheService
{
    Task<UserOnboardingCacheItem?> GetUserAsync(string clerkId, CancellationToken cancellationToken = default);
    Task SetUserAsync(string clerkId, UserOnboardingCacheItem item, TimeSpan? ttl = null, CancellationToken cancellationToken = default);
    Task<UserDto?> GetUserProfileAsync(string clerkId, CancellationToken cancellationToken = default);
    Task SetUserProfileAsync(string clerkId, UserDto user, TimeSpan? ttl = null, CancellationToken cancellationToken = default);
    Task InvalidateAsync(string clerkId, CancellationToken cancellationToken = default);
}
