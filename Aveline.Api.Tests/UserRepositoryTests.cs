using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Shared.Models;
using Aveline.Api.Modules.Shared.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Tests;

public class UserRepositoryTests
{
    private readonly AppDbContext _context;
    private readonly UserRepository _sut;

    public UserRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"UserRepositoryTests_{Guid.NewGuid()}")
            .Options;

        _context = new AppDbContext(options);
        _sut = new UserRepository(_context);
    }

    [Fact]
    public async Task CreateAsync_PersistsUserToDatabase()
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            ClerkId = "user_repo_test_1",
            Email = "owner@aveline.lk",
            FirstName = "Boutique",
            LastName = "Owner",
            Username = "boutique_owner",
            UserRole = "owner",
            OrganizationRole = "org:admin",
            HasCompletedOnboarding = false
        };

        var created = await _sut.CreateAsync(user);

        Assert.NotNull(created);
        Assert.NotEqual(default, created.CreatedAt);

        var retrieved = await _sut.GetByClerkIdAsync("user_repo_test_1");
        Assert.NotNull(retrieved);
        Assert.Equal("owner@aveline.lk", retrieved.Email);
    }

    [Fact]
    public async Task UpdateAsync_UpdatesUserProperties()
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            ClerkId = "user_update_test",
            Email = "update@aveline.lk",
            FirstName = "First",
            LastName = "Last",
            Username = "user_upd",
            UserRole = "associate",
            OrganizationRole = "org:member",
            HasCompletedOnboarding = false
        };

        await _sut.CreateAsync(user);

        user.DisplayName = "Updated Associate Name";
        user.PhoneNumber = "+94711122334";
        user.Address = "45 Galle Face, Colombo";
        user.HasCompletedOnboarding = true;

        await _sut.UpdateAsync(user);

        var updated = await _sut.GetByClerkIdAsync("user_update_test");
        Assert.NotNull(updated);
        Assert.True(updated.HasCompletedOnboarding);
        Assert.Equal("Updated Associate Name", updated.DisplayName);
        Assert.Equal("+94711122334", updated.PhoneNumber);
    }

    [Fact]
    public async Task SoftDeletedUser_IsNotReturnedByQueries()
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            ClerkId = "user_soft_deleted",
            Email = "deleted@aveline.lk",
            FirstName = "Del",
            LastName = "User",
            Username = "del_user",
            UserRole = "user",
            OrganizationRole = "org:member",
            DeletedAt = DateTime.UtcNow
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var retrieved = await _sut.GetByClerkIdAsync("user_soft_deleted");
        Assert.Null(retrieved);
    }
}
