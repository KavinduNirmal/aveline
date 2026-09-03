using System.Security.Claims;
using Aveline.Api.Infrastructure.Caching;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Shared.DTOs;
using Aveline.Api.Modules.Shared.Models;
using Aveline.Api.Modules.Shared.Repositories;
using Aveline.Api.Modules.Shared.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Aveline.Api.Tests;

public class UserServiceTests
{
    private readonly AppDbContext _dbContext;
    private readonly IUserRepository _userRepository;
    private readonly IUserCacheService _cacheService;
    private readonly UserService _sut;

    public UserServiceTests()
    {
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"UserServiceTests_{Guid.NewGuid()}")
            .Options;
        _dbContext = new AppDbContext(dbOptions);
        _userRepository = new UserRepository(_dbContext);

        var memCacheOptions = Options.Create(new MemoryDistributedCacheOptions());
        var distCache = new MemoryDistributedCache(memCacheOptions);
        _cacheService = new UserCacheService(distCache, NullLogger<UserCacheService>.Instance);

        _sut = new UserService(_userRepository, _cacheService, NullLogger<UserService>.Instance);
    }

    [Fact]
    public async Task GetOrSynchronizeUserAsync_WhenInCache_ReturnsCachedWithoutDbCall()
    {
        var cached = new UserOnboardingCacheItem
        {
            Id = Guid.NewGuid(),
            ClerkId = "clerk_cached_user",
            HasCompletedOnboarding = true,
            Email = "cached@aveline.lk",
            DisplayName = "Cached User",
            UserRole = "owner",
            OrganizationRole = "org:admin"
        };
        await _cacheService.SetUserAsync("clerk_cached_user", cached);

        var claimsPrincipal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("sub", "clerk_cached_user"),
            new Claim("email", "cached@aveline.lk")
        }, "TestAuth"));

        var result = await _sut.GetOrSynchronizeUserAsync("clerk_cached_user", claimsPrincipal);

        Assert.NotNull(result);
        Assert.Equal(cached.Id, result.Id);
        Assert.True(result.HasCompletedOnboarding);
        Assert.Equal("Cached User", result.DisplayName);
    }

    [Fact]
    public async Task GetOrSynchronizeUserAsync_WhenNotInCache_ButInDb_ReturnsDbUserAndSetsCache()
    {
        var existingUser = new User
        {
            Id = Guid.NewGuid(),
            ClerkId = "clerk_db_user",
            Email = "dbuser@aveline.lk",
            FirstName = "Alice",
            LastName = "Silva",
            Username = "alice_silva",
            DisplayName = "Alice Silva",
            UserRole = "manager",
            OrganizationRole = "org:admin",
            HasCompletedOnboarding = true
        };
        await _userRepository.CreateAsync(existingUser);

        var claimsPrincipal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("sub", "clerk_db_user"),
            new Claim("email", "dbuser@aveline.lk")
        }, "TestAuth"));

        var result = await _sut.GetOrSynchronizeUserAsync("clerk_db_user", claimsPrincipal);

        Assert.NotNull(result);
        Assert.Equal("clerk_db_user", result.ClerkId);
        Assert.True(result.HasCompletedOnboarding);

        // Verify it was written to cache
        var cached = await _cacheService.GetUserAsync("clerk_db_user");
        Assert.NotNull(cached);
        Assert.Equal("Alice Silva", cached.DisplayName);
    }

    [Fact]
    public async Task GetOrSynchronizeUserAsync_WhenNotInCacheAndNotInDb_CreatesStubAndSavesToCache()
    {
        var claimsPrincipal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("sub", "clerk_new_user"),
            new Claim("email", "newuser@aveline.lk"),
            new Claim("first_name", "Kasun"),
            new Claim("last_name", "Perera"),
            new Claim("user_role", "associate"),
            new Claim("org_role", "org:member")
        }, "TestAuth"));

        var result = await _sut.GetOrSynchronizeUserAsync("clerk_new_user", claimsPrincipal);

        Assert.NotNull(result);
        Assert.Equal("clerk_new_user", result.ClerkId);
        Assert.False(result.HasCompletedOnboarding);
        Assert.Equal("newuser@aveline.lk", result.Email);
        Assert.Equal("associate", result.UserRole);

        // Verify DB contains stub record
        var dbUser = await _userRepository.GetByClerkIdAsync("clerk_new_user");
        Assert.NotNull(dbUser);
        Assert.False(dbUser.HasCompletedOnboarding);
        Assert.Equal("Kasun", dbUser.FirstName);
        Assert.Equal("Perera", dbUser.LastName);

        // Verify cache contains item
        var cached = await _cacheService.GetUserAsync("clerk_new_user");
        Assert.NotNull(cached);
        Assert.False(cached.HasCompletedOnboarding);
    }

    [Fact]
    public async Task CompleteOnboardingAsync_UpdatesDbAndCache_ReturnsUserDto()
    {
        var stubUser = new User
        {
            Id = Guid.NewGuid(),
            ClerkId = "clerk_onboarding_user",
            Email = "onboarding@aveline.lk",
            FirstName = "Nimal",
            LastName = "Fernando",
            Username = "nimal_f",
            UserRole = "associate",
            OrganizationRole = "org:member",
            HasCompletedOnboarding = false
        };
        await _userRepository.CreateAsync(stubUser);

        var request = new CompleteOnboardingRequest
        {
            DisplayName = "Nimal Fernando",
            PhoneNumber = "+94779876543",
            Address = "No 10, Flower Road, Colombo 07",
            ContactPreference = ContactPreferences.SMS,
            PushNotificationsEnabled = true
        };

        var dto = await _sut.CompleteOnboardingAsync("clerk_onboarding_user", request);

        Assert.NotNull(dto);
        Assert.True(dto.HasCompletedOnboarding);
        Assert.Equal("Nimal Fernando", dto.DisplayName);
        Assert.Equal("+94779876543", dto.PhoneNumber);
        Assert.Equal("No 10, Flower Road, Colombo 07", dto.Address);
        Assert.Equal(ContactPreferences.SMS, dto.ContactPreference);
        Assert.True(dto.PushNotificationsEnabled);

        // Verify DB was updated
        var dbUser = await _userRepository.GetByClerkIdAsync("clerk_onboarding_user");
        Assert.NotNull(dbUser);
        Assert.True(dbUser.HasCompletedOnboarding);
        Assert.Equal("Nimal Fernando", dbUser.DisplayName);

        // Verify cache was updated
        var cached = await _cacheService.GetUserAsync("clerk_onboarding_user");
        Assert.NotNull(cached);
        Assert.True(cached.HasCompletedOnboarding);
        Assert.Equal("Nimal Fernando", cached.DisplayName);
    }

    [Fact]
    public async Task CompleteOnboardingAsync_WhenUserNotFound_ThrowsKeyNotFoundException()
    {
        var request = new CompleteOnboardingRequest
        {
            DisplayName = "Nobody",
            PhoneNumber = "+94770000000",
            Address = "Colombo"
        };

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _sut.CompleteOnboardingAsync("non_existent_clerk_id", request));
    }
}
