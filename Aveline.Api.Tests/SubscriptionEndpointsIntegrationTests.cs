using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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
/// Issue #196 — subscription, plan change and entitlement endpoints
/// (docs/api/README.md §C.3).
/// </summary>
public class SubscriptionEndpointsIntegrationTests : IAsyncLifetime
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

    private HttpRequestMessage Authorized(
        HttpMethod method, string path, string token, object? body = null, string? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(method, path)
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
        };

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        return request;
    }

    private static async Task<(Guid OrgId, string OwnerClerk)> SeedBoutiqueAsync(
        string suffix, PlanTier tier = PlanTier.Seed, decimal blossomUsed = 0m, decimal limit = 150m)
    {
        await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "AvelineInMemoryDb")
            .Options);

        var owner = new User
        {
            Id = Guid.CreateVersion7(),
            ClerkId = $"sub_owner_{suffix}",
            Email = $"sub_{suffix}@aveline.lk",
            FirstName = "Sub",
            LastName = "Owner",
            Username = $"sub_owner_{suffix}",
            UserRole = Roles.Staff,
            OrganizationRole = string.Empty,
            HasCompletedOnboarding = true,
            AccountState = AccountState.Active,
        };
        context.Users.Add(owner);
        await context.SaveChangesAsync();

        var org = new Organization
        {
            Name = $"Sub Org {suffix}",
            Slug = $"sub-{suffix}",
            OwnerUserId = owner.Id,
            PlanTier = tier,
        };
        context.Organizations.Add(org);

        context.OrganizationMemberships.Add(new OrganizationMembership
        {
            OrganizationId = org.Id,
            UserId = owner.Id,
            BoutiqueRole = Roles.BoutiqueOwner,
            Status = MembershipStatus.Active,
        });

        if (blossomUsed > 0)
        {
            var now = DateTime.UtcNow;
            var periodStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            context.UsageAccounts.Add(new UsageAccount
            {
                OrganizationId = org.Id,
                PeriodStart = periodStart,
                PeriodEnd = periodStart.AddMonths(1),
                MonthlyBlossomLimit = limit,
                BlossomUsed = blossomUsed,
                BlossomRemaining = limit - blossomUsed,
            });
        }

        await context.SaveChangesAsync();
        return (org.Id, owner.ClerkId);
    }

    [Fact]
    public async Task GetSubscription_WithNoRow_ReturnsNoneStatus()
    {
        var (orgId, clerk) = await SeedBoutiqueAsync("none");
        var token = CreateToken(clerk, orgRole: Roles.BoutiqueOwner);

        var response = await _client.SendAsync(
            Authorized(HttpMethod.Get, $"/api/v1/orgs/{orgId}/subscription", token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("None", body.GetProperty("status").GetString());
        Assert.Equal("Seed", body.GetProperty("planTier").GetString());
    }

    [Fact]
    public async Task ChangePlan_UpgradeImmediate_ProratesTheAllowance()
    {
        var (orgId, clerk) = await SeedBoutiqueAsync("upgrade");
        var token = CreateToken(clerk, orgRole: Roles.BoutiqueOwner);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Post, $"/api/v1/orgs/{orgId}/subscription/change-plan", token,
            new { planTier = "Bloom", effective = "immediate", reason = "Upgrade for peak season." },
            "change-plan-upgrade"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(600m, body.GetProperty("blossomDelta").GetDecimal());
        Assert.Equal(750m, body.GetProperty("blossomRemaining").GetDecimal());
        Assert.Equal("Bloom", body.GetProperty("subscription").GetProperty("planTier").GetString());
    }

    [Fact]
    public async Task ChangePlan_SameTier_Returns400NoOp()
    {
        var (orgId, clerk) = await SeedBoutiqueAsync("noop");
        var token = CreateToken(clerk, orgRole: Roles.BoutiqueOwner);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Post, $"/api/v1/orgs/{orgId}/subscription/change-plan", token,
            new { planTier = "Seed", effective = "immediate" },
            "change-plan-noop"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("no-op-plan-change", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ChangePlan_ImmediateDowngradeExceedingLimits_Returns409WithViolations()
    {
        var (orgId, clerk) = await SeedBoutiqueAsync("downgrade", PlanTier.Rose, blossomUsed: 600m, limit: 5000m);
        var token = CreateToken(clerk, orgRole: Roles.BoutiqueOwner);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Post, $"/api/v1/orgs/{orgId}/subscription/change-plan", token,
            new { planTier = "Seed", effective = "immediate" },
            "change-plan-downgrade"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("plan-limit-violation", body.GetProperty("code").GetString());
        var violations = body.GetProperty("violations").EnumerateArray().ToArray();
        Assert.Contains(violations, v => v.GetProperty("key").GetString() == "blossoms.monthly");
    }

    [Fact]
    public async Task GetEntitlements_ReturnsCatalogItems()
    {
        var (orgId, clerk) = await SeedBoutiqueAsync("entitlements");
        var token = CreateToken(clerk, orgRole: Roles.BoutiqueOwner);

        var response = await _client.SendAsync(
            Authorized(HttpMethod.Get, $"/api/v1/orgs/{orgId}/entitlements", token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Contains(
            body.EnumerateArray(),
            item => item.GetProperty("key").GetString() == "blossoms.monthly"
                 && item.GetProperty("value").GetDecimal() == 150m);
    }

    [Fact]
    public async Task GetEntitlementUsage_ReturnsObservedAndAllowed()
    {
        var (orgId, clerk) = await SeedBoutiqueAsync("usage", blossomUsed: 30m, limit: 150m);
        var token = CreateToken(clerk, orgRole: Roles.BoutiqueOwner);

        var response = await _client.SendAsync(
            Authorized(HttpMethod.Get, $"/api/v1/orgs/{orgId}/entitlements/usage", token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var items = body.GetProperty("items").EnumerateArray().ToArray();
        Assert.Contains(items, i => i.GetProperty("key").GetString() == "blossoms.monthly"
                                 && i.GetProperty("observed").GetDecimal() == 30m
                                 && i.GetProperty("allowed").GetDecimal() == 150m);
        Assert.True(body.GetProperty("dataQuality").GetProperty("materialisedCounts").GetBoolean());
    }
}
