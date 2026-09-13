using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Aveline.Api.Authorization;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Aveline.Api.Tests;

/// <summary>
/// Issue #241 / FR-4.8 — the Aveline-team paginated organization search behind
/// <c>admin:orgs:read</c>.
/// </summary>
public class AdminOrganizationEndpointsIntegrationTests : IAsyncLifetime
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

    private static async Task<Guid> SeedOrganizationAsync(
        string name, string slug, PlanTier tier = PlanTier.Bloom, bool isActive = true)
    {
        await using var context = CreateContext();
        var org = new Organization
        {
            Name = name,
            Slug = slug,
            OwnerUserId = Guid.CreateVersion7(),
            PlanTier = tier,
            IsActive = isActive,
        };
        context.Organizations.Add(org);
        await context.SaveChangesAsync();
        return org.Id;
    }

    private string CreateToken(string clerkId, string? userRole)
    {
        var claims = new List<Claim> { new("sub", clerkId) };
        if (userRole is not null) claims.Add(new Claim("user_role", userRole));

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

    /// <summary>
    /// The account-state gate (M-15) requires a local Active user before an /admin route
    /// beyond the onboarding exemption is reachable.
    /// </summary>
    private static async Task SeedActiveUserAsync(string clerkId, string role = Roles.Admin)
    {
        await using var context = CreateContext();
        if (await context.Users.AnyAsync(u => u.ClerkId == clerkId))
        {
            return;
        }

        context.Users.Add(new User
        {
            Id = Guid.CreateVersion7(),
            ClerkId = clerkId,
            Email = $"{clerkId}@aveline.lk",
            FirstName = "Org",
            LastName = "Searcher",
            Username = clerkId,
            UserRole = role,
            OrganizationRole = string.Empty,
            HasCompletedOnboarding = true,
            AccountState = AccountState.Active,
        });
        await context.SaveChangesAsync();
    }

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    [Fact]
    public async Task Admin_Search_ReturnsPagedShape()
    {
        var token = Guid.NewGuid().ToString("N");
        var orgId = await SeedOrganizationAsync(
            $"Admin Org {token}", $"admin-org-{token}", PlanTier.Orchid);

        var adminToken = CreateToken($"admin_orgs_{token}", Roles.Admin);
        await SeedActiveUserAsync($"admin_orgs_{token}");
        var response = await _client.SendAsync(Authorized(
            HttpMethod.Get, $"/api/v1/admin/orgs?q={token}", adminToken));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await BodyAsync(response);
        Assert.Equal(1, body.GetProperty("total").GetInt32());
        Assert.Equal(1, body.GetProperty("page").GetInt32());
        Assert.Equal(50, body.GetProperty("pageSize").GetInt32());
        var item = body.GetProperty("items").EnumerateArray().Single();
        Assert.Equal(orgId.ToString(), item.GetProperty("id").GetString());
        Assert.Equal($"admin-org-{token}", item.GetProperty("slug").GetString());
        Assert.Equal("Orchid", item.GetProperty("planTier").GetString());
        Assert.True(item.GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task Admin_Search_FiltersByActiveState()
    {
        var token = Guid.NewGuid().ToString("N");
        await SeedOrganizationAsync($"AdminOrg Inactive {token}", $"admin-inactive-{token}", isActive: false);

        var adminToken = CreateToken($"admin_orgs_{token}", Roles.Admin);
        await SeedActiveUserAsync($"admin_orgs_{token}");
        var response = await _client.SendAsync(Authorized(
            HttpMethod.Get, $"/api/v1/admin/orgs?q={token}&isActive=false", adminToken));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await BodyAsync(response);
        var item = body.GetProperty("items").EnumerateArray().Single();
        Assert.False(item.GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task Moderator_HoldingAdminOrgsRead_CanSearch()
    {
        var token = Guid.NewGuid().ToString("N");
        await SeedOrganizationAsync($"Moderator Org {token}", $"moderator-org-{token}");

        var moderatorToken = CreateToken($"admin_orgs_mod_{token}", Roles.Moderator);
        await SeedActiveUserAsync($"admin_orgs_mod_{token}", Roles.Moderator);
        var response = await _client.SendAsync(Authorized(
            HttpMethod.Get, $"/api/v1/admin/orgs?q={token}", moderatorToken));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await BodyAsync(response);
        Assert.Equal(1, body.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task BoutiqueOwner_IsForbidden()
    {
        var token = Guid.NewGuid().ToString("N");
        await SeedOrganizationAsync($"Owner Org {token}", $"owner-org-{token}");

        var ownerToken = CreateToken($"admin_orgs_owner_{token}", Roles.BoutiqueOwner);
        await SeedActiveUserAsync($"admin_orgs_owner_{token}", Roles.BoutiqueOwner);
        var response = await _client.SendAsync(Authorized(
            HttpMethod.Get, $"/api/v1/admin/orgs?q={token}", ownerToken));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_IsUnauthorized()
    {
        var response = await _client.GetAsync("/api/v1/admin/orgs");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
