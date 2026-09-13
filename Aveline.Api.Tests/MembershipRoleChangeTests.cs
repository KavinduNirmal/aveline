using Aveline.Api.Authorization;
using Aveline.Api.Infrastructure.Caching;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Organizations.Repositories;
using Aveline.Api.Modules.Organizations.Services;
using Aveline.Api.Modules.Shared.Models;
using Aveline.Api.Modules.Shared.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Aveline.Api.Tests;

/// <summary>
/// Issue #205 — the member role-change guards (FR-3.4).
/// </summary>
public class MembershipRoleChangeTests
{
    private readonly AppDbContext _context;
    private readonly OrganizationService _sut;

    public MembershipRoleChangeTests()
    {
        _context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"RoleChange_{Guid.NewGuid()}")
            .Options);

        var cache = new UserCacheService(
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
            NullLogger<UserCacheService>.Instance);

        _sut = new OrganizationService(
            new OrganizationRepository(_context),
            new InvitationRepository(_context),
            new FakeInvitationCodeStore(),
            new UserRepository(_context),
            cache,
            NullLogger<OrganizationService>.Instance);
    }

    private async Task<(Organization Org, User Owner, User Manager, User Staff)> SeedAsync()
    {
        var owner = NewUser(Roles.BoutiqueOwner);
        var manager = NewUser(Roles.BoutiqueManager);
        var staff = NewUser(Roles.BoutiqueStaff);
        _context.Users.AddRange(owner, manager, staff);

        var org = new Organization
        {
            Name = "Role Boutique", Slug = $"role-{Guid.NewGuid():N}", OwnerUserId = owner.Id,
        };
        _context.Organizations.Add(org);
        _context.OrganizationMemberships.AddRange(
            Membership(org.Id, owner.Id, Roles.BoutiqueOwner),
            Membership(org.Id, manager.Id, Roles.BoutiqueManager),
            Membership(org.Id, staff.Id, Roles.BoutiqueStaff));
        await _context.SaveChangesAsync();
        return (org, owner, manager, staff);
    }

    private static User NewUser(string role)
    {
        var id = Guid.NewGuid();
        return new User
        {
            Id = id,
            ClerkId = $"{role}_{id:N}",
            Email = $"{id:N}@aveline.lk",
            FirstName = "T",
            LastName = "U",
            Username = id.ToString("N"),
            UserRole = role,
            OrganizationRole = role,
        };
    }

    private static OrganizationMembership Membership(Guid orgId, Guid userId, string role) => new()
    {
        OrganizationId = orgId,
        UserId = userId,
        BoutiqueRole = role,
        Status = MembershipStatus.Active,
    };

    [Fact]
    public async Task ChangeMemberRole_ChangingOwnRole_IsRejected()
    {
        var (org, owner, manager, _) = await SeedAsync();

        await Assert.ThrowsAsync<CannotChangeOwnRoleException>(() =>
            _sut.ChangeMemberRoleAsync(org.Id, owner.Id, Roles.BoutiqueManager, owner.Id));
    }

    [Fact]
    public async Task ChangeMemberRole_OwnerMayPromoteAManagerToOwner()
    {
        var (org, owner, manager, _) = await SeedAsync();

        var updated = await _sut.ChangeMemberRoleAsync(
            org.Id, manager.Id, Roles.BoutiqueOwner, owner.Id);

        Assert.Equal(Roles.BoutiqueOwner, updated.BoutiqueRole);
    }

    [Fact]
    public async Task ChangeMemberRole_DemotingTheLastOwner_IsRejected()
    {
        var (org, owner, manager, _) = await SeedAsync();

        await Assert.ThrowsAsync<CannotDemoteLastOwnerException>(() =>
            _sut.ChangeMemberRoleAsync(org.Id, owner.Id, Roles.BoutiqueManager, manager.Id));
    }

    [Fact]
    public async Task ChangeMemberRole_DemotingAnOwnerWhenAnotherExists_IsAllowedByAnOwner()
    {
        var (org, owner, manager, _) = await SeedAsync();
        await _sut.ChangeMemberRoleAsync(org.Id, manager.Id, Roles.BoutiqueOwner, owner.Id);

        var updated = await _sut.ChangeMemberRoleAsync(
            org.Id, manager.Id, Roles.BoutiqueManager, owner.Id);

        Assert.Equal(Roles.BoutiqueManager, updated.BoutiqueRole);
    }

    [Fact]
    public async Task ChangeMemberRole_GrantingOwnerByANonOwner_IsRejected()
    {
        var (org, owner, manager, staff) = await SeedAsync();

        await Assert.ThrowsAsync<OwnerRoleChangeNotPermittedException>(() =>
            _sut.ChangeMemberRoleAsync(org.Id, staff.Id, Roles.BoutiqueOwner, manager.Id));
    }

    [Fact]
    public async Task ChangeMemberRole_RevokingOwnerByANonOwner_IsRejected()
    {
        var (org, owner, manager, staff) = await SeedAsync();
        await _sut.ChangeMemberRoleAsync(org.Id, manager.Id, Roles.BoutiqueOwner, owner.Id);

        await Assert.ThrowsAsync<OwnerRoleChangeNotPermittedException>(() =>
            _sut.ChangeMemberRoleAsync(org.Id, manager.Id, Roles.BoutiqueStaff, staff.Id));
    }

    [Fact]
    public async Task ChangeMemberRole_WithAnUnknownRole_IsRejected()
    {
        var (org, owner, _, staff) = await SeedAsync();

        await Assert.ThrowsAsync<InvalidBoutiqueRoleException>(() =>
            _sut.ChangeMemberRoleAsync(org.Id, staff.Id, "org:root", owner.Id));
    }

    [Fact]
    public async Task ChangeMemberRole_ForAnUnknownMember_ThrowsNotFound()
    {
        var (org, owner, _, _) = await SeedAsync();

        await Assert.ThrowsAsync<MembershipNotFoundException>(() =>
            _sut.ChangeMemberRoleAsync(org.Id, Guid.CreateVersion7(), Roles.BoutiqueStaff, owner.Id));
    }
}
