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

public class BillingStatisticsEndpointsTests : IAsyncLifetime
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

    [Fact]
    public async Task GetBurnRate_Returns200_WithCalculatedRate()
    {
        var orgId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var clerkId = $"user_burn_{Guid.NewGuid():N}";

        await using (var context = Context())
        {
            var user = new User
            {
                Id = userId,
                ClerkId = clerkId,
                Email = "owner@burn.test",
                FirstName = "Burn",
                LastName = "Owner",
                Username = $"burn_{Guid.NewGuid():N}",
                UserRole = Roles.Staff,
                HasCompletedOnboarding = true,
                AccountState = AccountState.Active,
            };
            var org = new Organization { Id = orgId, Name = "Burn Org", Slug = $"burn-{Guid.NewGuid():N}", PlanTier = PlanTier.Bloom, OwnerUserId = userId };
            var membership = new OrganizationMembership
            {
                OrganizationId = orgId,
                UserId = userId,
                BoutiqueRole = Roles.BoutiqueOwner,
                Status = MembershipStatus.Active,
            };

            context.Users.Add(user);
            context.Organizations.Add(org);
            context.OrganizationMemberships.Add(membership);
            await context.SaveChangesAsync();
        }

        var token = CreateToken(clerkId, orgRole: "owner");
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/orgs/{orgId}/billing/burn-rate?window=30d");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("30d", body.GetProperty("window").GetString());
        Assert.True(body.TryGetProperty("burnRatePerDay", out _));
    }

    [Fact]
    public async Task GetActiveCustomers_Returns200()
    {
        var orgId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var clerkId = $"user_cust_{Guid.NewGuid():N}";

        await using (var context = Context())
        {
            var user = new User
            {
                Id = userId,
                ClerkId = clerkId,
                Email = "owner@cust.test",
                FirstName = "Cust",
                LastName = "Owner",
                Username = $"cust_{Guid.NewGuid():N}",
                UserRole = Roles.Staff,
                HasCompletedOnboarding = true,
                AccountState = AccountState.Active,
            };
            var org = new Organization { Id = orgId, Name = "Cust Org", Slug = $"cust-{Guid.NewGuid():N}", PlanTier = PlanTier.Bloom, OwnerUserId = userId };
            var membership = new OrganizationMembership
            {
                OrganizationId = orgId,
                UserId = userId,
                BoutiqueRole = Roles.BoutiqueOwner,
                Status = MembershipStatus.Active,
            };

            context.Users.Add(user);
            context.Organizations.Add(org);
            context.OrganizationMemberships.Add(membership);
            await context.SaveChangesAsync();
        }

        var token = CreateToken(clerkId, orgRole: "owner");
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/orgs/{orgId}/customers/active");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(orgId.ToString(), body.GetProperty("organizationId").GetString());
        Assert.Equal(90, body.GetProperty("inactivityThresholdDays").GetInt32());
    }

    [Fact]
    public async Task GetStaffSeats_Returns200()
    {
        var orgId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var clerkId = $"user_staff_{Guid.NewGuid():N}";

        await using (var context = Context())
        {
            var user = new User
            {
                Id = userId,
                ClerkId = clerkId,
                Email = "owner@staff.test",
                FirstName = "Staff",
                LastName = "Owner",
                Username = $"staff_{Guid.NewGuid():N}",
                UserRole = Roles.Staff,
                HasCompletedOnboarding = true,
                AccountState = AccountState.Active,
            };
            var org = new Organization { Id = orgId, Name = "Staff Org", Slug = $"staff-{Guid.NewGuid():N}", PlanTier = PlanTier.Bloom, OwnerUserId = userId };
            var membership = new OrganizationMembership
            {
                OrganizationId = orgId,
                UserId = userId,
                BoutiqueRole = Roles.BoutiqueOwner,
                Status = MembershipStatus.Active,
            };

            context.Users.Add(user);
            context.Organizations.Add(org);
            context.OrganizationMemberships.Add(membership);
            await context.SaveChangesAsync();
        }

        var token = CreateToken(clerkId, orgRole: "owner");
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/orgs/{orgId}/staff/seats");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(1, body.GetProperty("activeSeats").GetInt32());
    }

    [Fact]
    public async Task AdminBilling_Profitability_RequiresAdminSystem()
    {
        var clerkId = $"user_admin_{Guid.NewGuid():N}";
        var userId = Guid.CreateVersion7();

        await using (var context = Context())
        {
            var adminUser = new User
            {
                Id = userId,
                ClerkId = clerkId,
                Email = "admin@aveline.lk",
                FirstName = "Admin",
                LastName = "User",
                Username = $"admin_{Guid.NewGuid():N}",
                UserRole = Roles.Admin,
                HasCompletedOnboarding = true,
                AccountState = AccountState.Active,
            };
            context.Users.Add(adminUser);
            await context.SaveChangesAsync();
        }

        var token = CreateToken(clerkId, userRole: Roles.Admin);

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/statistics/billing/profitability");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
