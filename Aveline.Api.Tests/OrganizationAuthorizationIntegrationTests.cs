using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Aveline.Api.Authorization;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Aveline.Api.Tests;

/// <summary>
/// Issue #52 — server-side authorization enforcement: fallback authentication,
/// suspended denial, and canonical organization scope (cross-org denial).
/// </summary>
public class OrganizationAuthorizationIntegrationTests : IAsyncLifetime
{
    private RsaSecurityKey _signingKey = null!;
    private StubAuthServer _authServer = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _signingKey = new RsaSecurityKey(RSA.Create(2048)) { KeyId = "test-kid" };
        _authServer = new StubAuthServer(_signingKey);
        await _authServer.StartAsync();

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Clerk:Authority", _authServer.BaseUrl);
                builder.UseSetting("Clerk:RequireHttpsMetadata", "false");
            });
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _authServer.DisposeAsync();
    }

    private string CreateToken(string userId, string? userRole = null)
    {
        var claims = new List<Claim> { new("sub", userId) };
        if (userRole != null) claims.Add(new Claim("user_role", userRole));

        var handler = new JsonWebTokenHandler();
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _authServer.BaseUrl,
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(_signingKey, SecurityAlgorithms.RsaSha256),
        };

        return handler.CreateToken(descriptor);
    }

    private HttpRequestMessage Authorized(HttpMethod method, string path, string token) =>
        new(method, path)
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
        };

    [Fact]
    public async Task Owner_SuspendsMember_ScopedAccessDenied_ThenActivateRestores()
    {
        var (owner, member, org) = await SeedBoutiqueAsync("mgmt_suspend");
        var memberId = member.Id;
        var orgId = org.Id;
        var ownerToken = CreateToken(owner.ClerkId, "staff");
        var memberToken = CreateToken(member.ClerkId, "staff");

        var before = await _client.SendAsync(
            Authorized(HttpMethod.Get, $"/api/v1/policies/orgs/{orgId}/catalog", memberToken));
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        var suspend = await _client.SendAsync(
            Authorized(HttpMethod.Post, $"/api/v1/orgs/{orgId}/members/{memberId}/suspend", ownerToken));
        Assert.Equal(HttpStatusCode.OK, suspend.StatusCode);
        var suspendBody = JsonDocument.Parse(await suspend.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("Suspended", suspendBody.GetProperty("status").GetString());

        // Suspended membership denies even a valid JWT on the org-scoped resource.
        var denied = await _client.SendAsync(
            Authorized(HttpMethod.Get, $"/api/v1/policies/orgs/{orgId}/catalog", memberToken));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        var activate = await _client.SendAsync(
            Authorized(HttpMethod.Post, $"/api/v1/orgs/{orgId}/members/{memberId}/activate", ownerToken));
        Assert.Equal(HttpStatusCode.OK, activate.StatusCode);

        var restored = await _client.SendAsync(
            Authorized(HttpMethod.Get, $"/api/v1/policies/orgs/{orgId}/catalog", memberToken));
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
    }

    [Fact]
    public async Task StaffMember_CannotManageMembership_InOwnOrg()
    {
        var (owner, member, org) = await SeedBoutiqueAsync("mgmt_staff_denied");
        var staffToken = CreateToken(member.ClerkId, "staff");
        var other = Guid.CreateVersion7();

        var response = await _client.SendAsync(
            Authorized(
                HttpMethod.Post,
                $"/api/v1/orgs/{org.Id}/members/{other}/suspend",
                staffToken));

        // Valid member, but lacks the settings:manage grant -> forbidden.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        _ = owner;
    }

    [Fact]
    public async Task OwnerOfOtherOrg_CannotManageForeignMembership()
    {
        var (_, member, orgA) = await SeedBoutiqueAsync("mgmt_cross_a");
        var (foreignOwner, _, _) = await SeedBoutiqueAsync("mgmt_cross_b");
        var foreignToken = CreateToken(foreignOwner.ClerkId, "staff");

        var response = await _client.SendAsync(
            Authorized(
                HttpMethod.Post,
                $"/api/v1/orgs/{orgA.Id}/members/{member.Id}/suspend",
                foreignToken));

        // Foreign owner is not a member of org A -> denied by org scope.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Owner_RemovesMember_ScopedAccessDenied()
    {
        var (owner, member, org) = await SeedBoutiqueAsync("mgmt_remove");
        var ownerToken = CreateToken(owner.ClerkId, "staff");
        var memberToken = CreateToken(member.ClerkId, "staff");

        var remove = await _client.SendAsync(
            Authorized(HttpMethod.Delete, $"/api/v1/orgs/{org.Id}/members/{member.Id}", ownerToken));
        Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);

        var denied = await _client.SendAsync(
            Authorized(HttpMethod.Get, $"/api/v1/policies/orgs/{org.Id}/catalog", memberToken));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [Fact]
    public async Task OwnerCannotManageOwnOwnerMembership()
    {
        var (owner, _, org) = await SeedBoutiqueAsync("mgmt_owner_guard");
        var ownerToken = CreateToken(owner.ClerkId, "staff");

        var response = await _client.SendAsync(
            Authorized(
                HttpMethod.Post,
                $"/api/v1/orgs/{org.Id}/members/{owner.Id}/suspend",
                ownerToken));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("owner", await response.Content.ReadAsStringAsync());
    }

    private async Task<(User owner, User member, Organization org)> SeedBoutiqueAsync(string slugSuffix)
    {
        await using var context = CreateSeedContext();
        var owner = new User
        {
            Id = Guid.CreateVersion7(),
            ClerkId = $"owner_{slugSuffix}",
            Email = $"{slugSuffix}.owner@aveline.lk",
            FirstName = "Owner",
            LastName = slugSuffix,
            Username = $"owner_{slugSuffix}",
            UserRole = Roles.Staff,
            OrganizationRole = string.Empty,
            HasCompletedOnboarding = true,
            AccountState = AccountState.Active,
        };
        var member = new User
        {
            Id = Guid.CreateVersion7(),
            ClerkId = $"member_{slugSuffix}",
            Email = $"{slugSuffix}.member@aveline.lk",
            FirstName = "Member",
            LastName = slugSuffix,
            Username = $"member_{slugSuffix}",
            UserRole = Roles.Staff,
            OrganizationRole = string.Empty,
            HasCompletedOnboarding = true,
            AccountState = AccountState.Active,
        };
        context.Users.AddRange(owner, member);
        await context.SaveChangesAsync();

        var org = new Organization
        {
            Name = $"Org {slugSuffix}",
            Slug = $"org-{slugSuffix}",
            OwnerUserId = owner.Id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        context.Organizations.Add(org);
        await context.SaveChangesAsync();

        context.OrganizationMemberships.AddRange(
            new OrganizationMembership
            {
                OrganizationId = org.Id,
                UserId = owner.Id,
                BoutiqueRole = Roles.BoutiqueOwner,
                Status = MembershipStatus.Active,
            },
            new OrganizationMembership
            {
                OrganizationId = org.Id,
                UserId = member.Id,
                BoutiqueRole = Roles.BoutiqueStaff,
                Status = MembershipStatus.Active,
            });
        await context.SaveChangesAsync();

        return (owner, member, org);
    }

    private static AppDbContext CreateSeedContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "AvelineInMemoryDb")
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task UnannotatedEndpoint_WithoutToken_Returns401()
    {
        var response = await _client.GetAsync("/api/v1/policies/fallback/authed-by-default");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UnannotatedEndpoint_WithToken_Returns200()
    {
        await using var context = CreateSeedContext();
        context.Users.Add(new User
        {
            Id = Guid.CreateVersion7(),
            ClerkId = "user_fallback_ok",
            Email = "fallback.ok@aveline.lk",
            FirstName = "Fallback",
            LastName = "Ok",
            Username = "user_fallback_ok",
            UserRole = Roles.Staff,
            OrganizationRole = string.Empty,
            HasCompletedOnboarding = true,
            AccountState = AccountState.Active,
        });
        await context.SaveChangesAsync();

        var token = CreateToken("user_fallback_ok", "staff");
        var response = await _client.SendAsync(
            Authorized(HttpMethod.Get, "/api/v1/policies/fallback/authed-by-default", token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task OrgScopedEndpoint_MemberOfOrganizationA_CanAccessA_ButNotB()
    {
        // Seed two organizations and a user who is an active member of A only.
        await using var context = CreateSeedContext();
        var user = new User
        {
            Id = Guid.CreateVersion7(),
            ClerkId = "cross_org_user",
            Email = "cross.org@aveline.lk",
            FirstName = "Cross",
            LastName = "Org",
            Username = "cross_org_user",
            UserRole = Roles.Staff,
            OrganizationRole = string.Empty,
            HasCompletedOnboarding = true,
            AccountState = AccountState.Active,
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var owner = new User { Id = Guid.CreateVersion7(), ClerkId = "cross_org_owner", Email = "cross.owner@aveline.lk", FirstName = "Owner", LastName = "Org", Username = "cross_org_owner", UserRole = Roles.Staff, OrganizationRole = string.Empty, HasCompletedOnboarding = true };
        context.Users.Add(owner);
        await context.SaveChangesAsync();

        var orgA = new Organization { Name = "Org A", Slug = "org-a", OwnerUserId = owner.Id, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        var orgB = new Organization { Name = "Org B", Slug = "org-b", OwnerUserId = owner.Id, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        context.Organizations.AddRange(orgA, orgB);
        await context.SaveChangesAsync();

        context.OrganizationMemberships.Add(new OrganizationMembership
        {
            OrganizationId = orgA.Id,
            UserId = user.Id,
            BoutiqueRole = Roles.BoutiqueOwner,
            Status = MembershipStatus.Active,
        });
        await context.SaveChangesAsync();

        var token = CreateToken("cross_org_user", "staff");

        var inScope = await _client.SendAsync(
            Authorized(HttpMethod.Get, $"/api/v1/policies/orgs/{orgA.Id}/catalog", token));
        Assert.Equal(HttpStatusCode.OK, inScope.StatusCode);

        // Same valid JWT must be denied for an organization the user is not a member of.
        var crossOrg = await _client.SendAsync(
            Authorized(HttpMethod.Get, $"/api/v1/policies/orgs/{orgB.Id}/catalog", token));
        Assert.Equal(HttpStatusCode.Forbidden, crossOrg.StatusCode);
    }

    [Fact]
    public async Task OrgScopedEndpoint_WhenMembershipRemoved_Returns403()
    {
        await using var context = CreateSeedContext();
        var user = new User
        {
            Id = Guid.CreateVersion7(),
            ClerkId = "removed_member_user",
            Email = "removed.member@aveline.lk",
            FirstName = "Removed",
            LastName = "Member",
            Username = "removed_member_user",
            UserRole = Roles.Staff,
            OrganizationRole = string.Empty,
            HasCompletedOnboarding = true,
            AccountState = AccountState.Active,
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var owner = new User { Id = Guid.CreateVersion7(), ClerkId = "removed_member_owner", Email = "removed.owner@aveline.lk", FirstName = "Owner", LastName = "Removed", Username = "removed_member_owner", UserRole = Roles.Staff, OrganizationRole = string.Empty, HasCompletedOnboarding = true };
        context.Users.Add(owner);
        await context.SaveChangesAsync();

        var org = new Organization { Name = "Removal Boutique", Slug = "removal-boutique", OwnerUserId = owner.Id, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        context.Organizations.Add(org);
        await context.SaveChangesAsync();

        context.OrganizationMemberships.Add(new OrganizationMembership
        {
            OrganizationId = org.Id,
            UserId = user.Id,
            BoutiqueRole = Roles.BoutiqueStaff,
            Status = MembershipStatus.Active,
        });
        await context.SaveChangesAsync();

        var token = CreateToken("removed_member_user", "staff");

        var before = await _client.SendAsync(
            Authorized(HttpMethod.Get, $"/api/v1/policies/orgs/{org.Id}/catalog", token));
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        // Remove the membership (e.g. owner revokes access); authorization must follow.
        context.OrganizationMemberships.RemoveRange(
            context.OrganizationMemberships.Where(m => m.OrganizationId == org.Id && m.UserId == user.Id));
        await context.SaveChangesAsync();

        var after = await _client.SendAsync(
            Authorized(HttpMethod.Get, $"/api/v1/policies/orgs/{org.Id}/catalog", token));
        Assert.Equal(HttpStatusCode.Forbidden, after.StatusCode);
    }

    [Fact]
    public async Task OrgScopedEndpoint_MemberSuspendedAccount_Returns403AccountSuspended()
    {
        await using var context = CreateSeedContext();
        var user = new User
        {
            Id = Guid.CreateVersion7(),
            ClerkId = "suspended_scoped_user",
            Email = "suspended.scoped@aveline.lk",
            FirstName = "Suspended",
            LastName = "Scoped",
            Username = "suspended_scoped_user",
            UserRole = Roles.Staff,
            OrganizationRole = string.Empty,
            HasCompletedOnboarding = true,
            AccountState = AccountState.Suspended,
            IsActive = false,
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var owner = new User { Id = Guid.CreateVersion7(), ClerkId = "suspended_scoped_owner", Email = "ss.owner@aveline.lk", FirstName = "Owner", LastName = "Scoped", Username = "suspended_scoped_owner", UserRole = Roles.Staff, OrganizationRole = string.Empty, HasCompletedOnboarding = true };
        context.Users.Add(owner);
        await context.SaveChangesAsync();

        var org = new Organization { Name = "Suspended Scoped", Slug = "suspended-scoped", OwnerUserId = owner.Id, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        context.Organizations.Add(org);
        await context.SaveChangesAsync();

        context.OrganizationMemberships.Add(new OrganizationMembership
        {
            OrganizationId = org.Id,
            UserId = user.Id,
            BoutiqueRole = Roles.BoutiqueOwner,
            Status = MembershipStatus.Active,
        });
        await context.SaveChangesAsync();

        var token = CreateToken("suspended_scoped_user", "staff");

        // The valid JWT would authorize this endpoint by role, but the suspended
        // local account must be denied before the endpoint executes.
        var response = await _client.SendAsync(
            Authorized(HttpMethod.Get, $"/api/v1/policies/orgs/{org.Id}/catalog", token));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("account-suspended", await response.Content.ReadAsStringAsync());
    }
}
