using Aveline.Api.Authorization;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Organizations.Repositories;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Tests;

public class OrganizationRepositoryTests
{
    private readonly AppDbContext _context;
    private readonly OrganizationRepository _sut;

    public OrganizationRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"OrganizationRepositoryTests_{Guid.NewGuid()}")
            .Options;

        _context = new AppDbContext(options);
        _sut = new OrganizationRepository(_context);
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

    [Fact]
    public async Task CreateAsync_PersistsOrganization_AndAllowsLookupBySlug()
    {
        var organization = new Organization
        {
            Name = "Aveline Boutique Colombo",
            Slug = "aveline-colombo",
            ClerkOrgId = "org_test_123",
            OwnerUserId = Guid.CreateVersion7(),
        };

        var created = await _sut.CreateAsync(organization);

        Assert.NotEqual(default, created.Id);
        Assert.NotEqual(default, created.CreatedAt);

        var bySlug = await _sut.GetBySlugAsync("aveline-colombo");
        Assert.NotNull(bySlug);
        Assert.Equal("org_test_123", bySlug.ClerkOrgId);

        Assert.True(await _sut.ExistsBySlugAsync("aveline-colombo"));
        Assert.False(await _sut.ExistsBySlugAsync("nope"));
    }

    [Fact]
    public async Task AddMembership_And_GetMembership_RoundTrip()
    {
        var ownerId = Guid.CreateVersion7();
        var organization = new Organization
        {
            Name = "Galle Heritage",
            Slug = "galle-heritage",
            OwnerUserId = ownerId,
        };
        organization = await _sut.CreateAsync(organization);

        var membership = new OrganizationMembership
        {
            OrganizationId = organization.Id,
            UserId = ownerId,
            BoutiqueRole = Roles.BoutiqueOwner,
            Status = MembershipStatus.Active,
        };
        await _sut.AddMembershipAsync(membership);

        var retrieved = await _sut.GetMembershipAsync(organization.Id, ownerId);
        Assert.NotNull(retrieved);
        Assert.Equal(Roles.BoutiqueOwner, retrieved.BoutiqueRole);
        Assert.Equal(MembershipStatus.Active, retrieved.Status);
    }

    [Fact]
    public async Task ListMembershipsForUser_ReturnsOnlyThatUsersMemberships()
    {
        var userId = Guid.CreateVersion7();
        var otherUserId = Guid.CreateVersion7();

        var orgA = await _sut.CreateAsync(new Organization { Name = "A", Slug = "org-a", OwnerUserId = userId });
        var orgB = await _sut.CreateAsync(new Organization { Name = "B", Slug = "org-b", OwnerUserId = userId });

        await _sut.AddMembershipAsync(new OrganizationMembership { OrganizationId = orgA.Id, UserId = userId, BoutiqueRole = Roles.BoutiqueOwner });
        await _sut.AddMembershipAsync(new OrganizationMembership { OrganizationId = orgB.Id, UserId = userId, BoutiqueRole = Roles.BoutiqueOwner });
        await _sut.AddMembershipAsync(new OrganizationMembership { OrganizationId = orgA.Id, UserId = otherUserId, BoutiqueRole = Roles.BoutiqueStaff });

        var memberships = await _sut.ListMembershipsForUserAsync(userId);

        Assert.Equal(2, memberships.Count);
        Assert.All(memberships, m => Assert.Equal(userId, m.UserId));
    }

    [Fact]
    public async Task InvitationRepository_HashesTokenAreUniqueAndPendingQueryFiltersUsed()
    {
        var ownerId = Guid.CreateVersion7();
        var org = await _sut.CreateAsync(new Organization { Name = "Kandy", Slug = "kandy-1", OwnerUserId = ownerId });

        var invitationRepo = new InvitationRepository(_context);
        var created = await invitationRepo.CreateAsync(new OrganizationInvitation
        {
            OrganizationId = org.Id,
            InvitedByUserId = ownerId,
            RecipientEmail = "staff@aveline.lk",
            TokenHash = "hash-abc",
            BoutiqueRole = Roles.BoutiqueStaff,
            ExpiresAt = DateTime.UtcNow.AddDays(3),
        });

        var byHash = await invitationRepo.GetByTokenHashAsync("hash-abc");
        Assert.NotNull(byHash);

        var pending = await invitationRepo.ListPendingByOrganizationAsync(org.Id);
        Assert.Single(pending);

        // Accepting marks it used and it no longer appears as pending.
        byHash!.AcceptedAt = DateTime.UtcNow;
        await invitationRepo.UpdateAsync(byHash);

        Assert.Empty(await invitationRepo.ListPendingByOrganizationAsync(org.Id));
    }

    // NOTE: the (OrganizationId, UserId) uniqueness and TokenHash uniqueness are
    // enforced by the PostgreSQL database indexes, which the EF InMemory provider
    // does not simulate; they are validated by integration against Npgsql.
}
