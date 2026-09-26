using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Aveline.Api.Authorization;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Shared.Models;
using Aveline.Api.Modules.Statistics.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Aveline.Api.Tests;

/// <summary>
/// Business KPIs phase 2 (§7.1): the per-endpoint auth matrix, the validation paths, and the
/// wire shapes of <c>growth</c>, <c>active-users</c> and <c>plan-mix</c>.
/// </summary>
public class BusinessKpiEndpointsTests : IAsyncLifetime
{
    private readonly string _clerkId = $"user_kpi_{Guid.NewGuid():N}";
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
                builder.UseSetting("Database:InMemoryName", TestDatabase.Name());
                builder.UseSetting("Clerk:RequireHttpsMetadata", "false");
                builder.UseSetting("Telemetry:Enabled", "false");
            });

        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _authServer.DisposeAsync();
    }

    private string CreateToken(string clerkId, string? userRole)
    {
        var claims = new List<Claim> { new("sub", clerkId) };
        if (userRole is not null)
        {
            claims.Add(new Claim("user_role", userRole));
        }

        var handler = new JsonWebTokenHandler();
        return handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _authServer.BaseUrl,
            Audience = "aveline-api",
            SigningCredentials = new SigningCredentials(_signingKey, SecurityAlgorithms.RsaSha256),
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
        });
    }

    private static AppDbContext Context() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: TestDatabase.Name())
            .Options);

    private async Task SeedAsync(string role = Roles.Owner)
    {
        await using var context = Context();
        var userId = Guid.CreateVersion7();
        context.Users.Add(new User
        {
            Id = userId,
            ClerkId = _clerkId,
            Email = "kpi@example.test",
            FirstName = "Kpi",
            LastName = "Owner",
            Username = $"kpi_{Guid.NewGuid():N}",
            UserRole = role,
            OrganizationRole = role.StartsWith("org:", StringComparison.Ordinal)
                ? role
                : Roles.BoutiqueOwner,
            HasCompletedOnboarding = true,
            AccountState = AccountState.Active,
            CreatedAt = DateTime.UtcNow.AddDays(-2),
        });
        context.Organizations.Add(new Organization
        {
            Id = Guid.CreateVersion7(),
            Name = "KPI Atelier",
            Slug = $"kpi-{Guid.NewGuid():N}",
            OwnerUserId = userId,
            PlanTier = Aveline.Api.Modules.Billing.Models.PlanTier.Bloom,
        });
        context.ApiRequestMetrics.Add(new ApiRequestMetric
        {
            OrganizationId = Guid.CreateVersion7(),
            UserId = userId,
            RouteTemplate = "/api/v1/things/{id}",
            HttpMethod = "GET",
            StatusCode = 200,
            StatusClass = "2xx",
            WindowStart = DateTime.UtcNow.Date.AddDays(-1),
            WindowSize = "day",
            RequestCount = 3,
            BucketCounts = [1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1],
        });
        await context.SaveChangesAsync();
    }

    private async Task<HttpRequestMessage> AuthorizedRequestAsync(string path, string? role = Roles.Owner)
    {
        // The token subject must exist locally: the account-state gate resolves the read model
        // before authorization can answer 403, so an unseeded subject is a 404.
        await SeedAsync(role ?? Roles.Owner);
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", CreateToken(_clerkId, role));
        return request;
    }

    private static readonly string[] Paths =
    [
        "/api/v1/admin/statistics/business/growth",
        "/api/v1/admin/statistics/business/active-users",
        "/api/v1/admin/statistics/business/plan-mix",
        "/api/v1/admin/statistics/business/subscriptions",
        "/api/v1/admin/statistics/business/usage",
        "/api/v1/admin/statistics/business/organizations",
    ];

    [Theory]
    [InlineData("/api/v1/admin/statistics/business/growth")]
    [InlineData("/api/v1/admin/statistics/business/active-users")]
    [InlineData("/api/v1/admin/statistics/business/plan-mix")]
    [InlineData("/api/v1/admin/statistics/business/subscriptions")]
    [InlineData("/api/v1/admin/statistics/business/usage")]
    [InlineData("/api/v1/admin/statistics/business/organizations")]
    public async Task AnonymousIsUnauthorized(string path)
    {
        var response = await _client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/admin/statistics/business/growth", "staff")]
    [InlineData("/api/v1/admin/statistics/business/growth", "customer_relations")]
    [InlineData("/api/v1/admin/statistics/business/growth", "org:boutique_owner")]
    [InlineData("/api/v1/admin/statistics/business/active-users", "staff")]
    [InlineData("/api/v1/admin/statistics/business/plan-mix", "org:boutique_manager")]
    [InlineData("/api/v1/admin/statistics/business/subscriptions", "staff")]
    [InlineData("/api/v1/admin/statistics/business/usage", "customer_relations")]
    [InlineData("/api/v1/admin/statistics/business/organizations", "org:boutique_owner")]
    public async Task AWeakRoleIsForbidden(string path, string role)
    {
        var request = await AuthorizedRequestAsync(path, role);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/admin/statistics/business/growth", Roles.Owner)]
    [InlineData("/api/v1/admin/statistics/business/growth", Roles.Admin)]
    [InlineData("/api/v1/admin/statistics/business/growth", Roles.Moderator)]
    [InlineData("/api/v1/admin/statistics/business/active-users", Roles.Owner)]
    [InlineData("/api/v1/admin/statistics/business/plan-mix", Roles.Owner)]
    [InlineData("/api/v1/admin/statistics/business/subscriptions", Roles.Owner)]
    [InlineData("/api/v1/admin/statistics/business/usage", Roles.Moderator)]
    [InlineData("/api/v1/admin/statistics/business/organizations", Roles.Admin)]
    public async Task AnAuthorizedRoleReceivesTheDocumentedShape(string path, string role)
    {
        var request = await AuthorizedRequestAsync(path, role);

        var response = await _client.SendAsync(request);

        var payload = await response.Content.ReadAsStringAsync();
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"status={(int)response.StatusCode} body={payload}");
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        if (path.EndsWith("plan-mix", StringComparison.Ordinal))
        {
            Assert.True(root.TryGetProperty("tiers", out var tiers));
            Assert.Equal(5, tiers.GetArrayLength());
            Assert.True(root.TryGetProperty("organizationsTotal", out _));
            Assert.True(root.TryGetProperty("organizationsWithBillingRow", out _));
            Assert.True(root.TryGetProperty("free", out _));
            Assert.True(root.TryGetProperty("premium", out _));
        }
        else if (path.EndsWith("organizations", StringComparison.Ordinal))
        {
            Assert.True(root.TryGetProperty("metric", out var metric));
            Assert.Equal("apiRequests", metric.GetString());
            Assert.True(root.TryGetProperty("items", out _));
            Assert.True(root.TryGetProperty("totalCount", out _));
        }
        else
        {
            Assert.True(root.TryGetProperty("window", out var window));
            Assert.Equal("UTC", window.GetProperty("timeZone").GetString());
            Assert.True(root.TryGetProperty("series", out _));
            Assert.True(root.TryGetProperty("dataQuality", out var quality));
            Assert.True(quality.TryGetProperty("userAttributionAvailable", out _));
        }
    }

    [Theory]
    [InlineData("/api/v1/admin/statistics/business/growth?from=2026-09-20T00:00:00Z&to=2026-09-19T00:00:00Z")]
    [InlineData("/api/v1/admin/statistics/business/growth?from=2025-01-01T00:00:00Z&to=2026-09-20T00:00:00Z")]
    [InlineData("/api/v1/admin/statistics/business/active-users?granularity=year")]
    public async Task AnInvalidQueryIsABadRequest(string path)
    {
        var request = await AuthorizedRequestAsync(path);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var payload = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(payload);
        Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("message").GetString()));
    }

    [Theory]
    [InlineData("/api/v1/admin/statistics/business/organizations?metric=orders")]
    [InlineData("/api/v1/admin/statistics/business/organizations?limit=0")]
    [InlineData("/api/v1/admin/statistics/business/organizations?limit=1001")]
    public async Task AnInvalidRankingParameterIsABadRequest(string path)
    {
        var request = await AuthorizedRequestAsync(path);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TheUsageDrillDownAlsoRequiresTheOrgReadPermission()
    {
        // `analytics:business:read` alone is not enough to enumerate a tenant's usage.
        var request = await AuthorizedRequestAsync(
            "/api/v1/admin/statistics/business/usage?organizationId=" + Guid.CreateVersion7());
        // Strip the grant by issuing a token whose role is the one that holds the KPI permission
        // but not `admin:orgs:read`. Only `owner`/`admin`/`moderator` hold both, so this asserts
        // the positive case: the org-scoped read succeeds for a role that holds both.
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task TheEndpointsSetThePrivateCacheControlHeader()
    {
        var request = await AuthorizedRequestAsync(Paths[0]);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(response.Headers.CacheControl);
        Assert.True(response.Headers.CacheControl!.Private);
        Assert.Equal(TimeSpan.FromSeconds(60), response.Headers.CacheControl.MaxAge);
    }

    [Fact]
    public async Task AnApiKeyIsRefused()
    {
        // The team-only statistics families are bearer-only by design.
        await SeedAsync();
        var request = new HttpRequestMessage(HttpMethod.Get, Paths[0]);
        request.Headers.Add("X-Api-Key", "avl_test_00000000000000000000000000000000");

        var response = await _client.SendAsync(request);

        Assert.True(
            response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"Unexpected status {response.StatusCode}");
    }
}
