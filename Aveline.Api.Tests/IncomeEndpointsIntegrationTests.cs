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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Aveline.Api.Tests;

/// <summary>
/// T3 read half, over the wire: <c>GET …/income/ledger</c> and <c>GET …/income/accounts</c>.
///
/// The two things this suite exists to pin are the authorization shape (<c>reports:view</c> via the
/// org-scoped policy, so a cross-organization caller is refused) and the honesty of the response
/// (the two bases reported separately, a capped window echoed rather than silently clamped, and an
/// unknown filter rejected rather than ignored).
/// </summary>
public class IncomeEndpointsIntegrationTests : IAsyncLifetime
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

    /// <summary>
    /// Seeds one boutique with a manager (who holds <c>reports:view</c>) and a staff member (who does
    /// not), plus a counter sale and a derived order settlement so the two bases are both present.
    /// </summary>
    private static async Task<(Guid OrgId, string ManagerClerk, string StaffClerk)> SeedAsync(
        string suffix, bool withLedger = true)
    {
        await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "AvelineInMemoryDb")
            .Options);

        var manager = new User
        {
            Id = Guid.CreateVersion7(), ClerkId = $"inc_manager_{suffix}",
            Email = $"inc_manager_{suffix}@aveline.lk", FirstName = "M", LastName = "G",
            Username = $"inc_manager_{suffix}", UserRole = Roles.Staff,
            OrganizationRole = string.Empty, HasCompletedOnboarding = true,
            AccountState = AccountState.Active,
        };
        var staff = new User
        {
            Id = Guid.CreateVersion7(), ClerkId = $"inc_staff_{suffix}",
            Email = $"inc_staff_{suffix}@aveline.lk", FirstName = "S", LastName = "T",
            Username = $"inc_staff_{suffix}", UserRole = Roles.Staff,
            OrganizationRole = string.Empty, HasCompletedOnboarding = true,
            AccountState = AccountState.Active,
        };
        context.Users.AddRange(manager, staff);
        await context.SaveChangesAsync();

        var org = new Organization
        {
            Name = $"Income Endpoints {suffix}", Slug = $"inc-{suffix}", OwnerUserId = manager.Id,
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

        if (withLedger)
        {
            var ledger = new BoutiqueSaleLedgerService(context, NullLogger<BoutiqueSaleLedgerService>.Instance);
            await ledger.RecordAsync(new RecordBoutiqueSaleCommand(
                org.Id, 10000m, "Counter sale of a silk saree today.",
                BoutiqueSaleEntryKind.Sale, BoutiqueSaleChargeBasis.Verified,
                BoutiqueSaleSourceKind.CounterWalkIn, "interaction:seed-1",
                DateTime.UtcNow.AddDays(-1), manager.Id));
            await ledger.RecordAsync(new RecordBoutiqueSaleCommand(
                org.Id, 30000m, "Order reached a paid status; billed value.",
                BoutiqueSaleEntryKind.Sale, BoutiqueSaleChargeBasis.Derived,
                BoutiqueSaleSourceKind.OrderSettlement, "order:seed-1",
                DateTime.UtcNow.AddDays(-1), null));
        }

        return (org.Id, manager.ClerkId, staff.ClerkId);
    }

    [Fact]
    public async Task GetLedger_WithoutAToken_Returns401()
    {
        var response = await _client.GetAsync(
            $"/api/v1/orgs/{Guid.CreateVersion7()}/income/ledger");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetLedger_AsAManager_ReturnsTheRegisterWithBothBasesSeparate()
    {
        var seeded = await SeedAsync("manager");
        var token = CreateToken(seeded.ManagerClerk, orgRole: Roles.BoutiqueManager);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Get, $"/api/v1/orgs/{seeded.OrgId}/income/ledger", token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var reconciliation = body.GetProperty("reconciliation");
        Assert.Equal(10000m, reconciliation.GetProperty("verifiedTotal").GetDecimal());
        Assert.Equal(30000m, reconciliation.GetProperty("derivedTotal").GetDecimal());
        Assert.Equal(30000m, reconciliation.GetProperty("unverifiedGap").GetDecimal());
        Assert.False(reconciliation.GetProperty("isReconciled").GetBoolean());
        // Every response carries a data-quality block.
        Assert.True(body.GetProperty("dataQuality").TryGetProperty("paymentRowsPresent", out _));
    }

    [Fact]
    public async Task GetLedger_AsStaff_Returns403()
    {
        // Staff hold no `reports:view`: the full register, with its margin-relevant splits, is the
        // management view. Their reduced takings card is a separate route.
        var seeded = await SeedAsync("staff");
        var token = CreateToken(seeded.StaffClerk, orgRole: Roles.BoutiqueStaff);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Get, $"/api/v1/orgs/{seeded.OrgId}/income/ledger", token));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetLedger_ForAnotherOrganizationsIncome_Returns403()
    {
        var mine = await SeedAsync("mine");
        var theirs = await SeedAsync("theirs");
        var token = CreateToken(mine.ManagerClerk, orgRole: Roles.BoutiqueManager);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Get, $"/api/v1/orgs/{theirs.OrgId}/income/ledger", token));

        // The org-scoped policy matches the route value against the database membership, so a
        // still-valid token carrying another org's claim cannot cross tenants.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetLedger_WithAnUnknownKind_Returns400()
    {
        var seeded = await SeedAsync("badkind");
        var token = CreateToken(seeded.ManagerClerk, orgRole: Roles.BoutiqueManager);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Get,
            $"/api/v1/orgs/{seeded.OrgId}/income/ledger?kind=Nonsense",
            token));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetLedger_WithAnUnknownBasis_Returns400()
    {
        var seeded = await SeedAsync("badbasis");
        var token = CreateToken(seeded.ManagerClerk, orgRole: Roles.BoutiqueManager);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Get,
            $"/api/v1/orgs/{seeded.OrgId}/income/ledger?basis=Maybe",
            token));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetLedger_WithAWindowOverTheCap_EchoesTheCapInsteadOfClamping()
    {
        var seeded = await SeedAsync("cap");
        var token = CreateToken(seeded.ManagerClerk, orgRole: Roles.BoutiqueManager);
        var from = DateTime.UtcNow.AddDays(-500).ToString("O");
        var to = DateTime.UtcNow.ToString("O");

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Get,
            $"/api/v1/orgs/{seeded.OrgId}/income/ledger?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}",
            token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("windowCapped").GetBoolean());
    }

    [Fact]
    public async Task GetLedger_ClampsAnOversizedPageSize()
    {
        var seeded = await SeedAsync("page");
        var token = CreateToken(seeded.ManagerClerk, orgRole: Roles.BoutiqueManager);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Get,
            $"/api/v1/orgs/{seeded.OrgId}/income/ledger?pageSize=5000",
            token));

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(200, body.GetProperty("pageSize").GetInt32());
    }

    [Fact]
    public async Task GetLedger_OnABoutiqueWithNoEntries_IsAnHonestEmptyRegister()
    {
        var seeded = await SeedAsync("empty", withLedger: false);
        var token = CreateToken(seeded.ManagerClerk, orgRole: Roles.BoutiqueManager);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Get, $"/api/v1/orgs/{seeded.OrgId}/income/ledger", token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, body.GetProperty("total").GetInt32());
        // `paymentRowsPresent` is false, which is the single most likely reason a real shop's cash
        // figures read zero — stated rather than left for the owner to interpret.
        Assert.False(body.GetProperty("dataQuality").GetProperty("paymentRowsPresent").GetBoolean());
    }

    [Fact]
    public async Task GetAccounts_AsAManager_ReturnsOneTotalPerKind()
    {
        var seeded = await SeedAsync("accounts");
        var token = CreateToken(seeded.ManagerClerk, orgRole: Roles.BoutiqueManager);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Get, $"/api/v1/orgs/{seeded.OrgId}/income/accounts", token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var sale = body.GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("kind").GetString() == "Sale");
        Assert.Equal(40000m, sale.GetProperty("total").GetDecimal());
        Assert.Equal(2, sale.GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task GetAccounts_AsStaff_Returns403()
    {
        var seeded = await SeedAsync("accounts-staff");
        var token = CreateToken(seeded.StaffClerk, orgRole: Roles.BoutiqueStaff);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Get, $"/api/v1/orgs/{seeded.OrgId}/income/accounts", token));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task TheIncomeRoutes_AreNotReachableWithTheInternalTokenScheme()
    {
        var seeded = await SeedAsync("internal");
        var request = new HttpRequestMessage(
            HttpMethod.Get, $"/api/v1/orgs/{seeded.OrgId}/income/ledger");
        request.Headers.Add("X-Internal-Token", "not-a-real-token");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
