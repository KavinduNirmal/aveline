using System.Security.Claims;
using Aveline.Api.Infrastructure.Caching;
using Aveline.Api.Modules.Shared.DTOs;

using Aveline.Api.Modules.Shared.Models;

namespace Aveline.Api.Modules.Shared.Services;

public interface IUserService
{
    Task<UserOnboardingCacheItem> GetOrSynchronizeUserAsync(string clerkId, ClaimsPrincipal principal, CancellationToken cancellationToken = default);
    Task<UserDto?> GetByClerkIdAsync(string clerkId, CancellationToken cancellationToken = default);
    Task<UserDto> CompleteOnboardingAsync(string clerkId, CompleteOnboardingRequest request, CancellationToken cancellationToken = default);

    /// <summary>Sets the account lifecycle state and refreshes the caches.</summary>
    Task<UserDto?> SetAccountStateAsync(string clerkId, AccountState state, CancellationToken cancellationToken = default);
}
