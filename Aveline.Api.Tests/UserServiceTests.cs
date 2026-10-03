using System.Security.Claims;
using Aveline.Api.Authorization;
using Aveline.Api.Infrastructure.Caching;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Organizations.Repositories;
using Aveline.Api.Modules.Organizations.Services;
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
    private readonly IOrganizationService _organizationService;
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

        var orgRepository = new OrganizationRepository(_dbContext);
        var invitationRepository = new InvitationRepository(_dbContext);
        _organizationService = new OrganizationService(
            orgRepository, invitationRepository, new FakeInvitationCodeStore(), _userRepository, _cacheService,
            NullLogger<OrganizationService>.Instance);

        _sut = new UserService(_userRepository, _cacheService, _organizationService, NullLogger<UserService>.Instance);
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

    [Fact]
    public async Task GetByClerkIdAsync_WhenProfileInCache_ReturnsCachedProfileWithoutDbCall()
    {
        var cachedProfile = new UserDto
        {
            Id = Guid.NewGuid(),
            ClerkId = "clerk_cached_profile_only",
            Email = "cached_profile@aveline.lk",
            FirstName = "Cached",
            LastName = "Profile",
            DisplayName = "Cached Profile",
            UserRole = "owner",
            OrganizationRole = "org:admin",
            HasCompletedOnboarding = true
        };

        await _cacheService.SetUserProfileAsync("clerk_cached_profile_only", cachedProfile);

        // Intentionally do NOT put user in _userRepository / DB.
        var result = await _sut.GetByClerkIdAsync("clerk_cached_profile_only");

        Assert.NotNull(result);
        Assert.Equal(cachedProfile.Id, result.Id);
        Assert.Equal("Cached Profile", result.DisplayName);
        Assert.Equal("cached_profile@aveline.lk", result.Email);
    }

    [Fact]
    public async Task GetByClerkIdAsync_WhenNotInCache_QueriesDbAndPopulatesProfileCache()
    {
        var dbUser = new User
        {
            Id = Guid.NewGuid(),
            ClerkId = "clerk_miss_user",
            Email = "miss@aveline.lk",
            FirstName = "Miss",
            LastName = "Hit",
            Username = "miss_hit",
            DisplayName = "Miss Hit",
            UserRole = "associate",
            OrganizationRole = "org:member",
            HasCompletedOnboarding = true
        };
        await _userRepository.CreateAsync(dbUser);

        // Before call, cache must be empty
        var cacheBefore = await _cacheService.GetUserProfileAsync("clerk_miss_user");
        Assert.Null(cacheBefore);

        var result = await _sut.GetByClerkIdAsync("clerk_miss_user");

        Assert.NotNull(result);
        Assert.Equal("clerk_miss_user", result.ClerkId);
        Assert.Equal("Miss Hit", result.DisplayName);

        // Verify cache is now populated
        var cacheAfter = await _cacheService.GetUserProfileAsync("clerk_miss_user");
        Assert.NotNull(cacheAfter);
        Assert.Equal(dbUser.Id, cacheAfter.Id);
        Assert.Equal("Miss Hit", cacheAfter.DisplayName);
    }

    [Fact]
    public async Task CompleteOnboardingAsync_UpdatesBothOnboardingAndProfileCache()
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            ClerkId = "clerk_complete_dual",
            Email = "dual@aveline.lk",
            FirstName = "Dual",
            LastName = "Cache",
            Username = "dual_cache",
            UserRole = "manager",
            HasCompletedOnboarding = false
        };
        await _userRepository.CreateAsync(user);

        var request = new CompleteOnboardingRequest
        {
            DisplayName = "Dual Cache Completed",
            PhoneNumber = "+94771122334",
            Address = "Colombo 03",
            ContactPreference = ContactPreferences.WhatsApp,
            PushNotificationsEnabled = true
        };

        await _sut.CompleteOnboardingAsync("clerk_complete_dual", request);

        // Verify Onboarding Cache
        var onboardingCache = await _cacheService.GetUserAsync("clerk_complete_dual");
        Assert.NotNull(onboardingCache);
        Assert.True(onboardingCache.HasCompletedOnboarding);
        Assert.Equal("Dual Cache Completed", onboardingCache.DisplayName);

        // Verify Profile Cache
        var profileCache = await _cacheService.GetUserProfileAsync("clerk_complete_dual");
        Assert.NotNull(profileCache);
        Assert.True(profileCache.HasCompletedOnboarding);
        Assert.Equal("Dual Cache Completed", profileCache.DisplayName);
        Assert.Equal("+94771122334", profileCache.PhoneNumber);
        Assert.Equal(ContactPreferences.WhatsApp, profileCache.ContactPreference);
    }

    [Fact]
    public async Task CompleteOnboardingAsync_WhenMembershipExists_ButNoOrgClaims_ActivatesAccount()
    {
        var user = new User
        {
            Id = Guid.CreateVersion7(),
            ClerkId = "clerk_member_profile_last",
            Email = "member.last@aveline.lk",
            FirstName = "Member",
            LastName = "ProfileLast",
            Username = "member_profile_last",
            UserRole = Roles.Staff,
            HasCompletedOnboarding = false,
        };
        await _userRepository.CreateAsync(user);

        // Staff accepted an invite first: canonical membership exists, legacy org fields are empty.
        var organization = await _organizationService.CreateOrganizationAsync(
            Guid.CreateVersion7(), "Membership First Boutique", null, cancellationToken: CancellationToken.None);
        var invitation = await _organizationService.InviteMemberAsync(
            organization.Id, organization.OwnerUserId,
            new InviteMemberRequest(Roles.BoutiqueManager, RecipientUserId: user.Id),
            CancellationToken.None);
        await _organizationService.AcceptInvitationAsync(
            invitation.Code, user.Id, user.Email, CancellationToken.None);

        var request = new CompleteOnboardingRequest
        {
            DisplayName = "Member ProfileLast",
            PhoneNumber = "+94770001122",
            Address = "Colombo 05",
            ContactPreference = ContactPreferences.Email,
            PushNotificationsEnabled = true,
        };

        var dto = await _sut.CompleteOnboardingAsync("clerk_member_profile_last", request);

        Assert.True(dto.HasCompletedOnboarding);
        Assert.Equal(AccountState.Active, dto.AccountState);

        var dbUser = await _userRepository.GetByClerkIdAsync("clerk_member_profile_last");
        Assert.NotNull(dbUser);
        Assert.Equal(AccountState.Active, dbUser.AccountState);
    }

    [Fact]
    public async Task GetOrSynchronizeUserAsync_ExistingPendingUser_WhenMembershipAdded_PromotesToActive()
    {
        var user = new User
        {
            Id = Guid.CreateVersion7(),
            ClerkId = "clerk_pending_membership",
            Email = "pending.member@aveline.lk",
            FirstName = "Pending",
            LastName = "Member",
            Username = "pending_member",
            UserRole = Roles.Staff,
            HasCompletedOnboarding = true,
            AccountState = AccountState.OnboardingPending,
        };
        await _userRepository.CreateAsync(user);

        var organization = await _organizationService.CreateOrganizationAsync(
            Guid.CreateVersion7(), "Promotion Boutique", null, cancellationToken: CancellationToken.None);

        // Canonical record: an active membership is created (invite accepted out-of-band).
        var repo = new OrganizationRepository(_dbContext);
        await repo.AddMembershipAsync(new OrganizationMembership
        {
            OrganizationId = organization.Id,
            UserId = user.Id,
            BoutiqueRole = Roles.BoutiqueStaff,
            Status = MembershipStatus.Active,
        }, CancellationToken.None);

        var claimsPrincipal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("sub", "clerk_pending_membership"),
            new Claim("email", "pending.member@aveline.lk"),
        }, "TestAuth"));

        var result = await _sut.GetOrSynchronizeUserAsync("clerk_pending_membership", claimsPrincipal);

        Assert.Equal(AccountState.Active, result.AccountState);

        var dbUser = await _userRepository.GetByClerkIdAsync("clerk_pending_membership");
        Assert.NotNull(dbUser);
        Assert.Equal(AccountState.Active, dbUser.AccountState);
    }

    [Fact]
    public async Task GetOrSynchronizeUserAsync_CachedUser_WhenRefreshedClaimsCarryOrgContext_SyncsReadModel()
    {
        var user = new User
        {
            Id = Guid.CreateVersion7(),
            ClerkId = "clerk_org_switch",
            Email = "org.switch@aveline.lk",
            FirstName = "Org",
            LastName = "Switch",
            Username = "org_switch",
            UserRole = Roles.Staff,
            OrganizationRole = string.Empty,
            HasCompletedOnboarding = true,
            AccountState = AccountState.OnboardingPending,
        };
        await _userRepository.CreateAsync(user);

        // Warm cache holds the pre-refresh snapshot (no org context yet).
        var cached = new UserOnboardingCacheItem
        {
            Id = user.Id,
            ClerkId = user.ClerkId,
            HasCompletedOnboarding = true,
            Email = user.Email,
            FirstName = user.FirstName,
            LastName = user.LastName,
            UserRole = Roles.Staff,
            OrganizationRole = string.Empty,
            AccountState = AccountState.OnboardingPending,
        };
        await _cacheService.SetUserAsync(user.ClerkId, cached);

        var claimsPrincipal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("sub", "clerk_org_switch"),
            new Claim("email", "org.switch@aveline.lk"),
            new Claim("user_role", Roles.Staff),
            new Claim("org_role", Roles.BoutiqueManager),
            new Claim("org_id", "org_switch_boutique"),
        }, "TestAuth"));

        var result = await _sut.GetOrSynchronizeUserAsync("clerk_org_switch", claimsPrincipal);

        Assert.Equal(Roles.BoutiqueManager, result.OrganizationRole);
        Assert.Equal(AccountState.Active, result.AccountState);

        var dbUser = await _userRepository.GetByClerkIdAsync("clerk_org_switch");
        Assert.NotNull(dbUser);
        Assert.Equal(Roles.BoutiqueManager, dbUser.OrganizationRole);
        Assert.Equal("org_switch_boutique", dbUser.OrganizationId);
        Assert.Equal(AccountState.Active, dbUser.AccountState);
    }

    // ── Membership-derived organization context ───────────────────────────────────────────
    //
    // A live staff device logged `[user] fetchUser OK: ... orgRole=` on every poll while the API
    // returned no 403 at all: the mobile derives permissions from `[userRole, organizationRole]`,
    // so an empty role renders an empty home and locked-out sections. The row was empty because
    // `User.OrganizationRole` mirrors a Clerk `org_role` claim this deployment can never mint —
    // only the owner onboarding wizard ever wrote it, as a side effect, for the owner alone.

    [Fact]
    public async Task GetByClerkIdAsync_ProjectsTheMembershipRole_WhenTheRowCarriesNone()
    {
        var (user, org) = await SeedMemberAsync("clerk_staff_projection", Roles.BoutiqueStaff);

        var dto = await CreateSutWithOrganizationRepository().GetByClerkIdAsync(user.ClerkId);

        Assert.NotNull(dto);
        Assert.Equal(Roles.BoutiqueStaff, dto!.OrganizationRole);
        Assert.Equal(org.Id.ToString(), dto.OrganizationId);
    }

    [Fact]
    public async Task GetByClerkIdAsync_ReDerivesPastACachedEmptyRole()
    {
        // An entry cached before members were included would otherwise serve an empty role for the
        // entry's 24-hour life, so deploying the fix would not repair a warm instance.
        var (user, _) = await SeedMemberAsync("clerk_staff_warm_cache", Roles.BoutiqueStaff);
        await _cacheService.SetUserProfileAsync(user.ClerkId, UserDto.FromEntity(user));

        var dto = await CreateSutWithOrganizationRepository().GetByClerkIdAsync(user.ClerkId);

        Assert.NotNull(dto);
        Assert.Equal(Roles.BoutiqueStaff, dto!.OrganizationRole);
    }

    [Fact]
    public async Task GetByClerkIdAsync_LeavesARowThatAlreadyCarriesARoleAlone()
    {
        // The claim stays authoritative where one exists, so a future Clerk organization is not
        // overwritten by whichever membership happens to be first.
        var (user, _) = await SeedMemberAsync("clerk_claim_wins", Roles.BoutiqueOwner);
        user.OrganizationRole = "org:from-claim";
        user.OrganizationId = "org_from_claim";
        await _dbContext.SaveChangesAsync();

        var dto = await CreateSutWithOrganizationRepository().GetByClerkIdAsync(user.ClerkId);

        Assert.NotNull(dto);
        Assert.Equal("org:from-claim", dto!.OrganizationRole);
        Assert.Equal("org_from_claim", dto.OrganizationId);
    }

    [Fact]
    public async Task GetByClerkIdAsync_LeavesARolelessNonMemberEmpty()
    {
        var user = NewMemberRow("clerk_no_membership");
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        var dto = await CreateSutWithOrganizationRepository().GetByClerkIdAsync(user.ClerkId);

        Assert.NotNull(dto);
        Assert.Equal(string.Empty, dto!.OrganizationRole);
    }

    [Fact]
    public async Task GetOrSynchronizeUserAsync_ProjectsTheMembershipRoleOntoTheCacheItem()
    {
        // `/auth/claims` reads `Account.OrganizationRole` from this item, so the middleware path
        // has to agree with `/users/me`.
        var (user, _) = await SeedMemberAsync("clerk_staff_cache_item", Roles.BoutiqueStaff);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("sub", user.ClerkId),
            new Claim("email", user.Email),
        }, "TestAuth"));

        var item = await CreateSutWithOrganizationRepository()
            .GetOrSynchronizeUserAsync(user.ClerkId, principal);

        Assert.Equal(Roles.BoutiqueStaff, item.OrganizationRole);
    }

    /// <summary>
    /// A service with the organization repository wired in, which is what enables the membership
    /// projection. The shared <c>_sut</c> leaves that optional dependency null, so the claim-driven
    /// tests above stay isolated to the claim path.
    /// </summary>
    private UserService CreateSutWithOrganizationRepository()
    {
        var orgRepository = new OrganizationRepository(_dbContext);
        var invitationRepository = new InvitationRepository(_dbContext);
        var organizationService = new OrganizationService(
            orgRepository, invitationRepository, new FakeInvitationCodeStore(), _userRepository, _cacheService,
            NullLogger<OrganizationService>.Instance);

        return new UserService(
            _userRepository, _cacheService, organizationService, NullLogger<UserService>.Instance,
            orgRepository);
    }

    private static User NewMemberRow(string clerkId) => new()
    {
        Id = Guid.CreateVersion7(),
        ClerkId = clerkId,
        Email = $"{clerkId}@aveline.lk",
        FirstName = "Invited",
        LastName = "Member",
        Username = clerkId,
        UserRole = Roles.Staff,
        OrganizationRole = string.Empty,
        OrganizationId = string.Empty,
        HasCompletedOnboarding = false,
        AccountState = AccountState.OnboardingPending,
    };

    private async Task<(User User, Organization Organization)> SeedMemberAsync(
        string clerkId,
        string boutiqueRole)
    {
        var user = NewMemberRow(clerkId);
        var organization = new Organization
        {
            Id = Guid.CreateVersion7(),
            Name = $"Boutique {clerkId}",
            Slug = $"boutique-{clerkId}",
            OwnerUserId = Guid.CreateVersion7(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        _dbContext.Users.Add(user);
        _dbContext.Organizations.Add(organization);
        _dbContext.OrganizationMemberships.Add(new OrganizationMembership
        {
            OrganizationId = organization.Id,
            UserId = user.Id,
            BoutiqueRole = boutiqueRole,
            Status = MembershipStatus.Active,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await _dbContext.SaveChangesAsync();

        return (user, organization);
    }
}
