using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
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
/// Issue #204 — API-key management endpoints and the entitlement gate
/// (FR-3.10–FR-3.13, FR-3.16).
/// </summary>
public class ApiKeyEndpointsIntegrationTests : IAsyncLifetime
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

    private static async Task<(Guid OrgId, string OwnerClerkId)> SeedBoutiqueAsync(
        string suffix,
        string ownerRole = Roles.BoutiqueOwner,
        Modules.Billing.Models.PlanTier tier = Modules.Billing.Models.PlanTier.Rose)
    {
        await using var context = CreateContext();
        var owner = new User
        {
            Id = Guid.CreateVersion7(),
            ClerkId = $"apikey_owner_{suffix}",
            Email = $"apikey_{suffix}@aveline.lk",
            FirstName = "Api",
            LastName = "Owner",
            Username = $"apikey_{suffix}",
            UserRole = Roles.BoutiqueOwner,
            OrganizationRole = ownerRole,
            HasCompletedOnboarding = true,
            AccountState = AccountState.Active,
        };
        context.Users.Add(owner);

        var org = new Organization
        {
            Name = $"ApiKey Org {suffix}",
            Slug = $"apikey-{suffix}",
            OwnerUserId = owner.Id,
            PlanTier = tier,
        };
        context.Organizations.Add(org);
        context.OrganizationMemberships.Add(new OrganizationMembership
        {
            OrganizationId = org.Id,
            UserId = owner.Id,
            BoutiqueRole = ownerRole,
            Status = MembershipStatus.Active,
        });
        await context.SaveChangesAsync();
        return (org.Id, owner.ClerkId);
    }

    private static async Task<(Guid KeyId, string Plaintext)> SeedKeyAsync(
        Guid organizationId, string suffix, long requestCount = 0, params string[] scopes)
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
            CreatedByUserId = Guid.CreateVersion7(),
            RequestCount = requestCount,
            LastUsedAt = requestCount > 0 ? DateTime.UtcNow : null,
        };
        context.ApiKeys.Add(key);
        await context.SaveChangesAsync();
        return (key.Id, generated.Plaintext);
    }

    private string CreateToken(string clerkId, string? orgRole = null)
    {
        var claims = new List<Claim> { new("sub", clerkId) };
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

    private HttpRequestMessage Authorized(
        HttpMethod method, string path, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, path)
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
        };

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    [Fact]
    public async Task Create_ReturnsTheSecretExactlyOnce()
    {
        var (orgId, ownerClerk) = await SeedBoutiqueAsync("create");
        var token = CreateToken(ownerClerk, Roles.BoutiqueOwner);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Post, $"/api/v1/orgs/{orgId}/api-keys", token,
            new { name = "Production", scopes = new[] { Permissions.CatalogView } }));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await BodyAsync(response);
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("secret").GetString()));
        Assert.Equal("Production", body.GetProperty("key").GetProperty("name").GetString());
        Assert.False(body.GetProperty("key").TryGetProperty("keyHash", out _));

        var listResponse = await _client.SendAsync(Authorized(
            HttpMethod.Get, $"/api/v1/orgs/{orgId}/api-keys", token));
        var listBody = await BodyAsync(listResponse);
        var first = listBody.EnumerateArray().First();
        Assert.False(first.TryGetProperty("secret", out _));
        Assert.False(first.TryGetProperty("keyHash", out _));
    }

    [Fact]
    public async Task List_AsStaffWithoutPermission_Returns403()
    {
        var (orgId, staffClerk) = await SeedBoutiqueAsync("staff", Roles.BoutiqueStaff);
        var token = CreateToken(staffClerk, Roles.BoutiqueStaff);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Get, $"/api/v1/orgs/{orgId}/api-keys", token));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Create_OnASeedPlan_Returns403()
    {
        var (orgId, ownerClerk) = await SeedBoutiqueAsync(
            "seed", tier: Modules.Billing.Models.PlanTier.Seed);
        var token = CreateToken(ownerClerk, Roles.BoutiqueOwner);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Post, $"/api/v1/orgs/{orgId}/api-keys", token,
            new { name = "No Access", scopes = new[] { Permissions.CatalogView } }));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithAForbiddenScope_Returns400()
    {
        var (orgId, ownerClerk) = await SeedBoutiqueAsync("forbidden");
        var token = CreateToken(ownerClerk, Roles.BoutiqueOwner);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Post, $"/api/v1/orgs/{orgId}/api-keys", token,
            new { name = "Bad", scopes = new[] { Permissions.PricingManage } }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Revoke_MakesTheKeyUnusable()
    {
        var (orgId, ownerClerk) = await SeedBoutiqueAsync("revoke");
        var token = CreateToken(ownerClerk, Roles.BoutiqueOwner);
        var (keyId, plaintext) = await SeedKeyAsync(orgId, "revoke", scopes: Permissions.CatalogView);

        var revokeResponse = await _client.SendAsync(Authorized(
            HttpMethod.Post, $"/api/v1/orgs/{orgId}/api-keys/{keyId}/revoke", token,
            new { reason = "rotated" }));
        Assert.Equal(HttpStatusCode.OK, revokeResponse.StatusCode);

        var authenticated = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/orgs/{orgId}");
        authenticated.Headers.Add("X-Api-Key", plaintext);
        var authResponse = await _client.SendAsync(authenticated);
        Assert.Equal(HttpStatusCode.Unauthorized, authResponse.StatusCode);
    }

    [Fact]
    public async Task Delete_RemovesAnUnusedKeyButConflictsForAUsedOne()
    {
        var (orgId, ownerClerk) = await SeedBoutiqueAsync("delete");
        var token = CreateToken(ownerClerk, Roles.BoutiqueOwner);

        var (unusedId, _) = await SeedKeyAsync(orgId, "unused", scopes: Permissions.CatalogView);
        var unused = await _client.SendAsync(Authorized(
            HttpMethod.Delete, $"/api/v1/orgs/{orgId}/api-keys/{unusedId}", token));
        Assert.Equal(HttpStatusCode.NoContent, unused.StatusCode);

        var (usedId, _) = await SeedKeyAsync(
            orgId, "used", requestCount: 3, scopes: Permissions.CatalogView);
        var used = await _client.SendAsync(Authorized(
            HttpMethod.Delete, $"/api/v1/orgs/{orgId}/api-keys/{usedId}", token));
        Assert.Equal(HttpStatusCode.Conflict, used.StatusCode);
    }

    [Fact]
    public async Task Create_AsAnApiKey_Returns403()
    {
        var (orgId, _) = await SeedBoutiqueAsync("machine");
        var (_, plaintext) = await SeedKeyAsync(orgId, "machine", scopes: Permissions.ApiKeysManage);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/orgs/{orgId}/api-keys")
        {
            Content = JsonContent.Create(new { name = "Escalation", scopes = new[] { Permissions.CatalogView } }),
        };
        request.Headers.Add("X-Api-Key", plaintext);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Revoke_UnknownKey_Returns404()
    {
        var (orgId, ownerClerk) = await SeedBoutiqueAsync("missing");
        var token = CreateToken(ownerClerk, Roles.BoutiqueOwner);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Post, $"/api/v1/orgs/{orgId}/api-keys/{Guid.CreateVersion7()}/revoke", token,
            new { reason = "n/a" }));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task List_RequiresAuthentication()
    {
        var (orgId, _) = await SeedBoutiqueAsync("anonymous");

        var response = await _client.GetAsync($"/api/v1/orgs/{orgId}/api-keys");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
