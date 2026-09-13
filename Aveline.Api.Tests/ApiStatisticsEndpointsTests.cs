using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Aveline.Api.Authorization;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Shared.Models;
using Aveline.Api.Modules.Statistics.Domain;
using Aveline.Api.Modules.Statistics.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Aveline.Api.Tests;

/// <summary>
/// Issue #223 — the API statistics endpoints (docs/api/README.md §C.7). Verifies org
/// isolation, the team-only admin subset, anonymous 401s, the 400 validation surface and
/// the <c>null</c>-percentile body.
/// </summary>
public class ApiStatisticsEndpointsTests : IAsyncLifetime
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
                builder.UseSetting("Telemetry:Enabled", "false");
                builder.UseSetting("Telemetry:MinSampleForPercentile", "20");
                builder.UseSetting("Telemetry:MaxWindowDays", "92");
            });

        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _authServer.DisposeAsync();
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

    private Task<HttpResponseMessage> GetAsync(string path, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path)
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
        };
        return _client.SendAsync(request);
    }

    private static AppDbContext Context() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "AvelineInMemoryDb")
            .Options);

    private static async Task<(Guid OrgId, string OwnerClerkId)> SeedOwnerAsync(string suffix)
    {
        await using var context = Context();

        var owner = new User
        {
            Id = Guid.CreateVersion7(),
            ClerkId = $"apistats_owner_{suffix}",
            Email = $"apistats_{suffix}@aveline.lk",
            FirstName = "Api",
            LastName = "Owner",
            Username = $"apistats_{suffix}",
            UserRole = Roles.Staff,
            OrganizationRole = string.Empty,
            HasCompletedOnboarding = true,
            AccountState = AccountState.Active,
        };
        context.Users.Add(owner);

        var org = new Organization
        {
            Name = $"Api Stats Org {suffix}",
            Slug = $"api-stats-{suffix}",
            OwnerUserId = owner.Id,
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
        return (org.Id, owner.ClerkId);
    }

    private static async Task SeedAdminAsync(string clerkId)
    {
        await using var context = Context();
        context.Users.Add(new User
        {
            Id = Guid.CreateVersion7(),
            ClerkId = clerkId,
            Email = $"{clerkId}@aveline.lk",
            FirstName = "Admin",
            LastName = "User",
            Username = clerkId,
            UserRole = Roles.Admin,
            OrganizationRole = string.Empty,
            HasCompletedOnboarding = true,
            AccountState = AccountState.Active,
        });
        await context.SaveChangesAsync();
    }

    private static async Task SeedMetricAsync(
        Guid organizationId, long requestCount, short statusCode = 200, int durationMs = 10,
        string route = "/api/v1/things/{id}", Guid? userId = null)
    {
        await using var context = Context();

        var buckets = new int[LatencyBuckets.Count];
        for (var i = 0; i < requestCount; i++)
        {
            LatencyBuckets.Add(buckets, durationMs);
        }

        context.ApiRequestMetrics.Add(new ApiRequestMetric
        {
            OrganizationId = organizationId,
            UserId = userId,
            RouteTemplate = route,
            HttpMethod = "GET",
            StatusCode = statusCode,
            StatusClass = $"{statusCode / 100}xx",
            WindowStart = DateTime.UtcNow.AddHours(-2),
            WindowSize = "hour",
            RequestCount = requestCount,
            ErrorCount = statusCode >= 400 ? requestCount : 0,
            TotalDurationMs = requestCount * durationMs,
            MaxDurationMs = durationMs,
            BucketCounts = buckets,
        });

        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task Anonymous_Returns401()
    {
        var orgId = Guid.CreateVersion7();
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync(
            $"/api/v1/orgs/{orgId}/statistics/api/requests")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync(
            "/api/v1/admin/statistics/api/requests")).StatusCode);
    }

    [Fact]
    public async Task OrgRequests_AreScopedToTheCallersOrganization()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var (orgA, ownerA) = await SeedOwnerAsync($"a{suffix}");
        var (orgB, _) = await SeedOwnerAsync($"b{suffix}");
        await SeedMetricAsync(orgA, 5);
        await SeedMetricAsync(orgB, 100);

        var token = CreateToken(ownerA, orgRole: Roles.BoutiqueOwner);
        var response = await GetAsync($"/api/v1/orgs/{orgA}/statistics/api/requests", token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(5, body.GetProperty("requestCount").GetInt64());

        var forbidden = await GetAsync($"/api/v1/orgs/{orgB}/statistics/api/requests", token);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task AdminEndpoints_RequireTeamRole()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var (orgA, ownerA) = await SeedOwnerAsync($"adm{suffix}");
        await SeedMetricAsync(orgA, 7);

        var outsider = CreateToken(ownerA, orgRole: Roles.BoutiqueOwner);
        var forbidden = await GetAsync("/api/v1/admin/statistics/api/requests", outsider);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        await SeedAdminAsync($"apistats_admin_{suffix}");
        var admin = CreateToken($"apistats_admin_{suffix}", userRole: Roles.Admin);
        var ok = await GetAsync("/api/v1/admin/statistics/api/requests", admin);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    [Fact]
    public async Task Latency_ReturnsNullPercentilesWithAReasonBelowTheFloor()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var (orgA, ownerA) = await SeedOwnerAsync($"lat{suffix}");
        await SeedMetricAsync(orgA, 3, durationMs: 7);

        var token = CreateToken(ownerA, orgRole: Roles.BoutiqueOwner);
        var response = await GetAsync($"/api/v1/orgs/{orgA}/statistics/api/latency", token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal(JsonValueKind.Null, body.GetProperty("p50Ms").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("p95Ms").ValueKind);
        Assert.Equal(LatencyBuckets.InsufficientSamplesReason, body.GetProperty("reason").GetString());
        Assert.Equal("bucket-interpolated", body.GetProperty("precision").GetString());
        Assert.True(body.GetProperty("dataQuality").GetProperty("latencyBuckets").GetBoolean());
    }

    [Fact]
    public async Task Users_ReturnThePaginationEnvelope()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var (orgA, ownerA) = await SeedOwnerAsync($"page{suffix}");
        await SeedMetricAsync(orgA, 4, userId: Guid.CreateVersion7());
        await SeedMetricAsync(orgA, 2, userId: Guid.CreateVersion7(), route: "/api/v1/other");

        var token = CreateToken(ownerA, orgRole: Roles.BoutiqueOwner);
        var response = await GetAsync(
            $"/api/v1/orgs/{orgA}/statistics/api/users?page=1&pageSize=1", token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        Assert.Single(body.GetProperty("items").EnumerateArray());
        Assert.Equal(1, body.GetProperty("page").GetInt32());
        Assert.Equal(1, body.GetProperty("pageSize").GetInt32());
        Assert.Equal(2, body.GetProperty("total").GetInt32());
    }

    [Theory]
    [InlineData("status=not-a-status")]
    [InlineData("statusClass=9xx")]
    [InlineData("groupBy=week")]
    [InlineData("from=2026-09-12T00:00:00Z&to=2026-09-11T00:00:00Z")]
    [InlineData("from=2020-01-01T00:00:00Z&to=2026-09-11T00:00:00Z")]
    public async Task InvalidQueryValues_Return400WithMessage(string query)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var (orgA, ownerA) = await SeedOwnerAsync($"bad{suffix}");
        var token = CreateToken(ownerA, orgRole: Roles.BoutiqueOwner);

        var response = await GetAsync(
            $"/api/v1/orgs/{orgA}/statistics/api/requests?{query}", token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.True(body.TryGetProperty("message", out var message));
        Assert.False(string.IsNullOrWhiteSpace(message.GetString()));
    }

    [Fact]
    public async Task AdminApiKeys_ReturnSystemWideKeys()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var (orgA, _) = await SeedOwnerAsync($"keys{suffix}");
        var apiKeyId = Guid.CreateVersion7();
        await using (var context = Context())
        {
            context.ApiKeys.Add(new Modules.ApiAccess.Models.ApiKey
            {
                Id = apiKeyId,
                OrganizationId = orgA,
                Name = "Key",
                Prefix = $"avl_live_{suffix[..4]}",
                KeyHash = new string('a', 64),
            });
            await context.SaveChangesAsync();
        }

        await SeedMetricAsync(orgA, 9);
        // Attach the key id by rewriting the seeded metric. The shared in-memory database
        // means other classes' telemetry may also be present, so match this test's row.
        await using (var context = Context())
        {
            var metric = await context.ApiRequestMetrics.FirstAsync(
                candidate => candidate.OrganizationId == orgA && candidate.RequestCount == 9);
            metric.ApiKeyId = apiKeyId;
            await context.SaveChangesAsync();
        }

        await SeedAdminAsync($"apistats_keysadmin_{suffix}");
        var admin = CreateToken($"apistats_keysadmin_{suffix}", userRole: Roles.Admin);

        // Scope the window to the seeded metric so live telemetry written by other test
        // classes into the shared in-memory database cannot change the total.
        var from = DateTime.UtcNow.AddHours(-3);
        var to = DateTime.UtcNow.AddHours(-1);
        var response = await GetAsync(
            $"/api/v1/admin/statistics/api-keys?from={from:O}&to={to:O}", admin);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.True(body.GetProperty("total").GetInt32() >= 1);
        var item = body.GetProperty("items").EnumerateArray()
            .Single(candidate => candidate.GetProperty("apiKeyId").GetGuid() == apiKeyId);
        Assert.Equal(9, item.GetProperty("requestCount").GetInt64());
    }
}
