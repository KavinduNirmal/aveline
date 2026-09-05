using Aveline.Api.Authorization;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Organizations.Repositories;
using Aveline.Api.Modules.Organizations.Services;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aveline.Api.Tests;

public class OrganizationServiceTests
{
    private readonly AppDbContext _context;
    private readonly OrganizationService _sut;

    public OrganizationServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"OrganizationServiceTests_{Guid.NewGuid()}")
            .Options;

        _context = new AppDbContext(options);
        _sut = new OrganizationService(
            new OrganizationRepository(_context),
            new InvitationRepository(_context),
            NullLogger<OrganizationService>.Instance);
    }

    private async Task<User> AddUserAsync(string clerkId, string email)
    {
        var user = new User
        {
            Id = Guid.CreateVersion7(),
            ClerkId = clerkId,
            Email = email,
            FirstName = "Test",
            LastName = "User",
            Username = clerkId,
            UserRole = Roles.Staff,
            OrganizationRole = string.Empty,
            HasCompletedOnboarding = false,
        };
        _context.Users.Add(user);
        await _context.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task CreateOrganizationAsync_CreatesOrganization_AndOwnerMembership()
    {
        var owner = await AddUserAsync("clerk_owner", "owner@aveline.lk");

        var organization = await _sut.CreateOrganizationAsync(owner.Id, "Aveline Boutique Colombo", "org_aveline_1");

        Assert.Equal("aveline-boutique-colombo", organization.Slug);
        Assert.Equal("org_aveline_1", organization.ClerkOrgId);
        Assert.Equal(owner.Id, organization.OwnerUserId);

        var memberships = await _sut.GetUserMembershipsAsync(owner.Id);
        var ownerMembership = Assert.Single(memberships);
        Assert.Equal(organization.Id, ownerMembership.OrganizationId);
        Assert.Equal(Roles.BoutiqueOwner, ownerMembership.BoutiqueRole);
        Assert.Equal(MembershipStatus.Active, ownerMembership.Status);
    }

    [Fact]
    public async Task CreateOrganizationAsync_Throws_WhenSlugTaken()
    {
        var owner = await AddUserAsync("clerk_owner2", "owner2@aveline.lk");
        await _sut.CreateOrganizationAsync(owner.Id, "Aveline Boutique Colombo", null);

        await Assert.ThrowsAsync<OrganizationSlugAlreadyInUseException>(() =>
            _sut.CreateOrganizationAsync(owner.Id, "Aveline Boutique Colombo", null));
    }

    [Fact]
    public async Task InviteMemberAsync_StoresOnlyTokenHash_AndReturnsCode()
    {
        var owner = await AddUserAsync("clerk_owner3", "owner3@aveline.lk");
        var organization = await _sut.CreateOrganizationAsync(owner.Id, "Boutique", null);

        var result = await _sut.InviteMemberAsync(
            organization.Id,
            owner.Id,
            new InviteMemberRequest(Roles.BoutiqueManager, RecipientEmail: "new@aveline.lk"));

        Assert.False(string.IsNullOrEmpty(result.Code));
        Assert.Equal(InvitationTokens.Hash(result.Code), result.Invitation.TokenHash);
        Assert.NotEqual(result.Code, result.Invitation.TokenHash);
        Assert.Equal("new@aveline.lk", result.Invitation.RecipientEmail);
    }

    [Fact]
    public async Task AcceptInvitationAsync_ActivatesMembership_AndIsOneTime()
    {
        var owner = await AddUserAsync("clerk_owner4", "owner4@aveline.lk");
        var staff = await AddUserAsync("clerk_staff4", "staff4@aveline.lk");
        var organization = await _sut.CreateOrganizationAsync(owner.Id, "Boutique", "org_x");

        var invite = await _sut.InviteMemberAsync(
            organization.Id, owner.Id,
            new InviteMemberRequest(Roles.BoutiqueStaff, RecipientEmail: staff.Email));

        var accepted = await _sut.AcceptInvitationAsync(invite.Code, staff.Id, staff.Email);

        Assert.Equal(organization.Id, accepted.OrganizationId);
        Assert.Equal("org_x", accepted.ClerkOrgId);

        var membership = await _context.OrganizationMemberships
            .SingleAsync(m => m.OrganizationId == organization.Id && m.UserId == staff.Id);
        Assert.Equal(Roles.BoutiqueStaff, membership.BoutiqueRole);
        Assert.Equal(MembershipStatus.Active, membership.Status);

        // Second acceptance is rejected (one-time).
        await Assert.ThrowsAsync<InvitationNotAcceptableException>(() =>
            _sut.AcceptInvitationAsync(invite.Code, staff.Id, staff.Email));
    }

    [Fact]
    public async Task AcceptInvitationAsync_Expired_Throws()
    {
        var owner = await AddUserAsync("clerk_owner5", "owner5@aveline.lk");
        var staff = await AddUserAsync("clerk_staff5", "staff5@aveline.lk");
        var organization = await _sut.CreateOrganizationAsync(owner.Id, "Boutique", null);

        var invite = await _sut.InviteMemberAsync(
            organization.Id, owner.Id,
            new InviteMemberRequest(Roles.BoutiqueStaff, RecipientEmail: staff.Email, ValidFor: TimeSpan.FromDays(-1)));

        var ex = await Assert.ThrowsAsync<InvitationNotAcceptableException>(() =>
            _sut.AcceptInvitationAsync(invite.Code, staff.Id, staff.Email));
        Assert.Contains("expired", ex.Message);
    }

    [Fact]
    public async Task AcceptInvitationAsync_Revoked_Throws()
    {
        var owner = await AddUserAsync("clerk_owner6", "owner6@aveline.lk");
        var staff = await AddUserAsync("clerk_staff6", "staff6@aveline.lk");
        var organization = await _sut.CreateOrganizationAsync(owner.Id, "Boutique", null);

        var invite = await _sut.InviteMemberAsync(
            organization.Id, owner.Id,
            new InviteMemberRequest(Roles.BoutiqueStaff, RecipientEmail: staff.Email));

        var invitationRepo = new InvitationRepository(_context);
        var persisted = await invitationRepo.GetByTokenHashAsync(invite.Invitation.TokenHash);
        persisted!.RevokedAt = DateTime.UtcNow;
        await invitationRepo.UpdateAsync(persisted);

        await Assert.ThrowsAsync<InvitationNotAcceptableException>(() =>
            _sut.AcceptInvitationAsync(invite.Code, staff.Id, staff.Email));
    }

    [Fact]
    public async Task AcceptInvitationAsync_UnknownCode_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<InvitationNotFoundException>(() =>
            _sut.AcceptInvitationAsync("DOESNOTEXIST", Guid.NewGuid(), "x@aveline.lk"));
    }

    [Theory]
    [InlineData("other@aveline.lk")] // different email
    [InlineData("STAFF@AVELINE.LK")] // case-insensitive match should succeed
    public async Task AcceptInvitationAsync_MatchesRecipientEmailCaseInsensitively(string acceptingEmail)
    {
        var owner = await AddUserAsync("clerk_owner7", "owner7@aveline.lk");
        var staff = await AddUserAsync("clerk_staff7", "staff7@aveline.lk");
        var organization = await _sut.CreateOrganizationAsync(owner.Id, "Boutique", null);

        var invite = await _sut.InviteMemberAsync(
            organization.Id, owner.Id,
            new InviteMemberRequest(Roles.BoutiqueStaff, RecipientEmail: "staff@aveline.lk"));

        if (acceptingEmail == "other@aveline.lk")
        {
            await Assert.ThrowsAsync<InvitationRecipientMismatchException>(() =>
                _sut.AcceptInvitationAsync(invite.Code, staff.Id, acceptingEmail));
        }
        else
        {
            var accepted = await _sut.AcceptInvitationAsync(invite.Code, staff.Id, acceptingEmail);
            Assert.Equal(organization.Id, accepted.OrganizationId);
        }
    }

    [Fact]
    public async Task AcceptInvitationAsync_ScopedToRecipientUser_WhenSet()
    {
        var owner = await AddUserAsync("clerk_owner8", "owner8@aveline.lk");
        var intended = await AddUserAsync("clerk_intended8", "intended@aveline.lk");
        var intruder = await AddUserAsync("clerk_intruder8", "intruder@aveline.lk");
        var organization = await _sut.CreateOrganizationAsync(owner.Id, "Boutique", null);

        var invite = await _sut.InviteMemberAsync(
            organization.Id, owner.Id,
            new InviteMemberRequest(Roles.BoutiqueManager, RecipientUserId: intended.Id));

        await Assert.ThrowsAsync<InvitationRecipientMismatchException>(() =>
            _sut.AcceptInvitationAsync(invite.Code, intruder.Id, intruder.Email));

        var accepted = await _sut.AcceptInvitationAsync(invite.Code, intended.Id, intended.Email);
        Assert.Equal(Roles.BoutiqueManager, accepted.BoutiqueRole);
    }

    [Fact]
    public async Task AcceptInvitationAsync_NewStaffUser_WithoutExistingMembership_IsSupported()
    {
        // A freshly registered staff user has no OrganizationMembership and no
        // org_id; accepting an email invite must create their first membership.
        var owner = await AddUserAsync("clerk_owner9", "owner9@aveline.lk");
        var newStaff = await AddUserAsync("clerk_new9", "new.staff@aveline.lk");
        var organization = await _sut.CreateOrganizationAsync(owner.Id, "Boutique", null);

        Assert.Empty(await _sut.GetUserMembershipsAsync(newStaff.Id));

        var invite = await _sut.InviteMemberAsync(
            organization.Id, owner.Id,
            new InviteMemberRequest(Roles.BoutiqueSupervisor, RecipientEmail: newStaff.Email));

        var accepted = await _sut.AcceptInvitationAsync(invite.Code, newStaff.Id, newStaff.Email);

        var memberships = await _sut.GetUserMembershipsAsync(newStaff.Id);
        var membership = Assert.Single(memberships);
        Assert.Equal(Roles.BoutiqueSupervisor, membership.BoutiqueRole);
        Assert.Equal(organization.Id, accepted.OrganizationId);
    }

    [Fact]
    public async Task AcceptInvitationAsync_AlreadyMember_Throws()
    {
        var owner = await AddUserAsync("clerk_owner10", "owner10@aveline.lk");
        var staff = await AddUserAsync("clerk_staff10", "staff10@aveline.lk");
        var organization = await _sut.CreateOrganizationAsync(owner.Id, "Boutique", null);

        var invite = await _sut.InviteMemberAsync(
            organization.Id, owner.Id,
            new InviteMemberRequest(Roles.BoutiqueStaff, RecipientEmail: staff.Email));

        // Force an existing membership (e.g. provisioned separately), then accepting must fail.
        _context.OrganizationMemberships.Add(new OrganizationMembership
        {
            OrganizationId = organization.Id,
            UserId = staff.Id,
            BoutiqueRole = Roles.BoutiqueStaff,
            Status = MembershipStatus.Active,
        });
        await _context.SaveChangesAsync();

        await Assert.ThrowsAsync<MembershipAlreadyExistsException>(() =>
            _sut.AcceptInvitationAsync(invite.Code, staff.Id, staff.Email));
    }
}
