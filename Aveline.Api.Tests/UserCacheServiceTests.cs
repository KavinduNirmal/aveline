using System.Text;
using System.Text.Json;
using Aveline.Api.Infrastructure.Caching;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Aveline.Api.Tests;

public class UserCacheServiceTests
{
    private readonly IDistributedCache _cache;
    private readonly UserCacheService _sut;

    public UserCacheServiceTests()
    {
        var memoryCacheOptions = Options.Create(new MemoryDistributedCacheOptions());
        _cache = new MemoryDistributedCache(memoryCacheOptions);
        _sut = new UserCacheService(_cache, NullLogger<UserCacheService>.Instance);
    }

    [Fact]
    public async Task GetUserAsync_WhenKeyDoesNotExist_ReturnsNull()
    {
        var result = await _sut.GetUserAsync("clerk_non_existent");
        Assert.Null(result);
    }

    [Fact]
    public async Task SetUserAsync_ThenGetUserAsync_ReturnsCachedItem()
    {
        var item = new UserOnboardingCacheItem
        {
            Id = Guid.NewGuid(),
            ClerkId = "user_clerk_123",
            HasCompletedOnboarding = true,
            Email = "jane@aveline.lk",
            FirstName = "Jane",
            LastName = "Doe",
            DisplayName = "Jane Doe",
            PhoneNumber = "+94770000000",
            Address = "Galle Road, Colombo",
            UserRole = "manager",
            OrganizationRole = "org:admin"
        };

        await _sut.SetUserAsync("user_clerk_123", item);

        var retrieved = await _sut.GetUserAsync("user_clerk_123");

        Assert.NotNull(retrieved);
        Assert.Equal(item.Id, retrieved.Id);
        Assert.Equal("user_clerk_123", retrieved.ClerkId);
        Assert.True(retrieved.HasCompletedOnboarding);
        Assert.Equal("jane@aveline.lk", retrieved.Email);
        Assert.Equal("Jane Doe", retrieved.DisplayName);
        Assert.Equal("manager", retrieved.UserRole);
    }

    [Fact]
    public async Task InvalidateAsync_RemovesItemFromCache()
    {
        var item = new UserOnboardingCacheItem
        {
            Id = Guid.NewGuid(),
            ClerkId = "user_to_invalidate",
            HasCompletedOnboarding = false,
            Email = "test@aveline.lk"
        };

        await _sut.SetUserAsync("user_to_invalidate", item);
        var before = await _sut.GetUserAsync("user_to_invalidate");
        Assert.NotNull(before);

        await _sut.InvalidateAsync("user_to_invalidate");

        var after = await _sut.GetUserAsync("user_to_invalidate");
        Assert.Null(after);
    }

    [Fact]
    public async Task GetUserProfileAsync_WhenKeyDoesNotExist_ReturnsNull()
    {
        var result = await _sut.GetUserProfileAsync("clerk_non_existent_profile");
        Assert.Null(result);
    }

    [Fact]
    public async Task SetUserProfileAsync_ThenGetUserProfileAsync_ReturnsCachedProfile()
    {
        var dto = new Aveline.Api.Modules.Shared.DTOs.UserDto
        {
            Id = Guid.NewGuid(),
            ClerkId = "user_profile_123",
            Email = "kasun@aveline.lk",
            FirstName = "Kasun",
            LastName = "Delpachithra",
            DisplayName = "Kasun Delpachithra",
            Username = "kasund",
            PhoneNumber = "+94771234567",
            Address = "15 Alfred House Gardens, Colombo 03",
            ProfileImageUrl = "https://img.clerk.com/avatar.png",
            UserRole = "owner",
            OrganizationRole = "org:admin",
            OrganizationId = "org_colombo",
            HasCompletedOnboarding = true,
            ContactPreference = Aveline.Api.Modules.Shared.Models.ContactPreferences.WhatsApp,
            PushNotificationsEnabled = true,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _sut.SetUserProfileAsync("user_profile_123", dto);

        var retrieved = await _sut.GetUserProfileAsync("user_profile_123");

        Assert.NotNull(retrieved);
        Assert.Equal(dto.Id, retrieved.Id);
        Assert.Equal("user_profile_123", retrieved.ClerkId);
        Assert.Equal("kasun@aveline.lk", retrieved.Email);
        Assert.Equal("Kasun Delpachithra", retrieved.DisplayName);
        Assert.Equal("owner", retrieved.UserRole);
        Assert.Equal("org:admin", retrieved.OrganizationRole);
        Assert.True(retrieved.HasCompletedOnboarding);
        Assert.Equal(Aveline.Api.Modules.Shared.Models.ContactPreferences.WhatsApp, retrieved.ContactPreference);
        Assert.True(retrieved.PushNotificationsEnabled);
    }

    [Fact]
    public async Task InvalidateAsync_RemovesBothOnboardingAndProfileCache()
    {
        var onboardingItem = new UserOnboardingCacheItem
        {
            Id = Guid.NewGuid(),
            ClerkId = "user_dual_invalidate",
            HasCompletedOnboarding = true
        };
        var profileDto = new Aveline.Api.Modules.Shared.DTOs.UserDto
        {
            Id = onboardingItem.Id,
            ClerkId = "user_dual_invalidate",
            Email = "dual@aveline.lk"
        };

        await _sut.SetUserAsync("user_dual_invalidate", onboardingItem);
        await _sut.SetUserProfileAsync("user_dual_invalidate", profileDto);

        Assert.NotNull(await _sut.GetUserAsync("user_dual_invalidate"));
        Assert.NotNull(await _sut.GetUserProfileAsync("user_dual_invalidate"));

        await _sut.InvalidateAsync("user_dual_invalidate");

        Assert.Null(await _sut.GetUserAsync("user_dual_invalidate"));
        Assert.Null(await _sut.GetUserProfileAsync("user_dual_invalidate"));
    }
}
