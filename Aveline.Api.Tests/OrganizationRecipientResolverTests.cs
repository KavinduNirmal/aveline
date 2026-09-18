using Aveline.Api.Authorization;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Notifications.Models;
using Aveline.Api.Modules.Notifications.Repositories;
using Aveline.Api.Modules.Notifications.Services;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Organizations.Repositories;
using Aveline.Api.Modules.Shared.Models;
using Aveline.Api.Modules.Shared.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Tests;

public class OrganizationRecipientResolverTests
{
    private readonly AppDbContext _context;
    private readonly OrganizationRecipientResolver _sut;

    public OrganizationRecipientResolverTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"RecipientResolverTests_{Guid.NewGuid()}")
            .Options;
        _context = new AppDbContext(options);
        _sut = new OrganizationRecipientResolver(
            new OrganizationRepository(_context),
            new UserRepository(_context),
            new DeviceTokenRepository(_context));
    }

    private static User NewUser(string clerkId, string email) => new()
    {
        Id = Guid.CreateVersion7(),
        ClerkId = clerkId,
        Email = email,
        FirstName = "Test",
        LastName = "User",
        Username = clerkId,
        UserRole = Roles.Staff,
        OrganizationRole = string.Empty,
        HasCompletedOnboarding = true,
    };

    private async Task<(Guid orgId, User owner, User manager, User staff, User pending)> SeedAsync()
    {
        var org = new Organization { Name = "Boutique", Slug = "boutique", OwnerUserId = Guid.CreateVersion7() };
        _context.Organizations.Add(org);

        var owner = NewUser("owner", "owner@aveline.lk");
        var manager = NewUser("manager", "manager@aveline.lk");
        var staff = NewUser("staff", "staff@aveline.lk");
        var pending = NewUser("pending", "pending@aveline.lk");
        _context.Users.AddRange(owner, manager, staff, pending);

        _context.OrganizationMemberships.AddRange(
            new OrganizationMembership { OrganizationId = org.Id, UserId = owner.Id, BoutiqueRole = Roles.BoutiqueOwner, Status = MembershipStatus.Active, User = owner },
            new OrganizationMembership { OrganizationId = org.Id, UserId = manager.Id, BoutiqueRole = Roles.BoutiqueManager, Status = MembershipStatus.Active, User = manager },
            new OrganizationMembership { OrganizationId = org.Id, UserId = staff.Id, BoutiqueRole = Roles.BoutiqueStaff, Status = MembershipStatus.Active, User = staff },
            new OrganizationMembership { OrganizationId = org.Id, UserId = pending.Id, BoutiqueRole = Roles.BoutiqueStaff, Status = MembershipStatus.Pending, User = pending });

        await _context.SaveChangesAsync();
        return (org.Id, owner, manager, staff, pending);
    }

    private static Notification NotificationFor(Guid orgId, NotificationTarget target) => new(
        NotificationType.NewMessage,
        "Title",
        "Body",
        target,
        new Dictionary<string, string?>(),
        NotificationChannel.Realtime);

    [Fact]
    public async Task ResolveAsync_NoRoleFilter_ReturnsAllActiveMembers()
    {
        var (orgId, owner, manager, staff, _) = await SeedAsync();

        var result = await _sut.ResolveAsync(NotificationFor(orgId, new NotificationTarget(orgId)));

        var ids = result.Select(r => r.UserId).OrderBy(id => id).ToArray();
        Assert.Equal(3, ids.Length);
        Assert.Contains(owner.Id, ids);
        Assert.Contains(manager.Id, ids);
        Assert.Contains(staff.Id, ids);
    }

    [Fact]
    public async Task ResolveAsync_RoleFilter_ReturnsOnlyMatchingRoles()
    {
        var (orgId, owner, manager, _, _) = await SeedAsync();

        var target = new NotificationTarget(orgId, Roles: [Roles.BoutiqueOwner, Roles.BoutiqueManager]);
        var result = await _sut.ResolveAsync(NotificationFor(orgId, target));

        var ids = result.Select(r => r.UserId).ToArray();
        Assert.Equal(2, ids.Length);
        Assert.Contains(owner.Id, ids);
        Assert.Contains(manager.Id, ids);
    }

    [Fact]
    public async Task ResolveAsync_SpecificActiveUser_ReturnsThatUser()
    {
        var (orgId, _, _, staff, _) = await SeedAsync();

        var target = new NotificationTarget(orgId, SpecificUserId: staff.Id);
        var result = await _sut.ResolveAsync(NotificationFor(orgId, target));

        var recipient = Assert.Single(result);
        Assert.Equal(staff.Id, recipient.UserId);
        Assert.Equal(staff.Email, recipient.Email);
    }

    [Fact]
    public async Task ResolveAsync_SpecificUserNotActive_ReturnsEmpty()
    {
        var (orgId, _, _, _, pending) = await SeedAsync();

        var target = new NotificationTarget(orgId, SpecificUserId: pending.Id);
        var result = await _sut.ResolveAsync(NotificationFor(orgId, target));

        Assert.Empty(result);
    }

    [Fact]
    public async Task ResolveAsync_EmptyOrg_ReturnsEmpty()
    {
        var emptyOrgId = Guid.CreateVersion7();

        var result = await _sut.ResolveAsync(NotificationFor(emptyOrgId, new NotificationTarget(emptyOrgId)));

        Assert.Empty(result);
    }

    [Fact]
    public async Task ResolveAsync_PopulatesActiveDeviceTokens_ForResolvedUser()
    {
        var (orgId, _, _, staff, _) = await SeedAsync();

        _context.UserDeviceTokens.AddRange(
            new UserDeviceToken { UserId = staff.Id, Token = "active-tok", Platform = DevicePlatform.Android, IsActive = true },
            new UserDeviceToken { UserId = staff.Id, Token = "inactive-tok", Platform = DevicePlatform.IOS, IsActive = false });
        await _context.SaveChangesAsync();

        var target = new NotificationTarget(orgId, SpecificUserId: staff.Id);
        var result = await _sut.ResolveAsync(NotificationFor(orgId, target));

        var recipient = Assert.Single(result);
        var token = Assert.Single(recipient.DeviceTokens);
        Assert.Equal("active-tok", token);
    }
}
