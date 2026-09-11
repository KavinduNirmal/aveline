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

    /// <summary>Applies a partial profile update and refreshes the caches (FR-3.2).</summary>
    Task<UserDto?> UpdateProfileAsync(
        string clerkId,
        UpdateUserProfileRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Soft-deletes the account: sets <c>DeletedAt</c>/<c>IsActive=false</c>/<c>Suspended</c>,
    /// revokes every membership and API key, and invalidates the cached snapshot (FR-3.5).
    /// </summary>
    Task<UserDto?> DeleteAccountAsync(
        string clerkId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Administrator account-state change, enforcing the FR-3.9 transition matrix.
    /// </summary>
    Task<UserDto> ChangeAccountStateAsync(
        Guid userId,
        AccountState state,
        Guid actorUserId,
        CancellationToken cancellationToken = default);

    /// <summary>Cross-organization paginated user search for Aveline support staff (FR-3.7).</summary>
    Task<PagedUsers> SearchUsersAsync(
        string? search,
        AccountState? state,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}
