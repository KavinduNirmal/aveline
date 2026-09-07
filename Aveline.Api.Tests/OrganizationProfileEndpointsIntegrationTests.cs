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
/// Tenant-dashboard (#74) endpoints: slug-based boutique resolution, org-scoped profile,
/// org-scoped Blossom usage, and the enriched /orgs/my membership payload.
/// </summary>
public class OrganizationProfileEndpointsIntegrationTests : IAsyncLifetime
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

    private string CreateToken(string clerkId) => CreateToken(clerkId, "staff");

    private string CreateToken(string clerkId, string? userRole)
    {
        var claims = new List<Claim> { new("sub", clerkId) };
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
    public async Task Owner_BySlug_ReturnsProfile_WithOwnerMembership()
    {
        var (owner, _, org) = await SeedBoutiqueAsync("profile_owner");
        var token = CreateToken(owner.ClerkId);

        var response = await _client.SendAsync(
            Authorized(HttpMethod.Get, $"/api/v1/orgs/by-slug/{org.Slug}", token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(org.Name, doc.GetProperty("organization").GetProperty("name").GetString());
        Assert.Equal(org.Slug, doc.GetProperty("organization").GetProperty("slug").GetString());
        Assert.Equal(Roles.BoutiqueOwner, doc.GetProperty("membership").GetProperty("boutiqueRole").GetString());
    }

    [Fact]
    public async Task ActiveNonMember_BySlug_Returns404()
    {
        var (_, _, orgA) = await SeedBoutiqueAsync("by_slug_a");
        var (foreignOwner, _, _) = await SeedBoutiqueAsync("by_slug_b");
        var token = CreateToken(foreignOwner.ClerkId);

        // Cross-tenant (and existence) must not be observable via the by-slug lookup:
        // a boutique the caller does not belong to is indistinguishable from a missing one.
        var response = await _client.SendAsync(
            Authorized(HttpMethod.Get, $"/api/v1/orgs/by-slug/{orgA.Slug}", token));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task BySlug_UnknownSlug_Returns404()
    {
        var (owner, _, _) = await SeedBoutiqueAsync("by_slug_missing");
        var token = CreateToken(owner.ClerkId);

        var response = await _client.SendAsync(
            Authorized(HttpMethod.Get, "/api/v1/orgs/by-slug/does-not-exist", token));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task OrgProfile_Member_CanAccess_AndReturnsProfile()
    {
        var (owner, _, org) = await SeedBoutiqueAsync("profile_scoped");
        var token = CreateToken(owner.ClerkId);

        var response = await _client.SendAsync(
            Authorized(HttpMethod.Get, $"/api/v1/orgs/{org.Id}", token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(org.Name, doc.GetProperty("name").GetString());
        Assert.Equal(org.Slug, doc.GetProperty("slug").GetString());
    }

    [Fact]
    public async Task OrgProfile_NonMemberOfTargetOrg_Returns403()
    {
        var (_, _, orgA) = await SeedBoutiqueAsync("profile_denied_a");
        var (foreignOwner, _, _) = await SeedBoutiqueAsync("profile_denied_b");
        var token = CreateToken(foreignOwner.ClerkId);

        var response = await _client.SendAsync(
            Authorized(HttpMethod.Get, $"/api/v1/orgs/{orgA.Id}", token));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task OrgUsage_Member_ReturnsUsageSummary()
    {
        var (owner, _, org) = await SeedBoutiqueAsync("usage_member");
        var token = CreateToken(owner.ClerkId);

        var response = await _client.SendAsync(
            Authorized(HttpMethod.Get, $"/api/v1/orgs/{org.Id}/usage", token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(org.Id, doc.GetProperty("organizationId").GetGuid());
        Assert.Equal("Active", doc.GetProperty("status").GetString());
        // No usage recorded yet; a fresh Seed-tier account is provisioned on first read.
        Assert.Equal(0, doc.GetProperty("blossomUsed").GetInt32());
    }

    [Fact]
    public async Task OrgUsage_NonMemberOfTargetOrg_Returns403()
    {
        var (_, _, orgA) = await SeedBoutiqueAsync("usage_denied_a");
        var (foreignOwner, _, _) = await SeedBoutiqueAsync("usage_denied_b");
        var token = CreateToken(foreignOwner.ClerkId);

        var response = await _client.SendAsync(
            Authorized(HttpMethod.Get, $"/api/v1/orgs/{orgA.Id}/usage", token));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task MyOrganizations_IncludeSlugAndName()
    {
        var (owner, _, org) = await SeedBoutiqueAsync("my_orgs_slug");
        var token = CreateToken(owner.ClerkId);

        var response = await _client.SendAsync(
            Authorized(HttpMethod.Get, "/api/v1/orgs/my", token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var array = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(1, array.GetArrayLength());
        Assert.Equal(org.Slug, array[0].GetProperty("slug").GetString());
        Assert.Equal(org.Name, array[0].GetProperty("organizationName").GetString());
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
            PlanTier = Aveline.Api.Modules.Billing.Models.PlanTier.Seed,
            HasCompletedOnboarding = true,
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
}
