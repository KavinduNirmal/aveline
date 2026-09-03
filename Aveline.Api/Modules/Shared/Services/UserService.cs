using System.Security.Claims;
using Aveline.Api.Infrastructure.Caching;
using Aveline.Api.Modules.Shared.DTOs;
using Aveline.Api.Modules.Shared.Models;
using Aveline.Api.Modules.Shared.Repositories;
using Microsoft.Extensions.Logging;

namespace Aveline.Api.Modules.Shared.Services;

public class UserService : IUserService
{
    private readonly IUserRepository _userRepository;
    private readonly IUserCacheService _cacheService;
    private readonly ILogger<UserService> _logger;

    public UserService(
        IUserRepository userRepository,
        IUserCacheService cacheService,
        ILogger<UserService> logger)
    {
        _userRepository = userRepository;
        _cacheService = cacheService;
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
            return cached;
        }

        // 2. Cache Miss - Query Database
        var user = await _userRepository.GetByClerkIdAsync(clerkId, cancellationToken);
        if (user == null)
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

            user = new User
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
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await _userRepository.CreateAsync(user, cancellationToken);
            _logger.LogInformation("Created new Aveline DB stub user for ClerkId={ClerkId}, Email={Email}", clerkId, email);
        }

        var cacheItem = MapToCacheItem(user);
        var userDto = UserDto.FromEntity(user);
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
            OrganizationRole = user.OrganizationRole
        };
    }
}
