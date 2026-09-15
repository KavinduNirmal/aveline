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
/// Issue #224 — quota enforcement: with <c>Quotas:EnforcementEnabled=true</c> an exhausted
/// meter returns 429 with the documented body; with the flag off (the default) the request
/// proceeds regardless of the limit.
/// </summary>
public class ApiQuotaEnforcementTests : IAsyncLifetime
{
    private RsaSecurityKey _signingKey = null!;
    private StubAuthServer _authServer = null!;
    private WebApplicationFactory<Program> _enforcingFactory = null!;
    private HttpClient _enforcingClient = null!;

    public async Task InitializeAsync()
    {
        _signingKey = new RsaSecurityKey(RSA.Create(2048)) { KeyId = "test-kid" };
        _authServer = new StubAuthServer(_signingKey);
        await _authServer.StartAsync();

        _enforcingFactory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Clerk:Authority", _authServer.BaseUrl);
                builder.UseSetting("Clerk:RequireHttpsMetadata", "false");
                builder.UseSetting("Telemetry:Enabled", "false");
                builder.UseSetting("Quotas:EnforcementEnabled", "true");
            });

        _enforcingClient = _enforcingFactory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _enforcingClient.Dispose();
        await _enforcingFactory.DisposeAsync();
        await _authServer.DisposeAsync();
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

    private Task<HttpResponseMessage> GetAsync(HttpClient client, string path, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path)
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
        };
        return client.SendAsync(request);
    }

    private static async Task<(Guid OrgId, string ClerkId)> SeedOwnerWithLimitAsync(
        string suffix, decimal monthlyLimit)
    {
        await using var context = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(databaseName: "AvelineInMemoryDb")
                .Options);

        var owner = new User
        {
            Id = Guid.CreateVersion7(),
            ClerkId = $"quota_owner_{suffix}",
            Email = $"quota_{suffix}@aveline.lk",
            FirstName = "Quota",
            LastName = "Owner",
            Username = $"quota_{suffix}",
            UserRole = Roles.Staff,
            OrganizationRole = string.Empty,
            HasCompletedOnboarding = true,
            AccountState = AccountState.Active,
        };
        context.Users.Add(owner);

        var org = new Organization
        {
            Name = $"Quota Org {suffix}",
            Slug = $"quota-{suffix}",
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

        context.PlanEntitlementOverrides.Add(new PlanEntitlementOverride
        {
            OrganizationId = org.Id,
            Key = "api.requests.monthly",
            ValueType = EntitlementValueType.Integer,
            ValueDecimal = monthlyLimit,
            EffectiveFrom = DateTime.UtcNow.AddDays(-1),
            Reason = "integration test",
            CreatedByUserId = owner.Id,
        });

        await context.SaveChangesAsync();
        return (org.Id, owner.ClerkId);
    }

    [Fact]
    public async Task ExhaustedQuota_Returns429WithTheDocumentedBody()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var (orgId, clerkId) = await SeedOwnerWithLimitAsync(suffix, monthlyLimit: 1);
        var token = CreateToken(clerkId, orgRole: Roles.BoutiqueOwner);

        var response = await GetAsync(
            _enforcingClient, $"/api/v1/orgs/{orgId}/statistics/api/requests", token);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("message").GetString()));

        var quota = body.GetProperty("quota");
        Assert.Equal("api.requests.monthly", quota.GetProperty("metricKey").GetString());
        Assert.Equal(1, quota.GetProperty("limit").GetInt64());
        Assert.Equal(1, quota.GetProperty("used").GetInt64());
        Assert.Equal(JsonValueKind.String, quota.GetProperty("resetsAt").ValueKind);
    }

    [Fact]
    public async Task ExhaustedQuota_IsAllowedWhenEnforcementIsDisabled()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var (orgId, clerkId) = await SeedOwnerWithLimitAsync(suffix, monthlyLimit: 1);
        var token = CreateToken(clerkId, orgRole: Roles.BoutiqueOwner);

        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Clerk:Authority", _authServer.BaseUrl);
                builder.UseSetting("Clerk:RequireHttpsMetadata", "false");
                builder.UseSetting("Telemetry:Enabled", "false");
                builder.UseSetting("Quotas:EnforcementEnabled", "false");
            });
        using var client = factory.CreateClient();

        var response = await GetAsync(
            client, $"/api/v1/orgs/{orgId}/statistics/api/requests", token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
