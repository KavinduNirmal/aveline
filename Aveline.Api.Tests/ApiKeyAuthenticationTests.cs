using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using Aveline.Api.Authorization;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.ApiAccess.Domain;
using Aveline.Api.Modules.ApiAccess.Models;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Aveline.Api.Tests;

/// <summary>
/// Issue #203 — API-key authentication end to end through the authorization handlers
/// (FR-3.17, BR-3.5, plan §8.2).
/// </summary>
public class ApiKeyAuthenticationTests : IAsyncLifetime
{
    private const string DatabaseName = "AvelineInMemoryDb";

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

    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: DatabaseName)
            .Options);

    private static async Task<Organization> SeedOrganizationAsync(
        string suffix, bool isActive = true, DateTime? suspendedAt = null)
    {
        await using var context = CreateContext();
        var owner = new User
        {
            Id = Guid.CreateVersion7(),
            ClerkId = $"key_owner_{suffix}",
            Email = $"key_{suffix}@aveline.lk",
            FirstName = "Key",
            LastName = "Owner",
            Username = $"key_{suffix}",
            UserRole = Roles.BoutiqueOwner,
            OrganizationRole = Roles.BoutiqueOwner,
            HasCompletedOnboarding = true,
            AccountState = AccountState.Active,
        };
        context.Users.Add(owner);

        var org = new Organization
        {
            Name = $"Key Org {suffix}",
            Slug = $"key-{suffix}",
            OwnerUserId = owner.Id,
            IsActive = isActive,
            SuspendedAt = suspendedAt,
        };
        context.Organizations.Add(org);
        context.OrganizationMemberships.Add(new OrganizationMembership
        {
            OrganizationId = org.Id,
            UserId = owner.Id,
            BoutiqueRole = Roles.BoutiqueOwner,
            Status = MembershipStatus.Active,
        });
        await context.SaveChangesAsync();
        return org;
    }

    private static async Task<(ApiKey Key, string Plaintext)> SeedKeyAsync(
        Guid organizationId, string suffix, params string[] scopes)
    {
        await using var context = CreateContext();
        var generated = ApiKeyCredentials.Generate(ApiKeyEnvironment.Live);
        var key = new ApiKey
        {
            OrganizationId = organizationId,
            Name = $"Key {suffix}",
            Prefix = generated.Prefix,
            KeyHash = generated.Hash,
            Scopes = [.. scopes],
            Environment = ApiKeyEnvironment.Live,
            Status = ApiKeyStatus.Active,
            CreatedByUserId = Guid.CreateVersion7(),
        };
        context.ApiKeys.Add(key);
        await context.SaveChangesAsync();
        return (key, generated.Plaintext);
    }

    private static async Task RevokeKeyAsync(Guid keyId)
    {
        await using var context = CreateContext();
        var key = await context.ApiKeys.SingleAsync(k => k.Id == keyId);
        key.Status = ApiKeyStatus.Revoked;
        key.RevokedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();
    }

    private HttpRequestMessage ApiKeyRequest(HttpMethod method, string path, string plaintext)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Api-Key", plaintext);
        return request;
    }

    private string CreateToken(string clerkId, string? userRole = null, string? orgRole = null)
    {
        var claims = new List<Claim> { new("sub", clerkId) };
        if (userRole is not null) claims.Add(new Claim("user_role", userRole));
        if (orgRole is not null) claims.Add(new Claim("org_role", orgRole));

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

    [Fact]
    public async Task ValidKey_AuthenticatesAnOrgScopedRoute()
    {
        var org = await SeedOrganizationAsync("valid");
        var (_, plaintext) = await SeedKeyAsync(org.Id, "valid", Permissions.CatalogView);

        var response = await _client.SendAsync(
            ApiKeyRequest(HttpMethod.Get, $"/api/v1/orgs/{org.Id}", plaintext));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task KeyForAnotherOrganization_Returns404()
    {
        var orgA = await SeedOrganizationAsync("cross-a");
        var orgB = await SeedOrganizationAsync("cross-b");
        var (_, plaintext) = await SeedKeyAsync(orgA.Id, "cross", Permissions.CatalogView);

        var response = await _client.SendAsync(
            ApiKeyRequest(HttpMethod.Get, $"/api/v1/orgs/{orgB.Id}", plaintext));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RevokedKey_Returns401()
    {
        var org = await SeedOrganizationAsync("revoked");
        var (key, plaintext) = await SeedKeyAsync(org.Id, "revoked", Permissions.CatalogView);
        await RevokeKeyAsync(key.Id);

        var response = await _client.SendAsync(
            ApiKeyRequest(HttpMethod.Get, $"/api/v1/orgs/{org.Id}", plaintext));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UnknownKey_Returns401()
    {
        var org = await SeedOrganizationAsync("unknown");
        var unknown = ApiKeyCredentials.Generate(ApiKeyEnvironment.Live).Plaintext;

        var response = await _client.SendAsync(
            ApiKeyRequest(HttpMethod.Get, $"/api/v1/orgs/{org.Id}", unknown));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SuspendedOrganizationKey_Returns401()
    {
        var org = await SeedOrganizationAsync("suspended", isActive: false, suspendedAt: DateTime.UtcNow);
        var (_, plaintext) = await SeedKeyAsync(org.Id, "suspended", Permissions.CatalogView);

        var response = await _client.SendAsync(
            ApiKeyRequest(HttpMethod.Get, $"/api/v1/orgs/{org.Id}", plaintext));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task KeyWithoutTheRequiredScope_Returns403()
    {
        var org = await SeedOrganizationAsync("scope");
        var (_, plaintext) = await SeedKeyAsync(org.Id, "scope", Permissions.CatalogView);

        var response = await _client.SendAsync(
            ApiKeyRequest(HttpMethod.Get, $"/api/v1/orgs/{org.Id}/blossoms/balance", plaintext));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task KeyWithTheRequiredScope_IsAuthorized()
    {
        var org = await SeedOrganizationAsync("scope-ok");
        var (_, plaintext) = await SeedKeyAsync(org.Id, "scope-ok", Permissions.BillingView);

        var response = await _client.SendAsync(
            ApiKeyRequest(HttpMethod.Get, $"/api/v1/orgs/{org.Id}/blossoms/balance", plaintext));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ApiKeyIsNeverAcceptedOnATeamOnlyRoute()
    {
        var org = await SeedOrganizationAsync("admin");
        var (_, plaintext) = await SeedKeyAsync(org.Id, "admin", Permissions.CatalogView);

        var response = await _client.SendAsync(
            ApiKeyRequest(HttpMethod.Get, "/api/v1/admin/pricing/rules", plaintext));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task BearerTokenStillAuthenticatesOrgScopedRoutes()
    {
        var org = await SeedOrganizationAsync("bearer");
        var token = CreateToken("key_owner_bearer", orgRole: Roles.BoutiqueOwner);

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/orgs/{org.Id}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
