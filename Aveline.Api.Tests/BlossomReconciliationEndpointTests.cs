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
/// Revenue Ledger R4 (issue #345) — S-56, the cross-org drift read.
///
/// Drift is already a Critical alert: `SystemMetricCollector` emits
/// `aveline.blossom.reconciliation.drift`, which `blossom.ledger.drift` watches (S-3, S-30). That
/// alarm is only actionable if an operator can find *which* account drifted, and until now the only
/// place to look was Grafana. This endpoint puts the per-account figure in the console.
///
/// The formula is deliberately **not** reimplemented: it calls the same
/// `BlossomService.LedgerDerivedBalance` and `ReconciliationDrift` the collector does, because a
/// second derivation would let the console and the alarm disagree about the same account.
/// </summary>
public class BlossomReconciliationEndpointTests : IAsyncLifetime
{
    private const string Path = "/api/v1/admin/statistics/billing/reconciliation";

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
            });

        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _authServer.DisposeAsync();
    }

    private static AppDbContext Context() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "AvelineInMemoryDb")
            .Options);

    private string CreateToken(string clerkId, string? userRole = null)
    {
        var claims = new List<Claim> { new("sub", clerkId) };
        if (userRole is not null) claims.Add(new Claim("user_role", userRole));

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _authServer.BaseUrl,
            Audience = "aveline-api",
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(_signingKey, SecurityAlgorithms.RsaSha256),
        });
    }

    private HttpRequestMessage Authorized(string token, Guid? organizationId = null)
    {
        var path = organizationId is null ? Path : $"{Path}?organizationId={organizationId}";
        return new HttpRequestMessage(HttpMethod.Get, path)
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
        };
    }

    private static async Task<string> SeedTeamUserAsync(string suffix, string role)
    {
        var clerkId = $"recon_{role}_{suffix}";
        await using var context = Context();
        context.Users.Add(new User
        {
            Id = Guid.CreateVersion7(), ClerkId = clerkId, Email = $"{clerkId}@aveline.lk",
            FirstName = "Recon", LastName = role, Username = clerkId, UserRole = role,
            OrganizationRole = string.Empty, HasCompletedOnboarding = true,
            AccountState = AccountState.Active,
        });
        await context.SaveChangesAsync();
        return clerkId;
    }

    /// <summary>
    /// Seeds an account whose projection deliberately disagrees with its ledger by
    /// <paramref name="drift"/>, which is exactly the condition the alert fires on.
    /// </summary>
    private static async Task<Guid> SeedDriftedAccountAsync(string suffix, decimal drift)
    {
        await using var context = Context();
        var ownerId = Guid.CreateVersion7();
        context.Users.Add(new User
        {
            Id = ownerId, ClerkId = $"recon_owner_{suffix}", Email = $"recon_{suffix}@aveline.lk",
            FirstName = "Recon", LastName = "Owner", Username = $"recon_owner_{suffix}",
            UserRole = Roles.BoutiqueOwner, OrganizationRole = Roles.BoutiqueOwner,
            HasCompletedOnboarding = true, AccountState = AccountState.Active,
        });
        var org = new Organization
        {
            Name = $"Recon {suffix}", Slug = $"recon-{suffix}", OwnerUserId = ownerId,
        };
        context.Organizations.Add(org);
        await context.SaveChangesAsync();

        var periodStart = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var account = new UsageAccount
        {
            OrganizationId = org.Id,
            PeriodStart = periodStart,
            PeriodEnd = periodStart.AddMonths(1),
            MonthlyBlossomLimit = 1000m,
            BlossomGranted = 0m,
            BlossomAdjusted = 0m,
            BlossomUsed = 0m,
            // `LedgerDerivedBalance = limit + (nonPeriodAllocation deltas) - used`, and
            // `drift = remaining - ledgerDerivedBalance`. So remaining = limit + drift produces
            // exactly that drift against an empty ledger.
            BlossomRemaining = 1000m + drift,
            PlanTierSnapshot = PlanTier.Bloom,
            Status = UsageAccountStatus.Active,
        };
        context.UsageAccounts.Add(account);
        await context.SaveChangesAsync();
        return org.Id;
    }

    [Fact]
    public async Task WithoutAToken_Is401()
    {
        var response = await _client.GetAsync(Path);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// `stats:system`, matching the rest of the billing-statistics family. A `moderator` reads
    /// revenue through `revenue:read` and does not read system statistics, and this is the latter.
    /// </summary>
    [Fact]
    public async Task AModerator_Is403()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var clerkId = await SeedTeamUserAsync(suffix, Roles.Moderator);
        var token = CreateToken(clerkId, Roles.Moderator);

        var response = await _client.SendAsync(Authorized(token));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AnOwner_ReadsDriftedAccountsRankedByMagnitude()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var clerkId = await SeedTeamUserAsync(suffix, Roles.Owner);
        var token = CreateToken(clerkId, Roles.Owner);
        var small = await SeedDriftedAccountAsync($"s{suffix}", drift: 5m);
        var large = await SeedDriftedAccountAsync($"l{suffix}", drift: -40m);
        await SeedDriftedAccountAsync($"clean{suffix}", drift: 0m);

        // Scoped to the first account: the integration host shares one in-memory database across
        // tests, so an unscoped read would count another test's drifted accounts.
        var response = await _client.SendAsync(Authorized(token, small));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var rows = body.RootElement.GetProperty("accounts").EnumerateArray().ToList();

        Assert.Equal(small, body.RootElement.GetProperty("organizationId").GetGuid());
        var row = Assert.Single(rows);
        Assert.Equal(small, row.GetProperty("organizationId").GetGuid());
        Assert.Equal(5m, row.GetProperty("drift").GetDecimal());
        Assert.False(row.GetProperty("isConsistent").GetBoolean());
        Assert.Equal(1, body.RootElement.GetProperty("driftedCount").GetInt32());

        // And the global read ranks by magnitude, so the worst is first regardless of sign.
        var global = await _client.SendAsync(Authorized(token));
        using var globalBody = JsonDocument.Parse(await global.Content.ReadAsStringAsync());
        var ranked = globalBody.RootElement.GetProperty("accounts").EnumerateArray().ToList();
        var largeIndex = ranked.FindIndex(r => r.GetProperty("organizationId").GetGuid() == large);
        var smallIndex = ranked.FindIndex(r => r.GetProperty("organizationId").GetGuid() == small);
        Assert.True(largeIndex >= 0 && smallIndex >= 0);
        Assert.True(largeIndex < smallIndex, "the -40 drift must rank above the +5 drift");

        // `null`, not `false`: this read does not run the collector, so it does not know whether a
        // full reconciliation pass has happened. Saying `false` would claim it checked and found
        // nothing, which is a different and unearned statement.
        Assert.Equal(
            JsonValueKind.Null,
            body.RootElement.GetProperty("reconciliationChecked").ValueKind);
        Assert.Contains(
            body.RootElement.GetProperty("notes").EnumerateArray(),
            note => note.GetString()!.Contains("drift", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AConsistentAccount_IsReportedAsConsistent()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var clerkId = await SeedTeamUserAsync(suffix, Roles.Owner);
        var token = CreateToken(clerkId, Roles.Owner);
        var orgId = await SeedDriftedAccountAsync(suffix, drift: 0m);

        var response = await _client.SendAsync(Authorized(token, orgId));

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var rows = body.RootElement.GetProperty("accounts").EnumerateArray().ToList();

        // The clean account is absent, because the list is a drift report.
        Assert.Empty(rows);
        Assert.Equal(0, body.RootElement.GetProperty("driftedCount").GetInt32());
        // It was still checked, which is the difference between "consistent" and "not looked at".
        Assert.Equal(1, body.RootElement.GetProperty("accountsChecked").GetInt32());
    }
}
