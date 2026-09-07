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

public class OrganizationServiceTests
{
    private readonly AppDbContext _context;
    private readonly OrganizationService _sut;
    private readonly IUserCacheService _cacheService;
    private readonly FakeInvitationCodeStore _codeStore;

    public OrganizationServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"OrganizationServiceTests_{Guid.NewGuid()}")
            .Options;

        _context = new AppDbContext(options);
        var memCacheOptions = Options.Create(new MemoryDistributedCacheOptions());
        var distCache = new MemoryDistributedCache(memCacheOptions);
        _cacheService = new UserCacheService(distCache, NullLogger<UserCacheService>.Instance);
        _codeStore = new FakeInvitationCodeStore();
        _sut = new OrganizationService(
            new OrganizationRepository(_context),
            new InvitationRepository(_context),
            _codeStore,
            new UserRepository(_context),
            _cacheService,
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

        Assert.True(await _sut.HasActiveMembershipAsync(owner.Id));
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

    [Fact]
    public async Task AcceptInvitationAsync_InvalidatesStaleCachedUserState()
    {
        var owner = await AddUserAsync("clerk_owner_inv", "owner.inv@aveline.lk");
        var staff = await AddUserAsync("clerk_staff_inv", "staff.inv@aveline.lk");
        var organization = await _sut.CreateOrganizationAsync(owner.Id, "Cache Invalidation", null);

        var invite = await _sut.InviteMemberAsync(
            organization.Id, owner.Id,
            new InviteMemberRequest(Roles.BoutiqueStaff, RecipientEmail: staff.Email));

        // Simulate a stale 24h snapshot: pending even though the membership is about to exist.
        await _cacheService.SetUserAsync("clerk_staff_inv", new UserOnboardingCacheItem
        {
            Id = staff.Id,
            ClerkId = staff.ClerkId,
            HasCompletedOnboarding = true,
            Email = staff.Email,
            FirstName = "Test",
            LastName = "User",
            UserRole = Roles.Staff,
            OrganizationRole = string.Empty,
        });

        await _sut.AcceptInvitationAsync(invite.Code, staff.Id, staff.Email);

        // The stale snapshot must be invalidated so the next request re-syncs to Active.
        Assert.Null(await _cacheService.GetUserAsync("clerk_staff_inv"));
    }

    [Fact]
    public async Task SuspendMembershipAsync_SuspendsAndInvalidatesCache()
    {
        var owner = await AddUserAsync("clerk_owner_sus", "owner.sus@aveline.lk");
        var staff = await AddUserAsync("clerk_staff_sus", "staff.sus@aveline.lk");
        var organization = await _sut.CreateOrganizationAsync(owner.Id, "Suspend Boutique", null);

        var invite = await _sut.InviteMemberAsync(
            organization.Id, owner.Id,
            new InviteMemberRequest(Roles.BoutiqueStaff, RecipientEmail: staff.Email));
        await _sut.AcceptInvitationAsync(invite.Code, staff.Id, staff.Email);

        await _cacheService.SetUserAsync("clerk_staff_sus", new UserOnboardingCacheItem
        {
            Id = staff.Id,
            ClerkId = staff.ClerkId,
            HasCompletedOnboarding = true,
            Email = staff.Email,
            FirstName = "Test",
            LastName = "User",
            UserRole = Roles.Staff,
            OrganizationRole = string.Empty,
        });

        var suspended = await _sut.SetMembershipStatusAsync(
            organization.Id, staff.Id, MembershipStatus.Suspended);

        Assert.Equal(MembershipStatus.Suspended, suspended.Status);
        Assert.Null(await _cacheService.GetUserAsync("clerk_staff_sus"));
    }

    [Fact]
    public async Task RemoveMembershipAsync_RemovesAndInvalidatesCache()
    {
        var owner = await AddUserAsync("clerk_owner_rem", "owner.rem@aveline.lk");
        var staff = await AddUserAsync("clerk_staff_rem", "staff.rem@aveline.lk");
        var organization = await _sut.CreateOrganizationAsync(owner.Id, "Remove Boutique", null);

        var invite = await _sut.InviteMemberAsync(
            organization.Id, owner.Id,
            new InviteMemberRequest(Roles.BoutiqueStaff, RecipientEmail: staff.Email));
        await _sut.AcceptInvitationAsync(invite.Code, staff.Id, staff.Email);

        await _cacheService.SetUserAsync("clerk_staff_rem", new UserOnboardingCacheItem
        {
            Id = staff.Id,
            ClerkId = staff.ClerkId,
            HasCompletedOnboarding = true,
            Email = staff.Email,
            FirstName = "Test",
            LastName = "User",
            UserRole = Roles.Staff,
            OrganizationRole = string.Empty,
        });

        await _sut.RemoveMembershipAsync(organization.Id, staff.Id);

        Assert.Empty(await _sut.GetUserMembershipsAsync(staff.Id));
        Assert.Null(await _cacheService.GetUserAsync("clerk_staff_rem"));
    }

    [Fact]
    public async Task OwnerMembership_CannotBeSuspendedOrRemoved()
    {
        var owner = await AddUserAsync("clerk_owner_guard", "owner.guard@aveline.lk");
        var organization = await _sut.CreateOrganizationAsync(owner.Id, "Owner Guard", null);

        await Assert.ThrowsAsync<CannotManageOwnerMembershipException>(() =>
            _sut.SetMembershipStatusAsync(organization.Id, owner.Id, MembershipStatus.Suspended));
        await Assert.ThrowsAsync<CannotManageOwnerMembershipException>(() =>
            _sut.RemoveMembershipAsync(organization.Id, owner.Id));
    }

    [Fact]
    public async Task GetOrganizationProfileBySlugAsync_Member_ReturnsProfileAndMembership()
    {
        var owner = await AddUserAsync("clerk_profile_owner", "profile.owner@aveline.lk");
        var organization = await _sut.CreateOrganizationAsync(owner.Id, "Profile Boutique", null);

        var result = await _sut.GetOrganizationProfileBySlugAsync(organization.Slug, owner.Id);

        Assert.NotNull(result);
        Assert.Equal(organization.Name, result!.Organization.Name);
        Assert.Equal(organization.Slug, result.Organization.Slug);
        Assert.NotNull(result.Membership);
        Assert.Equal(Roles.BoutiqueOwner, result.Membership!.BoutiqueRole);
    }

    [Fact]
    public async Task GetOrganizationProfileBySlugAsync_NonMember_ReturnsProfileWithNullMembership()
    {
        var owner = await AddUserAsync("clerk_profile_owner2", "profile.owner2@aveline.lk");
        var outsider = await AddUserAsync("clerk_profile_outsider", "profile.outsider@aveline.lk");
        var organization = await _sut.CreateOrganizationAsync(owner.Id, "Profile Boutique 2", null);

        var result = await _sut.GetOrganizationProfileBySlugAsync(organization.Slug, outsider.Id);

        Assert.NotNull(result);
        Assert.Equal(organization.Slug, result!.Organization.Slug);
        Assert.Null(result.Membership);
    }

    [Fact]
    public async Task GetOrganizationProfileBySlugAsync_UnknownSlug_ReturnsNull()
    {
        var owner = await AddUserAsync("clerk_profile_owner3", "profile.owner3@aveline.lk");
        await _sut.CreateOrganizationAsync(owner.Id, "Profile Boutique 3", null);

        var result = await _sut.GetOrganizationProfileBySlugAsync("no-such-slug", owner.Id);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetOrganizationProfileAsync_ReturnsProfile_OrNullWhenMissing()
    {
        var owner = await AddUserAsync("clerk_profile_owner4", "profile.owner4@aveline.lk");
        var organization = await _sut.CreateOrganizationAsync(owner.Id, "Profile Boutique 4", null);

        var found = await _sut.GetOrganizationProfileAsync(organization.Id);
        Assert.NotNull(found);
        Assert.Equal(organization.Name, found!.Name);

        var missing = await _sut.GetOrganizationProfileAsync(Guid.NewGuid());
        Assert.Null(missing);
    }

    [Fact]
    public async Task CreateOrganizationAsync_NormalizesProvidedSlug()
    {
        var owner = await AddUserAsync("clerk_slug_norm_owner", "slug.norm.owner@aveline.lk");

        // Mixed case + illegal characters are normalized to a lowercase, hyphenated slug.
        var organization = await _sut.CreateOrganizationAsync(
            owner.Id, "Aveline Boutique", null, "  My Shop!! BLOOM  ");

        Assert.Equal("my-shop-bloom", organization.Slug);

        // The normalized slug is the one matched by case-sensitive by-slug lookups.
        var bySlug = await _sut.GetOrganizationProfileBySlugAsync("my-shop-bloom", owner.Id);
        Assert.NotNull(bySlug);
    }

    [Fact]
    public async Task CreateOrganizationAsync_SlugDerivedFromName_IsNormalized()
    {
        var owner = await AddUserAsync("clerk_slug_name_owner", "slug.name.owner@aveline.lk");

        var organization = await _sut.CreateOrganizationAsync(owner.Id, "House of Fashions — Colombo", null);

        Assert.Equal("house-of-fashions-colombo", organization.Slug);
    }

    [Fact]
    public async Task CreateOrganizationAsync_SymbolOnlySlug_ThrowsArgumentException()
    {
        var owner = await AddUserAsync("clerk_slug_symbol_owner", "slug.symbol.owner@aveline.lk");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.CreateOrganizationAsync(owner.Id, "Aveline", null, "!!!"));
    }
}
