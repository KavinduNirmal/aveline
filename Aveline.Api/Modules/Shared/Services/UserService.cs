using System.Security.Claims;
using Aveline.Api.Infrastructure.Caching;
using Aveline.Api.Modules.Organizations.Services;
using Aveline.Api.Modules.Shared.DTOs;
using Aveline.Api.Modules.Shared.Models;
using Aveline.Api.Modules.Shared.Repositories;
using Microsoft.Extensions.Logging;
namespace Aveline.Api.Modules.Shared.Services;

public class UserService : IUserService
{
    private readonly IUserRepository _userRepository;
    private readonly IUserCacheService _cacheService;
    private readonly IOrganizationService _organizationService;
    private readonly ILogger<UserService> _logger;

    public UserService(
        IUserRepository userRepository,
        IUserCacheService cacheService,
        IOrganizationService organizationService,
        ILogger<UserService> logger)
    {
        _userRepository = userRepository;
        _cacheService = cacheService;
        _organizationService = organizationService;
        _logger = logger;
    }

    public async Task<UserOnboardingCacheItem> GetOrSynchronizeUserAsync(
        string clerkId,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        // 1. Check Redis Cache
        var cached = await _cacheService.GetUserAsync(clerkId, cancellationToken);
        if (cached != null)
        {
            // A refreshed Clerk session may carry a new role/org context; adopt it so the
            // local read model never lags behind the claims for long.
            if (ClaimsContextDiffers(cached, principal))
            {
                var user = await _userRepository.GetByClerkIdAsync(clerkId, cancellationToken);
                if (user != null)
                {
                    var changed = ApplyClaimContext(user, principal);
                    if (changed)
                    {
                        var resolved = await ResolveAccountStateAsync(user, cancellationToken);
                        if (resolved != user.AccountState)
                        {
                            user.AccountState = resolved;
                        }

                        return await PersistAndRefreshCachesAsync(user, cancellationToken);
                    }
                }
            }

            return cached;
        }

        // 2. Cache Miss - Query Database
        var dbUser = await _userRepository.GetByClerkIdAsync(clerkId, cancellationToken);
        if (dbUser == null)
        {
            // 3. User does not exist in DB yet - Create stub record from Clerk claims
            var email = principal.FindFirstValue("email")
                ?? principal.FindFirstValue(ClaimTypes.Email)
                ?? string.Empty;

            var firstName = principal.FindFirstValue("first_name")
                ?? principal.FindFirstValue(ClaimTypes.GivenName)
                ?? string.Empty;

            var lastName = principal.FindFirstValue("last_name")
                ?? principal.FindFirstValue(ClaimTypes.Surname)
                ?? string.Empty;

            var username = principal.FindFirstValue("username")
                ?? (email.Contains('@') ? email.Split('@')[0] : clerkId);

            var userRole = principal.FindFirstValue("user_role")
                ?? (principal.FindAll(ClaimTypes.Role).Select(r => r.Value).FirstOrDefault() ?? "user");

            var orgRole = principal.FindFirstValue("org_role") ?? string.Empty;
            var orgId = principal.FindFirstValue("org_id") ?? string.Empty;

            var user = new User
            {
                Id = Guid.CreateVersion7(),
                ClerkId = clerkId,
                Email = email,
                FirstName = firstName,
                LastName = lastName,
                Username = username,
                UserRole = userRole,
                OrganizationRole = orgRole,
                OrganizationId = orgId,
                HasCompletedOnboarding = false,
                AccountState = AccountState.OnboardingPending,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await _userRepository.CreateAsync(user, cancellationToken);
            _logger.LogInformation("Created new Aveline DB stub user for ClerkId={ClerkId}, Email={Email}", clerkId, email);

            dbUser = user;
        }
        else
        {
            // 4. Existing DB record - adopt refreshed session claims and reconcile the
            //    account state against the canonical organization memberships.
            var changed = ApplyClaimContext(dbUser, principal);
            if (changed || dbUser.AccountState == AccountState.OnboardingPending)
            {
                var resolved = await ResolveAccountStateAsync(dbUser, cancellationToken);
                if (resolved != dbUser.AccountState)
                {
                    dbUser.AccountState = resolved;
                    dbUser.UpdatedAt = DateTime.UtcNow;
                    changed = true;
                }
            }

            if (changed)
            {
                await _userRepository.UpdateAsync(dbUser, cancellationToken);
            }
        }

        var cacheItem = MapToCacheItem(dbUser);
        var userDto = UserDto.FromEntity(dbUser);
        await _cacheService.SetUserAsync(clerkId, cacheItem, cancellationToken: cancellationToken);
        await _cacheService.SetUserProfileAsync(clerkId, userDto, cancellationToken: cancellationToken);
        return cacheItem;
    }

    public async Task<UserDto?> GetByClerkIdAsync(string clerkId, CancellationToken cancellationToken = default)
    {
        // 1. Check Redis profile cache
        var cachedProfile = await _cacheService.GetUserProfileAsync(clerkId, cancellationToken);
        if (cachedProfile != null)
        {
            return cachedProfile;
        }

        // 2. Cache miss - Query database
        var user = await _userRepository.GetByClerkIdAsync(clerkId, cancellationToken);
        if (user == null)
        {
            return null;
        }

        var dto = UserDto.FromEntity(user);

        // 3. Populate Redis profile cache
        await _cacheService.SetUserProfileAsync(clerkId, dto, cancellationToken: cancellationToken);
        return dto;
    }

    public async Task<UserDto> CompleteOnboardingAsync(
        string clerkId,
        CompleteOnboardingRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByClerkIdAsync(clerkId, cancellationToken);
        if (user == null)
        {
            throw new KeyNotFoundException($"User with ClerkId '{clerkId}' not found.");
        }

        user.DisplayName = request.DisplayName;
        user.PhoneNumber = request.PhoneNumber;
        user.Address = request.Address;
        if (!string.IsNullOrWhiteSpace(request.ProfileImageUrl))
        {
            user.ProfileImageUrl = request.ProfileImageUrl;
        }
        user.ContactPreference = request.ContactPreference;
        user.PushNotificationsEnabled = request.PushNotificationsEnabled;
        user.HasCompletedOnboarding = true;
        user.AccountState = await ResolveAccountStateAsync(user, cancellationToken);
        user.UpdatedAt = DateTime.UtcNow;

        await _userRepository.UpdateAsync(user, cancellationToken);
        _logger.LogInformation("User {ClerkId} completed onboarding successfully.", clerkId);

        // Update Redis Cache immediately (both onboarding item and full profile DTO)
        var cacheItem = MapToCacheItem(user);
        var updatedDto = UserDto.FromEntity(user);
        await _cacheService.SetUserAsync(clerkId, cacheItem, cancellationToken: cancellationToken);
        await _cacheService.SetUserProfileAsync(clerkId, updatedDto, cancellationToken: cancellationToken);

        return updatedDto;
    }

    public async Task<UserDto?> SetAccountStateAsync(
        string clerkId,
        AccountState state,
        CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByClerkIdAsync(clerkId, cancellationToken);
        if (user == null)
        {
            return null;
        }

        user.AccountState = state;
        user.IsActive = state != AccountState.Suspended;
        user.UpdatedAt = DateTime.UtcNow;

        await _userRepository.UpdateAsync(user, cancellationToken);
        _logger.LogInformation("Account state updated. clerkId={ClerkId} state={State}", clerkId, state);

        var cacheItem = MapToCacheItem(user);
        var dto = UserDto.FromEntity(user);
        await _cacheService.SetUserAsync(clerkId, cacheItem, cancellationToken: cancellationToken);
        await _cacheService.SetUserProfileAsync(clerkId, dto, cancellationToken: cancellationToken);
        return dto;
    }

    private async Task<AccountState> ResolveAccountStateAsync(
        User user,
        CancellationToken cancellationToken)
    {
        if (!user.IsActive)
        {
            return AccountState.Suspended;
        }

        if (HasLegacyOrgContext(user))
        {
            return AccountState.Active;
        }

        // Canonical source of truth: a valid organization membership makes the account active
        // even before Clerk session claims carry the org context.
        return await _organizationService.HasActiveMembershipAsync(user.Id, cancellationToken)
            ? AccountState.Active
            : AccountState.OnboardingPending;
    }

    private static bool HasLegacyOrgContext(User user)
    {
        return !string.IsNullOrWhiteSpace(user.OrganizationRole)
               || !string.IsNullOrWhiteSpace(user.OrganizationId);
    }

    /// <summary>
    /// Adopts the role/org context carried by a (possibly refreshed) Clerk session token into
    /// the local read model. Only claims that are actually present are adopted; a token without
    /// org claims never clears an existing context.
    /// </summary>
    private static bool ApplyClaimContext(User user, ClaimsPrincipal principal)
    {
        var changed = false;

        var userRole = principal.FindFirstValue("user_role");
        if (!string.IsNullOrWhiteSpace(userRole) && !string.Equals(user.UserRole, userRole, StringComparison.Ordinal))
        {
            user.UserRole = userRole;
            changed = true;
        }

        var orgRole = principal.FindFirstValue("org_role");
        if (!string.IsNullOrWhiteSpace(orgRole) && !string.Equals(user.OrganizationRole, orgRole, StringComparison.Ordinal))
        {
            user.OrganizationRole = orgRole;
            changed = true;
        }

        var orgId = principal.FindFirstValue("org_id");
        if (!string.IsNullOrWhiteSpace(orgId) && !string.Equals(user.OrganizationId, orgId, StringComparison.Ordinal))
        {
            user.OrganizationId = orgId;
            changed = true;
        }

        return changed;
    }

    private static bool ClaimsContextDiffers(UserOnboardingCacheItem cached, ClaimsPrincipal principal)
    {
        var userRole = principal.FindFirstValue("user_role");
        if (!string.IsNullOrWhiteSpace(userRole) && !string.Equals(cached.UserRole, userRole, StringComparison.Ordinal))
        {
            return true;
        }

        var orgRole = principal.FindFirstValue("org_role");
        return !string.IsNullOrWhiteSpace(orgRole)
               && !string.Equals(cached.OrganizationRole, orgRole, StringComparison.Ordinal);
    }

    private async Task<UserOnboardingCacheItem> PersistAndRefreshCachesAsync(
        User user,
        CancellationToken cancellationToken)
    {
        user.UpdatedAt = DateTime.UtcNow;
        await _userRepository.UpdateAsync(user, cancellationToken);
        _logger.LogInformation("User read model synchronized from refreshed session claims. clerkId={ClerkId}", user.ClerkId);

        var cacheItem = MapToCacheItem(user);
        var dto = UserDto.FromEntity(user);
        await _cacheService.SetUserAsync(user.ClerkId, cacheItem, cancellationToken: cancellationToken);
        await _cacheService.SetUserProfileAsync(user.ClerkId, dto, cancellationToken: cancellationToken);
        return cacheItem;
    }

    private static UserOnboardingCacheItem MapToCacheItem(User user)
    {
        return new UserOnboardingCacheItem
        {
            Id = user.Id,
            ClerkId = user.ClerkId,
            HasCompletedOnboarding = user.HasCompletedOnboarding,
            Email = user.Email,
            FirstName = user.FirstName,
            LastName = user.LastName,
            DisplayName = user.DisplayName,
            PhoneNumber = user.PhoneNumber,
            Address = user.Address,
            ProfileImageUrl = user.ProfileImageUrl,
            UserRole = user.UserRole,
            OrganizationRole = user.OrganizationRole,
            AccountState = user.AccountState,
        };
    }
}
