using System.Security.Claims;
using Aveline.Api.Infrastructure.Caching;
using Aveline.Api.Infrastructure.Eventing;
using Aveline.Api.Modules.ApiAccess.Repositories;
using Aveline.Api.Modules.Audit.Models;
using Aveline.Api.Modules.Audit.Services;
using Aveline.Api.Modules.Organizations.Repositories;
using Aveline.Api.Modules.Organizations.Services;
using Aveline.Api.Modules.Shared.DTOs;
using Aveline.Api.Modules.Shared.Models;
using Aveline.Api.Modules.Shared.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
namespace Aveline.Api.Modules.Shared.Services;

public class UserService : IUserService
{
    /// <summary>The admin user search paging convention (docs/api/README.md §A.4): 50 default / 200 max.</summary>
    private const int DefaultAdminUserPageSize = 50;
    private const int MaxAdminUserPageSize = 200;

    private readonly IUserRepository _userRepository;
    private readonly IUserCacheService _cacheService;
    private readonly IOrganizationService _organizationService;
    private readonly ILogger<UserService> _logger;
    private readonly IOrganizationRepository? _organizationRepository;
    private readonly IApiKeyRepository? _apiKeyRepository;
    private readonly IAuditService? _auditService;
    private readonly IEventBus? _eventBus;

    public UserService(
        IUserRepository userRepository,
        IUserCacheService cacheService,
        IOrganizationService organizationService,
        ILogger<UserService> logger,
        IOrganizationRepository? organizationRepository = null,
        IApiKeyRepository? apiKeyRepository = null,
        IAuditService? auditService = null,
        IEventBus? eventBus = null)
    {
        _userRepository = userRepository;
        _cacheService = cacheService;
        _organizationService = organizationService;
        _logger = logger;
        _organizationRepository = organizationRepository;
        _apiKeyRepository = apiKeyRepository;
        _auditService = auditService;
        _eventBus = eventBus;
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

            try
            {
                await _userRepository.CreateAsync(user, cancellationToken);
                _logger.LogInformation("Created new Aveline DB stub user for ClerkId={ClerkId}, Email={Email}", clerkId, email);
                dbUser = user;
            }
            catch (DbUpdateException ex) when (IsDuplicateClerkId(ex))
            {
                // A first page load fires several requests at once (the notifications hub
                // negotiate and /users/me among them). Each finds no row and each runs this
                // insert, so all but one lose the race against the unique index on ClerkId.
                // The loser adopts the winner's row instead of surfacing an unhandled 500.
                var winner = await _userRepository.GetByClerkIdAsync(clerkId, cancellationToken);
                if (winner == null)
                {
                    throw;
                }

                _logger.LogInformation(
                    "Concurrent first-login for ClerkId={ClerkId}; adopting the row created by the winning request.",
                    clerkId);
                dbUser = winner;
            }
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

    public async Task<UserDto?> UpdateProfileAsync(
        string clerkId,
        UpdateUserProfileRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var user = await _userRepository.GetByClerkIdAsync(clerkId, cancellationToken);
        if (user is null)
        {
            return null;
        }

        if (request.FirstName is not null)
        {
            user.FirstName = RequireText(request.FirstName, "firstName", 100);
        }

        if (request.LastName is not null)
        {
            user.LastName = RequireText(request.LastName, "lastName", 100);
        }

        if (request.DisplayName is not null)
        {
            user.DisplayName = OptionalText(request.DisplayName, "displayName", 200);
        }

        if (request.PhoneNumber is not null)
        {
            user.PhoneNumber = OptionalText(request.PhoneNumber, "phoneNumber", 20) ?? string.Empty;
        }

        if (request.ProfileImageUrl is not null)
        {
            user.ProfileImageUrl = OptionalText(request.ProfileImageUrl, "profileImageUrl", 1000);
        }

        if (request.ContactPreference is not null)
        {
            user.ContactPreference = request.ContactPreference.Value;
        }

        if (request.PushNotificationsEnabled is not null)
        {
            user.PushNotificationsEnabled = request.PushNotificationsEnabled.Value;
        }

        user.UpdatedAt = DateTime.UtcNow;
        await _userRepository.UpdateAsync(user, cancellationToken);

        var dto = UserDto.FromEntity(user);
        await _cacheService.SetUserAsync(user.ClerkId, MapToCacheItem(user), cancellationToken: cancellationToken);
        await _cacheService.SetUserProfileAsync(user.ClerkId, dto, cancellationToken: cancellationToken);

        _logger.LogInformation("User profile updated. userId={UserId}", user.Id);

        if (_auditService is not null)
        {
            await _auditService.RecordAsync(new AuditEntryRequest(
                AuditAction.UserProfileUpdated,
                "User",
                user.Id.ToString(),
                ActorKind: AuditActorKind.User,
                ActorUserId: user.Id,
                After: new
                {
                    user.FirstName,
                    user.LastName,
                    user.DisplayName,
                    user.PhoneNumber,
                    user.ContactPreference,
                    user.PushNotificationsEnabled,
                }), cancellationToken);
        }

        if (_eventBus is not null)
        {
            await _eventBus.PublishAsync(
                "user.profile.updated", null, new { userId = user.Id }, cancellationToken: cancellationToken);
        }

        return dto;
    }

    public async Task<UserDto?> DeleteAccountAsync(
        string clerkId,
        CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByClerkIdAsync(clerkId, cancellationToken);
        if (user is null)
        {
            return null;
        }

        if (user.DeletedAt is not null)
        {
            // Idempotent: a second delete changes nothing.
            return UserDto.FromEntity(user);
        }

        var now = DateTime.UtcNow;
        user.DeletedAt = now;
        user.IsActive = false;
        user.AccountState = AccountState.Suspended;
        user.UpdatedAt = now;

        // Sever the tenant relationships and every machine credential the user created.
        if (_organizationRepository is not null)
        {
            await _organizationRepository.RemoveAllMembershipsForUserAsync(user.Id, cancellationToken);
        }

        if (_apiKeyRepository is not null)
        {
            await _apiKeyRepository.RevokeAllForCreatorAsync(
                user.Id, "The owning user deleted their account.", cancellationToken);
        }

        await _userRepository.UpdateAsync(user, cancellationToken);
        await _cacheService.InvalidateAsync(user.ClerkId, cancellationToken);

        _logger.LogInformation("User account soft-deleted. userId={UserId}", user.Id);

        if (_auditService is not null)
        {
            await _auditService.RecordAsync(new AuditEntryRequest(
                AuditAction.UserStateChanged,
                "User",
                user.Id.ToString(),
                ActorKind: AuditActorKind.User,
                ActorUserId: user.Id,
                After: new { AccountState = nameof(AccountState.Suspended), user.IsActive, user.DeletedAt }),
                cancellationToken);
        }

        if (_eventBus is not null)
        {
            await _eventBus.PublishAsync(
                "user.state.changed", null, new { userId = user.Id, state = nameof(AccountState.Suspended) },
                cancellationToken: cancellationToken);
        }

        return UserDto.FromEntity(user);
    }

    public async Task<UserDto> ChangeAccountStateAsync(
        Guid userId,
        AccountState state,
        Guid actorUserId,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null || user.DeletedAt is not null)
        {
            throw new KeyNotFoundException($"User '{userId}' was not found.");
        }

        AccountStateTransitions.EnsureAllowed(user.AccountState, state);

        user.AccountState = state;
        user.IsActive = state != AccountState.Suspended;
        user.UpdatedAt = DateTime.UtcNow;

        await _userRepository.UpdateAsync(user, cancellationToken);
        await _cacheService.InvalidateAsync(user.ClerkId, cancellationToken);

        _logger.LogInformation(
            "Account state changed by an administrator. userId={UserId} state={State} actorUserId={ActorUserId}",
            userId, state, actorUserId);

        if (_auditService is not null)
        {
            await _auditService.RecordAsync(new AuditEntryRequest(
                AuditAction.UserStateChanged,
                "User",
                user.Id.ToString(),
                ActorKind: AuditActorKind.User,
                ActorUserId: actorUserId,
                After: new { AccountState = state.ToString() },
                Reason: string.IsNullOrWhiteSpace(reason) ? null : reason.Trim()), cancellationToken);
        }

        if (_eventBus is not null)
        {
            await _eventBus.PublishAsync(
                "user.state.changed", null, new { userId = user.Id, state = state.ToString() },
                cancellationToken: cancellationToken);
        }

        return UserDto.FromEntity(user);
    }

    public async Task<PagedUsers> SearchUsersAsync(
        string? search,
        AccountState? state,
        Guid? organizationId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1
            ? DefaultAdminUserPageSize
            : Math.Min(pageSize, MaxAdminUserPageSize);

        var (items, total) = await _userRepository.SearchAsync(
            search, state, organizationId, page, pageSize, cancellationToken);

        return new PagedUsers(items.Select(UserDto.FromEntity).ToArray(), page, pageSize, total);
    }

    private static string RequireText(string value, string field, int maxLength)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0 || trimmed.Length > maxLength)
        {
            throw new ArgumentException($"'{field}' must be 1..{maxLength} characters.");
        }

        return trimmed;
    }

    private static string? OptionalText(string value, string field, int maxLength)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        if (trimmed.Length > maxLength)
        {
            throw new ArgumentException($"'{field}' cannot exceed {maxLength} characters.");
        }

        return trimmed;
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

        // Identity claims keep the read model populated even when the user row was
        // first created from a token that predated the email/name claims, and when
        // Clerk webhooks are not configured (see ClerkWebhookSyncService).
        var email = principal.FindFirstValue("email") ?? principal.FindFirstValue(ClaimTypes.Email);
        if (!string.IsNullOrWhiteSpace(email) && !string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase))
        {
            user.Email = email;
            changed = true;
        }

        var firstName = principal.FindFirstValue("first_name") ?? principal.FindFirstValue(ClaimTypes.GivenName);
        if (!string.IsNullOrWhiteSpace(firstName) && !string.Equals(user.FirstName, firstName, StringComparison.Ordinal))
        {
            user.FirstName = firstName;
            changed = true;
        }

        var lastName = principal.FindFirstValue("last_name") ?? principal.FindFirstValue(ClaimTypes.Surname);
        if (!string.IsNullOrWhiteSpace(lastName) && !string.Equals(user.LastName, lastName, StringComparison.Ordinal))
        {
            user.LastName = lastName;
            changed = true;
        }

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

    /// <summary>
    /// True when the failure is the unique-index violation raised by two concurrent first
    /// requests racing to create the same Clerk user row. The constraint is named explicitly so
    /// an unrelated unique violation is never swallowed.
    /// </summary>
    private static bool IsDuplicateClerkId(DbUpdateException ex)
        => ex.InnerException is PostgresException { SqlState: "23505" } postgres
           && string.Equals(postgres.ConstraintName, "IX_Users_ClerkId", StringComparison.Ordinal);

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
