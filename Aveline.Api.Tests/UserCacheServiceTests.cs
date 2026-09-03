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
}
