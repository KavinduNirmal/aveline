using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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
/// Issue #230 — the team-only system statistics endpoints (docs/api/README.md §C.8).
/// Verifies the auth boundary, the overview/metrics/alerts shapes, acknowledge and the
/// 400 validation surface.
/// </summary>
public class SystemStatisticsEndpointsTests : IAsyncLifetime
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

    private static AppDbContext Context() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: TestDatabase.Name())
            .Options);

    private static async Task SeedOwnerAsync(string suffix)
    {
        await using var context = Context();

        var owner = new User
        {
            Id = Guid.CreateVersion7(),
            ClerkId = $"sysstats_owner_{suffix}",
            Email = $"sysstats_{suffix}@aveline.lk",
            FirstName = "Boutique",
            LastName = "Owner",
            Username = $"sysstats_{suffix}",
            UserRole = Roles.Staff,
            OrganizationRole = string.Empty,
            HasCompletedOnboarding = true,
            AccountState = AccountState.Active,
        };
        context.Users.Add(owner);

        var org = new Organization
        {
            Name = $"Sys Stats Org {suffix}",
            Slug = $"sys-stats-{suffix}",
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
    }

    private static async Task<string> SeedAdminAsync(string suffix)
    {
        var clerkId = $"sysstats_admin_{suffix}";
        await using var context = Context();
        context.Users.Add(new User
        {
            Id = Guid.CreateVersion7(),
            ClerkId = clerkId,
            Email = $"{clerkId}@aveline.lk",
            FirstName = "Aveline",
            LastName = "Admin",
            Username = clerkId,
            UserRole = Roles.Admin,
            OrganizationRole = string.Empty,
            HasCompletedOnboarding = true,
            AccountState = AccountState.Active,
        });
        await context.SaveChangesAsync();
        return clerkId;
    }

    private static async Task SeedSampleAsync(string metricName, decimal value, DateTime windowStart)
    {
        await using var context = Context();
        context.SystemMetricSamples.Add(new SystemMetricSample
        {
            MetricName = metricName,
            DimensionsJson = "{}",
            DimensionHash = new string('b', 64),
            ValueDecimal = value,
            Unit = "count",
            WindowStart = windowStart,
            WindowSize = "instant",
            SampledAt = windowStart,
        });
        await context.SaveChangesAsync();
    }

    private static async Task<Guid> SeedAlertAsync(
        string metricName, AlertSeverity severity, AlertStatus status, DateTime firedAt)
    {
        await using var context = Context();
        var alert = new SystemAlert
        {
            MetricName = metricName,
            Severity = severity,
            Status = status,
            Title = $"{metricName} breached",
            Detail = "seeded",
            ObservedValue = 1m,
            Threshold = 0m,
            FiredAt = firedAt,
            LastObservedAt = firedAt,
        };
        context.SystemAlerts.Add(alert);
        await context.SaveChangesAsync();
        return alert.Id;
    }

    [Fact]
    public async Task Anonymous_Returns401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync(
            "/api/v1/admin/statistics/system/overview")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync(
            "/api/v1/admin/statistics/system/alerts")).StatusCode);

        var acknowledge = await _client.PostAsJsonAsync(
            $"/api/v1/admin/statistics/system/alerts/{Guid.CreateVersion7()}/acknowledge", new { });
        Assert.Equal(HttpStatusCode.Unauthorized, acknowledge.StatusCode);
    }

    [Fact]
    public async Task BoutiqueOwner_Returns403()
    {
        var suffix = Guid.NewGuid().ToString("N");
        await SeedOwnerAsync(suffix);
        var token = CreateToken($"sysstats_owner_{suffix}", orgRole: Roles.BoutiqueOwner);

        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync(
            "/api/v1/admin/statistics/system/overview", token)).StatusCode);
    }

    [Fact]
    public async Task Overview_ReturnsTheOperationsShape()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var clerkId = await SeedAdminAsync(suffix);
        var token = CreateToken(clerkId, userRole: Roles.Admin);

        var response = await GetAsync("/api/v1/admin/statistics/system/overview", token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        Assert.True(body.TryGetProperty("version", out var version));
        Assert.True(version.TryGetProperty("gitSha", out _));
        Assert.True(body.TryGetProperty("readiness", out var readiness));
        Assert.True(readiness.TryGetProperty("status", out _));
        Assert.True(body.TryGetProperty("uptimeSeconds", out _));
        Assert.True(body.TryGetProperty("alerts", out var alerts));
        Assert.True(alerts.TryGetProperty("critical", out _));
        Assert.True(alerts.TryGetProperty("top", out _));
        Assert.True(body.TryGetProperty("throughput", out _));
        Assert.True(body.TryGetProperty("errors", out _));
        Assert.True(body.TryGetProperty("queues", out _));
        Assert.True(body.TryGetProperty("generatedAt", out _));
    }

    [Fact]
    public async Task Metrics_ReturnsOneNamedSeriesWithPoints()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var clerkId = await SeedAdminAsync(suffix);
        var token = CreateToken(clerkId, userRole: Roles.Admin);

        var metric = $"aveline.test.metric_{suffix}";
        var now = DateTime.UtcNow;
        await SeedSampleAsync(metric, 7m, now.AddMinutes(-2));

        var response = await GetAsync(
            $"/api/v1/admin/statistics/system/metrics?metric={metric}" +
            $"&from={now.AddHours(-1):O}&to={now.AddMinutes(1):O}&windowSize=instant",
            token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal(metric, body.GetProperty("metric").GetString());
        var points = body.GetProperty("points").EnumerateArray().ToList();
        Assert.Single(points);
        Assert.Equal(7m, points[0].GetProperty("value").GetDecimal());
        Assert.True(body.TryGetProperty("dataQuality", out _));
    }

    [Fact]
    public async Task Alerts_FilterByStatusAndSeverity()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var clerkId = await SeedAdminAsync(suffix);
        var token = CreateToken(clerkId, userRole: Roles.Admin);

        var metric = $"aveline.test.alert_{suffix}";
        var firingId = await SeedAlertAsync(
            metric, AlertSeverity.Critical, AlertStatus.Firing, DateTime.UtcNow.AddMinutes(-1));
        await SeedAlertAsync(
            metric, AlertSeverity.Warning, AlertStatus.Resolved, DateTime.UtcNow.AddHours(-2));

        var response = await GetAsync(
            "/api/v1/admin/statistics/system/alerts?status=Firing&severity=Critical&page=1&pageSize=50",
            token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        Assert.True(body.TryGetProperty("items", out var items));
        Assert.True(body.TryGetProperty("page", out _));
        Assert.True(body.TryGetProperty("pageSize", out _));
        Assert.True(body.TryGetProperty("total", out _));
        Assert.Contains(
            items.EnumerateArray(),
            item => item.GetProperty("id").GetGuid() == firingId);
        Assert.All(items.EnumerateArray(), item =>
        {
            Assert.Equal("Firing", item.GetProperty("status").GetString());
            Assert.Equal("Critical", item.GetProperty("severity").GetString());
        });
    }

    [Fact]
    public async Task Acknowledge_SetsTheStatus()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var clerkId = await SeedAdminAsync(suffix);
        var token = CreateToken(clerkId, userRole: Roles.Admin);

        var alertId = await SeedAlertAsync(
            $"aveline.test.ack_{suffix}", AlertSeverity.Warning, AlertStatus.Firing, DateTime.UtcNow);

        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/admin/statistics/system/alerts/{alertId}/acknowledge")
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
            Content = JsonContent.Create(new { note = "acknowledged in test" }),
        };

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("Acknowledged", body.GetProperty("status").GetString());

        await using var context = Context();
        var alert = await context.SystemAlerts.SingleAsync(candidate => candidate.Id == alertId);
        Assert.Equal(AlertStatus.Acknowledged, alert.Status);
        Assert.NotNull(alert.AcknowledgedAt);
    }

    [Fact]
    public async Task Acknowledge_UnknownAlertReturns404()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var clerkId = await SeedAdminAsync(suffix);
        var token = CreateToken(clerkId, userRole: Roles.Admin);

        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/admin/statistics/system/alerts/{Guid.CreateVersion7()}/acknowledge")
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
            Content = JsonContent.Create(new { }),
        };

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Acknowledge_ResolvedAlertReturns409_AndLeavesTheRowUnchanged()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var clerkId = await SeedAdminAsync(suffix);
        var token = CreateToken(clerkId, userRole: Roles.Admin);

        var alertId = await SeedAlertAsync(
            $"aveline.test.ack_resolved_{suffix}", AlertSeverity.Warning, AlertStatus.Resolved, DateTime.UtcNow);

        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/admin/statistics/system/alerts/{alertId}/acknowledge")
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
            Content = JsonContent.Create(new { note = "must not apply" }),
        };

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("message").GetString()));

        await using var context = Context();
        var alert = await context.SystemAlerts.SingleAsync(candidate => candidate.Id == alertId);
        Assert.Equal(AlertStatus.Resolved, alert.Status);
        Assert.Null(alert.AcknowledgedAt);
    }

    [Theory]
    [InlineData("/api/v1/admin/statistics/system/metrics")]
    [InlineData("/api/v1/admin/statistics/system/metrics?metric=aveline.process.thread_count&windowSize=fortnight")]
    [InlineData("/api/v1/admin/statistics/system/metrics?metric=aveline.process.thread_count&from=2026-09-12T00:00:00Z&to=2026-09-11T00:00:00Z")]
    [InlineData("/api/v1/admin/statistics/system/alerts?severity=Bogus")]
    [InlineData("/api/v1/admin/statistics/system/alerts?status=Bogus")]
    [InlineData("/api/v1/admin/statistics/system/errors?groupBy=week")]
    public async Task InvalidQueryValues_Return400WithMessage(string path)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var clerkId = await SeedAdminAsync(suffix);
        var token = CreateToken(clerkId, userRole: Roles.Admin);

        var response = await GetAsync(path, token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.True(body.TryGetProperty("message", out var message));
        Assert.False(string.IsNullOrWhiteSpace(message.GetString()));
    }
}
