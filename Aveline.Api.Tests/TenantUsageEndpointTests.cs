using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Aveline.Api.Authorization;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Infrastructure.Integrations;
using Aveline.Api.Modules.Billing.DTOs;
using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Billing.Services;
using Aveline.Api.Modules.CustomerConcierge.Models;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Aveline.Api.Tests;

/// <summary>
/// ADR-026 — the internal tenant-account snapshot Aveline answers "how many Blossoms do I have
/// left?" from.
/// </summary>
/// <remarks>
/// The endpoint exists so the concierge can state the boutique's own figures without the agent
/// service ever touching the database. What matters is that those figures are the *same* ones the
/// boutique sees in its dashboard, so the assertions here are about provenance as much as values.
/// </remarks>
public class TenantUsageEndpointTests : IAsyncLifetime
{
    private const string InternalToken = "test-internal-token";

    private RsaSecurityKey _signingKey = null!;
    private StubAuthServer _authServer = null!;
    private WebApplicationFactory<Program> _program = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _signingKey = new RsaSecurityKey(RSA.Create(2048)) { KeyId = "test-kid" };

        _authServer = new StubAuthServer(_signingKey);
        await _authServer.StartAsync();

        _program = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Clerk:Authority", _authServer.BaseUrl);
                builder.UseSetting("Database:InMemoryName", TestDatabase.Name());
                builder.UseSetting("Clerk:RequireHttpsMetadata", "false");
                builder.UseSetting("AgentService:InternalToken", InternalToken);
                // This suite boots the real app, so the media options validator runs. Pinning the
                // provider to the `database` default keeps the boot identical on a developer
                // machine, whose environment may select Cloudinary, and in CI, which selects
                // nothing. The URL and key are the repo's 32-zero-byte test values: a valid key,
                // not a credential, and inert under this provider.
                builder.UseSetting("Media:Provider", "database");
                builder.UseSetting("Media:PublicBaseUrl", "https://media.aveline.test");
                builder.UseSetting("Media:SigningKey", Convert.ToBase64String(new byte[32]));
            });

        _client = _program.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _program.DisposeAsync();
        await _authServer.DisposeAsync();
    }

    private static AppDbContext Db() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: TestDatabase.Name())
            .Options);

    private async Task<HttpResponseMessage> GetTenantUsageAsync(Guid orgId, string? token = InternalToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/internal/usage/tenant/{orgId}");
        if (token is not null)
        {
            request.Headers.Add(InternalServiceAuthHandler.HeaderName, token);
        }

        return await _client.SendAsync(request);
    }

    /// <summary>
    /// A boutique on <paramref name="tier"/> with a period account and a known headcount.
    /// </summary>
    /// <remarks>
    /// The account is seeded with a grant, which is what makes the balance test meaningful: the
    /// period allowance is <c>limit + granted - adjusted</c>, so a remainder derived from the
    /// entitlement limit (or from <c>limit - used</c>) is a different number from the stored
    /// projection. Only reading the balance projection gets the dashboard's figure.
    /// </remarks>
    private static async Task<Guid> SeedBoutiqueAsync(
        string suffix,
        PlanTier tier = PlanTier.Bloom,
        decimal monthlyLimit = 500m,
        decimal granted = 100m,
        decimal adjusted = 0m,
        decimal used = 87.4m,
        decimal remaining = 512.6m,
        int activeMembers = 2,
        int customers = 3)
    {
        await using var context = Db();

        var owner = new User
        {
            Id = Guid.CreateVersion7(),
            ClerkId = $"tenant_owner_{suffix}",
            Email = $"tenant_{suffix}@aveline.lk",
            FirstName = "Tenant",
            LastName = "Owner",
            Username = $"tenant_owner_{suffix}",
            UserRole = Roles.Staff,
            OrganizationRole = string.Empty,
            HasCompletedOnboarding = true,
            AccountState = AccountState.Active,
        };
        context.Users.Add(owner);

        var organization = new Organization
        {
            Id = Guid.CreateVersion7(),
            Name = $"Tenant Org {suffix}",
            Slug = $"tenant-{suffix}",
            OwnerUserId = owner.Id,
            PlanTier = tier,
        };
        context.Organizations.Add(organization);

        for (var i = 0; i < activeMembers; i++)
        {
            context.OrganizationMemberships.Add(new OrganizationMembership
            {
                OrganizationId = organization.Id,
                UserId = i == 0 ? owner.Id : Guid.CreateVersion7(),
                BoutiqueRole = i == 0 ? Roles.BoutiqueOwner : Roles.BoutiqueStaff,
                Status = MembershipStatus.Active,
            });
        }

        for (var i = 0; i < customers; i++)
        {
            context.Customers.Add(new Customer
            {
                OrganizationId = organization.Id,
                PhoneNumber = $"+9477100{i:D4}",
                FullName = $"Client {i}",
                Status = "returning",
            });
        }

        var now = DateTime.UtcNow;
        var periodStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        context.UsageAccounts.Add(new UsageAccount
        {
            OrganizationId = organization.Id,
            PeriodStart = periodStart,
            PeriodEnd = periodStart.AddMonths(1),
            MonthlyBlossomLimit = monthlyLimit,
            BlossomGranted = granted,
            BlossomAdjusted = adjusted,
            BlossomUsed = used,
            BlossomRemaining = remaining,
        });

        await context.SaveChangesAsync();
        return organization.Id;
    }

    [Fact]
    public async Task TenantUsage_WithoutTheInternalToken_IsUnauthorized()
    {
        var orgId = await SeedBoutiqueAsync("unauth");

        var response = await GetTenantUsageAsync(orgId, token: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TenantUsage_ForAnUnknownOrganisation_IsNotFoundAndLeavesNoAccountBehind()
    {
        // Entitlement usage is read before the balance on purpose: the balance read creates the
        // period account on demand, so asking it about an unknown organisation would leave a stray
        // account row behind. This pins that ordering.
        var unknown = Guid.NewGuid();

        var response = await GetTenantUsageAsync(unknown);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        await using var context = Db();
        Assert.False(await context.UsageAccounts.AnyAsync(account => account.OrganizationId == unknown));
    }

    [Fact]
    public async Task TenantUsage_ReportsTheStoredBalance_NotOneDerivedFromTheEntitlementLimit()
    {
        // Period allowance is 500 + 100 granted = 600, of which 87.4 is used, so the stored
        // remainder is 512.6. A derived figure would be 750 - 87.4 = 662.6 (the Bloom entitlement
        // limit) or 500 - 87.4 = 412.6 (the bare limit). Neither is the boutique's balance.
        var orgId = await SeedBoutiqueAsync("balance");

        var response = await GetTenantUsageAsync(orgId);
        var snapshot = await response.Content.ReadFromJsonAsync<TenantUsageSnapshotDto>();

        Assert.NotNull(snapshot);
        Assert.Equal(512.6m, snapshot.Blossoms.BlossomRemaining);
        Assert.Equal(87.4m, snapshot.Blossoms.BlossomUsed);
        Assert.Equal(500m, snapshot.Blossoms.MonthlyBlossomLimit);
        Assert.Equal(100m, snapshot.Blossoms.BlossomGranted);
    }

    [Fact]
    public async Task TenantUsage_AgreesWithTheBillingSummaryTheMeterReads()
    {
        // The dashboard's Blossom meter reads the summary; Aveline reads this snapshot. Two
        // different remainders for one boutique would be a product bug, not a rounding difference.
        var orgId = await SeedBoutiqueAsync("agreement");

        var snapshot = await (await GetTenantUsageAsync(orgId))
            .Content.ReadFromJsonAsync<TenantUsageSnapshotDto>();

        using var summaryRequest = new HttpRequestMessage(HttpMethod.Get, $"/internal/usage/summary/{orgId}");
        summaryRequest.Headers.Add(InternalServiceAuthHandler.HeaderName, InternalToken);
        var summary = await (await _client.SendAsync(summaryRequest))
            .Content.ReadFromJsonAsync<UsageSummary>();

        Assert.NotNull(snapshot);
        Assert.NotNull(summary);
        Assert.Equal(summary.BlossomRemaining, snapshot.Blossoms.BlossomRemaining);
        Assert.Equal(summary.BlossomUsed, snapshot.Blossoms.BlossomUsed);
    }

    [Fact]
    public async Task TenantUsage_ReportsTheSeatAndCustomerAllowancesAgainstThePlanLimits()
    {
        var orgId = await SeedBoutiqueAsync("allowances", activeMembers: 2, customers: 3);

        var snapshot = await (await GetTenantUsageAsync(orgId))
            .Content.ReadFromJsonAsync<TenantUsageSnapshotDto>();

        Assert.NotNull(snapshot);

        Assert.Equal("staff.max", snapshot.Staff.Key);
        Assert.Equal(2m, snapshot.Staff.Used);
        Assert.Equal(3m, snapshot.Staff.Limit);
        Assert.Equal(1m, snapshot.Staff.Remaining);
        Assert.True(snapshot.Staff.IsHardLimit);

        Assert.Equal("customers.active.max", snapshot.Customers.Key);
        Assert.Equal(3m, snapshot.Customers.Used);
        Assert.Equal(250m, snapshot.Customers.Limit);
        Assert.Equal(247m, snapshot.Customers.Remaining);
        Assert.True(snapshot.Customers.IsHardLimit);

        // The count is a 90-day activity measure, not a total, and the wording travels with it so
        // an answer cannot report it as "your customers".
        Assert.Contains("90 days", snapshot.CustomerCountBasis);
    }

    [Fact]
    public async Task TenantUsage_FlagsABalanceAtOrBelowTheLowWaterLine()
    {
        // 600 * 20% = 120, so 120 remaining is low and 120.01 is not. The boundary is the stored
        // rule the clients already apply, not a fresh threshold invented here.
        var low = await SeedBoutiqueAsync("low", used: 480m, remaining: 120m);
        var fine = await SeedBoutiqueAsync("fine", used: 479.99m, remaining: 120.01m);

        var lowSnapshot = await (await GetTenantUsageAsync(low))
            .Content.ReadFromJsonAsync<TenantUsageSnapshotDto>();
        var fineSnapshot = await (await GetTenantUsageAsync(fine))
            .Content.ReadFromJsonAsync<TenantUsageSnapshotDto>();

        Assert.NotNull(lowSnapshot);
        Assert.NotNull(fineSnapshot);
        Assert.True(lowSnapshot.BlossomsAreLow);
        Assert.False(fineSnapshot.BlossomsAreLow);
    }

    [Fact]
    public async Task TenantUsage_CarriesNoOperatorDetail()
    {
        // This endpoint is for a boutique's own question, so the response must not grow the
        // operator-facing fields the admin routes carry (per-entry reasons, source references,
        // internal cost). Asserted on the raw document rather than the DTO so a new field is
        // caught here rather than assumed harmless.
        var orgId = await SeedBoutiqueAsync("shape");

        var response = await GetTenantUsageAsync(orgId);
        var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        var keys = root.EnumerateObject().Select(property => property.Name).ToHashSet();
        Assert.Equal(
            new HashSet<string>
            {
                "organizationId", "blossoms", "staff", "customers",
                "customerCountBasis", "blossomsAreLow", "asOf",
            },
            keys);

        Assert.True(root.TryGetProperty("blossoms", out var blossoms));
        Assert.False(blossoms.TryGetProperty("actualCostUsd", out _));
        Assert.False(root.TryGetProperty("ledger", out _));
    }
}
