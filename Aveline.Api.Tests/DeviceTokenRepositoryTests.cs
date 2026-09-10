using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Notifications.Models;
using Aveline.Api.Modules.Notifications.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Tests;

public class DeviceTokenRepositoryTests
{
    private readonly AppDbContext _context;
    private readonly DeviceTokenRepository _sut;

    public DeviceTokenRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"DeviceTokenRepositoryTests_{Guid.NewGuid()}")
            .Options;
        _context = new AppDbContext(options);
        _sut = new DeviceTokenRepository(_context);
    }

    [Fact]
    public async Task UpsertAsync_NewToken_AddsActiveToken()
    {
        var userId = Guid.NewGuid();

        await _sut.UpsertAsync(new UserDeviceToken { UserId = userId, Token = "tok-1", Platform = DevicePlatform.Android });

        var stored = await _context.UserDeviceTokens.SingleAsync();
        Assert.Equal(userId, stored.UserId);
        Assert.Equal("tok-1", stored.Token);
        Assert.True(stored.IsActive);
        Assert.NotEqual(default, stored.CreatedAt);
    }

    [Fact]
    public async Task UpsertAsync_ExistingToken_UpdatesLastSeenAndReactivates()
    {
        var userId = Guid.NewGuid();
        await _sut.UpsertAsync(new UserDeviceToken { UserId = userId, Token = "tok-1", Platform = DevicePlatform.Android });

        // Deactivate, then upsert again (simulating a re-registration).
        await _sut.DeactivateAsync(userId, "tok-1");
        Assert.False((await _context.UserDeviceTokens.SingleAsync()).IsActive);

        await _sut.UpsertAsync(new UserDeviceToken { UserId = userId, Token = "tok-1", Platform = DevicePlatform.IOS });

        var stored = await _context.UserDeviceTokens.SingleAsync();
        Assert.True(stored.IsActive);
        Assert.Equal(DevicePlatform.IOS, stored.Platform);
        Assert.Equal(1, await _context.UserDeviceTokens.CountAsync());
    }

    [Fact]
    public async Task ListActiveTokensAsync_ReturnsOnlyActiveTokensForUser()
    {
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        await _sut.UpsertAsync(new UserDeviceToken { UserId = userA, Token = "a-1", Platform = DevicePlatform.Android });
        await _sut.UpsertAsync(new UserDeviceToken { UserId = userA, Token = "a-2", Platform = DevicePlatform.IOS });
        await _sut.UpsertAsync(new UserDeviceToken { UserId = userB, Token = "b-1", Platform = DevicePlatform.Web });
        await _sut.DeactivateAsync(userA, "a-2");

        var tokens = await _sut.ListActiveTokensAsync(userA);

        var token = Assert.Single(tokens);
        Assert.Equal("a-1", token);
    }

    [Fact]
    public async Task DeactivateAsync_WrongUser_IsNoOp()
    {
        var userA = Guid.NewGuid();
        var otherUser = Guid.NewGuid();
        await _sut.UpsertAsync(new UserDeviceToken { UserId = userA, Token = "tok-a", Platform = DevicePlatform.Android });

        // A token belongs to exactly one user; deactivating with a different user id is a no-op.
        await _sut.DeactivateAsync(otherUser, "tok-a");

        var stored = await _context.UserDeviceTokens.SingleAsync();
        Assert.True(stored.IsActive);
    }
}
