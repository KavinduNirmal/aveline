using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Aveline.Api.Authorization;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Commerce.Models;
using Aveline.Api.Modules.Commerce.Services;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Aveline.Api.Tests;

/// <summary>
/// T4, over the wire. The two things this suite exists to pin:
///
/// 1. **The role split.** The full KPI strip requires `reports:view`; the reduced takings card is
///    member-level, because every boutique role may see two labelled figures. A staff member must
///    get a 200 from `/takings` and a 403 from `/summary`.
/// 2. **Cross-tenant refusal**, because every route is org-scoped and the policy resolves the
///    membership from the database rather than the token's claims.
/// </summary>
public class TenantDashboardEndpointsTests : IAsyncLifetime
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
            });

        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
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

    private HttpRequestMessage Authorized(HttpMethod method, string path, string token)
        => new(method, path) { Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) } };

    private static async Task<(Guid OrgId, string ManagerClerk, string StaffClerk)> SeedAsync(string suffix)
    {
        await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: TestDatabase.Name())
            .Options);

        var manager = new User
        {
            Id = Guid.CreateVersion7(), ClerkId = $"dash_manager_{suffix}",
            Email = $"dash_manager_{suffix}@aveline.lk", FirstName = "M", LastName = "G",
            Username = $"dash_manager_{suffix}", UserRole = Roles.Staff,
            OrganizationRole = string.Empty, HasCompletedOnboarding = true,
            AccountState = AccountState.Active,
        };
        var staff = new User
        {
            Id = Guid.CreateVersion7(), ClerkId = $"dash_staff_{suffix}",
            Email = $"dash_staff_{suffix}@aveline.lk", FirstName = "S", LastName = "T",
            Username = $"dash_staff_{suffix}", UserRole = Roles.Staff,
            OrganizationRole = string.Empty, HasCompletedOnboarding = true,
            AccountState = AccountState.Active,
        };
        context.Users.AddRange(manager, staff);
        await context.SaveChangesAsync();

        var org = new Organization
        {
            Name = $"Dashboard {suffix}", Slug = $"dash-{suffix}", OwnerUserId = manager.Id,
        };
        context.Organizations.Add(org);
        await context.SaveChangesAsync();

        context.OrganizationMemberships.AddRange(
            new OrganizationMembership
            {
                OrganizationId = org.Id, UserId = manager.Id,
                BoutiqueRole = Roles.BoutiqueManager, Status = MembershipStatus.Active,
            },
            new OrganizationMembership
            {
                OrganizationId = org.Id, UserId = staff.Id,
                BoutiqueRole = Roles.BoutiqueStaff, Status = MembershipStatus.Active,
            });
        await context.SaveChangesAsync();

        context.Orders.Add(new Order
        {
            Id = Guid.CreateVersion7(), OrganizationId = org.Id,
            CustomerId = Guid.CreateVersion7(), CustomerName = "Dash Client",
            Status = "completed", Subtotal = 10000m, Discount = 0m, Total = 10000m,
            TotalCost = 6000m, CreatedAt = DateTime.UtcNow.AddDays(-1),
        });
        await context.SaveChangesAsync();

        var ledger = new BoutiqueSaleLedgerService(context, NullLogger<BoutiqueSaleLedgerService>.Instance);
        await ledger.RecordAsync(new RecordBoutiqueSaleCommand(
            org.Id, 10000m, "Counter sale of a silk saree today.",
            BoutiqueSaleEntryKind.Sale, BoutiqueSaleChargeBasis.Verified,
            BoutiqueSaleSourceKind.CounterWalkIn, "interaction:dash-1",
            DateTime.UtcNow.AddDays(-1), manager.Id));

        return (org.Id, manager.ClerkId, staff.ClerkId);
    }

    // ── the role split ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Summary_AsAManager_ReturnsTheKpiStrip()
    {
        var seeded = await SeedAsync("summary");
        var token = CreateToken(seeded.ManagerClerk, orgRole: Roles.BoutiqueManager);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Get, $"/api/v1/orgs/{seeded.OrgId}/dashboard/summary?window=30d", token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(10000m, body.GetProperty("sales").GetProperty("grossOrderValue").GetDecimal());
        Assert.True(body.GetProperty("dataQuality").TryGetProperty("paymentRowsPresent", out _));
    }

    [Fact]
    public async Task Summary_AsStaff_Returns403()
    {
        // The full strip carries margin and top items, so it is the management view.
        var seeded = await SeedAsync("summary-staff");
        var token = CreateToken(seeded.StaffClerk, orgRole: Roles.BoutiqueStaff);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Get, $"/api/v1/orgs/{seeded.OrgId}/dashboard/summary", token));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Takings_AsStaff_ReturnsTheReducedTwoFigureRead()
    {
        var seeded = await SeedAsync("takings-staff");
        var token = CreateToken(seeded.StaffClerk, orgRole: Roles.BoutiqueStaff);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Get, $"/api/v1/orgs/{seeded.OrgId}/dashboard/takings", token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(10000m, body.GetProperty("collected").GetDecimal());
        Assert.True(body.TryGetProperty("billedUnconfirmed", out _));
        // The reduced read carries no margin and no series.
        Assert.False(body.TryGetProperty("sales", out _));
        Assert.False(body.TryGetProperty("points", out _));
    }

    // ── cross-tenant ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Summary_ForAnotherOrganization_Returns403()
    {
        var mine = await SeedAsync("mine");
        var theirs = await SeedAsync("theirs");
        var token = CreateToken(mine.ManagerClerk, orgRole: Roles.BoutiqueManager);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Get, $"/api/v1/orgs/{theirs.OrgId}/dashboard/summary", token));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Takings_ForAnotherOrganization_Returns403()
    {
        var mine = await SeedAsync("takings-mine");
        var theirs = await SeedAsync("takings-theirs");
        var token = CreateToken(mine.StaffClerk, orgRole: Roles.BoutiqueStaff);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Get, $"/api/v1/orgs/{theirs.OrgId}/dashboard/takings", token));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── validation and the other two reports ────────────────────────────────────────────────────

    [Fact]
    public async Task Summary_WithAnUnknownWindow_Returns400()
    {
        var seeded = await SeedAsync("badwindow");
        var token = CreateToken(seeded.ManagerClerk, orgRole: Roles.BoutiqueManager);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Get, $"/api/v1/orgs/{seeded.OrgId}/dashboard/summary?window=forever", token));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RevenueSeries_AsAManager_ReturnsDenseBuckets()
    {
        var seeded = await SeedAsync("series");
        var token = CreateToken(seeded.ManagerClerk, orgRole: Roles.BoutiqueManager);
        var from = DateTime.UtcNow.AddDays(-7).ToString("O");
        var to = DateTime.UtcNow.ToString("O");

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Get,
            $"/api/v1/orgs/{seeded.OrgId}/dashboard/revenue-series?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}&bucket=day",
            token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("points").GetArrayLength() >= 7);
    }

    [Fact]
    public async Task TopItems_AsAManager_ReturnsTheClampedLimit()
    {
        var seeded = await SeedAsync("top");
        var token = CreateToken(seeded.ManagerClerk, orgRole: Roles.BoutiqueManager);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Get, $"/api/v1/orgs/{seeded.OrgId}/dashboard/top-items?limit=500", token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(20, body.GetProperty("limit").GetInt32());
    }

    [Fact]
    public async Task WithoutAToken_EveryDashboardRouteReturns401()
    {
        var orgId = Guid.CreateVersion7();

        foreach (var path in new[]
                 {
                     "dashboard/summary", "dashboard/takings",
                     "dashboard/revenue-series", "dashboard/top-items",
                 })
        {
            var response = await _client.GetAsync($"/api/v1/orgs/{orgId}/{path}");
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }
}
