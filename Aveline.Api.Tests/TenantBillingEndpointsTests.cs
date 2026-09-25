using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Aveline.Api.Authorization;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Analytics.Models;
using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Aveline.Api.Tests;

/// <summary>
/// T5 over the wire: the top-up pack catalogue (E-11) and the billing-period history (E-12).
///
/// Three things this suite exists to pin, none of which the unit tests can:
///
/// 1. **The authorization shape.** E-11 takes <c>billing:manage</c> — the same permission as the
///    purchase it feeds, so a manager who may *see* billing but not *buy* is refused the list. E-12
///    takes <c>billing:view</c>, so a supervisor (who holds only <c>billing:view:self</c>) is refused
///    the history. Both are org-scoped policies: a still-valid token carrying another organization's
///    claim cannot cross tenants.
/// 2. **The zero-price rule (C-4).** A period whose stored plan price is `0` reports
///    `planListPriceLkr: null` and `subscriptionPricesConfigured: false`, never `LKR 0`.
/// 3. **The active-only catalogue.** A draft price-book row is not for sale and must not be offered.
/// </summary>
public class TenantBillingEndpointsTests : IAsyncLifetime
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

    private sealed record Seeded(
        Guid OrgId, string OwnerClerk, string ManagerClerk, string SupervisorClerk);

    /// <summary>
    /// One boutique with all four boutique roles, four price-book packs (one Draft), one billing
    /// period and its subscription snapshot. The stored plan price is `0`, which is the production
    /// case: it is never assigned anywhere.
    /// </summary>
    private static async Task<Seeded> SeedAsync(string suffix)
    {
        await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "AvelineInMemoryDb")
            .Options);

        User NewUser(string role, string tag)
        {
            var user = new User
            {
                Id = Guid.CreateVersion7(),
                ClerkId = $"t5_{tag}_{suffix}",
                Email = $"t5_{tag}_{suffix}@aveline.lk",
                FirstName = tag,
                LastName = suffix,
                Username = $"t5_{tag}_{suffix}",
                UserRole = Roles.Staff,
                OrganizationRole = string.Empty,
                HasCompletedOnboarding = true,
                AccountState = AccountState.Active,
            };
            return user;
        }

        var owner = NewUser(Roles.BoutiqueOwner, "owner");
        var manager = NewUser(Roles.BoutiqueManager, "manager");
        var supervisor = NewUser(Roles.BoutiqueSupervisor, "supervisor");
        context.Users.AddRange(owner, manager, supervisor);
        await context.SaveChangesAsync();

        var org = new Organization
        {
            Name = $"Billing {suffix}",
            Slug = $"t5-{suffix}",
            OwnerUserId = owner.Id,
            PlanTier = PlanTier.Bloom,
            Currency = "LKR",
        };
        context.Organizations.Add(org);
        await context.SaveChangesAsync();

        context.OrganizationMemberships.AddRange(
            new OrganizationMembership
            {
                OrganizationId = org.Id, UserId = owner.Id,
                BoutiqueRole = Roles.BoutiqueOwner, Status = MembershipStatus.Active,
            },
            new OrganizationMembership
            {
                OrganizationId = org.Id, UserId = manager.Id,
                BoutiqueRole = Roles.BoutiqueManager, Status = MembershipStatus.Active,
            },
            new OrganizationMembership
            {
                OrganizationId = org.Id, UserId = supervisor.Id,
                BoutiqueRole = Roles.BoutiqueSupervisor, Status = MembershipStatus.Active,
            });

        var effectiveFrom = DateTime.UtcNow.AddMonths(-6);
        context.BlossomPriceEntries.AddRange(
            new BlossomPriceEntry
            {
                SkuKind = BlossomSkuKind.TopUpPack, SkuCode = $"pack-500-{suffix}",
                BlossomQuantity = 500m, PriceLkr = 2000m, Status = BlossomRuleStatus.Active,
                EffectiveFrom = effectiveFrom, CreatedByUserId = owner.Id,
            },
            new BlossomPriceEntry
            {
                SkuKind = BlossomSkuKind.TopUpPack, SkuCode = $"pack-100-{suffix}",
                BlossomQuantity = 100m, PriceLkr = 500m, Status = BlossomRuleStatus.Active,
                EffectiveFrom = effectiveFrom, CreatedByUserId = owner.Id,
            },
            new BlossomPriceEntry
            {
                SkuKind = BlossomSkuKind.TopUpPack, SkuCode = $"pack-draft-{suffix}",
                BlossomQuantity = 999m, PriceLkr = 1m, Status = BlossomRuleStatus.Draft,
                EffectiveFrom = effectiveFrom, CreatedByUserId = owner.Id,
            });

        var periodStart = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        context.UsageAccounts.Add(new UsageAccount
        {
            OrganizationId = org.Id,
            PeriodStart = periodStart,
            PeriodEnd = periodStart.AddMonths(1),
            MonthlyBlossomLimit = 150m,
            BlossomUsed = 30m,
            BlossomRemaining = 120m,
            IsClosed = true,
            ClosedAt = periodStart.AddMonths(1),
        });
        context.OrganizationSubscriptionSnapshots.Add(new OrganizationSubscriptionSnapshot
        {
            OrganizationId = org.Id,
            SnapshotDay = periodStart,
            PlanTier = PlanTier.Bloom,
            HasBillingRow = true,
            SeatsIncluded = 3,
            // Never assigned in production, so this is what every real subscription looks like.
            PriceLkr = 0m,
            CreatedAt = DateTime.UtcNow,
        });
        await context.SaveChangesAsync();

        return new Seeded(org.Id, owner.ClerkId, manager.ClerkId, supervisor.ClerkId);
    }

    // ── E-11: top-up packs ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetTopUpPacks_WithoutAToken_Returns401()
    {
        var response = await _client.GetAsync(
            $"/api/v1/orgs/{Guid.CreateVersion7()}/blossoms/top-up-packs");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetTopUpPacks_AsTheOwner_OffersOnlyActivePacksInSizeOrder()
    {
        const string suffix = "packs";
        var seeded = await SeedAsync(suffix);
        var token = CreateToken(seeded.OwnerClerk, orgRole: Roles.BoutiqueOwner);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Get, $"/api/v1/orgs/{seeded.OrgId}/blossoms/top-up-packs", token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var packs = body.EnumerateArray().ToList();
        var codes = packs.Select(pack => pack.GetProperty("skuCode").GetString()).ToList();

        // The catalogue is global (a top-up pack is not per-organization), so other tests in this
        // shared in-memory database seed their own packs. The assertions therefore key on this
        // test's own SKUs.
        var small = $"pack-100-{suffix}";
        var large = $"pack-500-{suffix}";
        Assert.Contains(small, codes);
        Assert.Contains(large, codes);
        // The draft row is not for sale, so it is not offered.
        Assert.DoesNotContain($"pack-draft-{suffix}", codes);
        // Ordered by size, so a dialog can present the packs in a stable order.
        Assert.True(codes.IndexOf(small) < codes.IndexOf(large));

        var pack = packs.Single(p => p.GetProperty("skuCode").GetString() == small);
        Assert.Equal(100m, pack.GetProperty("blossomQuantity").GetDecimal());
        Assert.Equal(500m, pack.GetProperty("priceLkr").GetDecimal());
        Assert.Equal("LKR", pack.GetProperty("currency").GetString());
    }

    [Fact]
    public async Task GetTopUpPacks_ForARepricedSku_OffersTheEffectiveRowOnce()
    {
        // G7: before this slice the catalogue listed every Active row, so a re-priced SKU appeared
        // twice, and an expired row stayed on sale. The catalogue now selects the newest row whose
        // effective window contains now — the same row the purchase route picks.
        const string suffix = "repriced";
        var seeded = await SeedAsync(suffix);
        var token = CreateToken(seeded.OwnerClerk, orgRole: Roles.BoutiqueOwner);
        var repriced = $"pack-repriced-{suffix}";
        var expired = $"pack-expired-{suffix}";

        await using (var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                         .UseInMemoryDatabase(databaseName: "AvelineInMemoryDb")
                         .Options))
        {
            context.BlossomPriceEntries.AddRange(
                new BlossomPriceEntry
                {
                    SkuKind = BlossomSkuKind.TopUpPack, SkuCode = repriced,
                    BlossomQuantity = 500m, PriceLkr = 2000m, Status = BlossomRuleStatus.Active,
                    EffectiveFrom = DateTime.UtcNow.AddMonths(-6),
                    EffectiveTo = DateTime.UtcNow.AddMonths(-1),
                },
                new BlossomPriceEntry
                {
                    SkuKind = BlossomSkuKind.TopUpPack, SkuCode = repriced,
                    BlossomQuantity = 500m, PriceLkr = 2500m, Status = BlossomRuleStatus.Active,
                    EffectiveFrom = DateTime.UtcNow.AddMonths(-1),
                },
                new BlossomPriceEntry
                {
                    SkuKind = BlossomSkuKind.TopUpPack, SkuCode = expired,
                    BlossomQuantity = 100m, PriceLkr = 100m, Status = BlossomRuleStatus.Active,
                    EffectiveFrom = DateTime.UtcNow.AddMonths(-6),
                    EffectiveTo = DateTime.UtcNow.AddMonths(-1),
                });
            await context.SaveChangesAsync();
        }

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Get, $"/api/v1/orgs/{seeded.OrgId}/blossoms/top-up-packs", token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var packs = body.EnumerateArray().ToList();

        var pack = Assert.Single(packs, item => item.GetProperty("skuCode").GetString() == repriced);
        Assert.Equal(2500m, pack.GetProperty("priceLkr").GetDecimal());
        Assert.DoesNotContain(expired, packs.Select(
            item => item.GetProperty("skuCode").GetString()));
    }

    [Fact]
    public async Task GetTopUpPacks_AsAManager_Returns403()
    {
        // A manager holds `billing:view` but not `billing:manage`, and the catalogue carries the
        // purchase's own permission: whoever may not buy may not see what is for sale either.
        var seeded = await SeedAsync("packs-manager");
        var token = CreateToken(seeded.ManagerClerk, orgRole: Roles.BoutiqueManager);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Get, $"/api/v1/orgs/{seeded.OrgId}/blossoms/top-up-packs", token));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetTopUpPacks_ForAnotherOrganization_Returns403()
    {
        var mine = await SeedAsync("packs-mine");
        var theirs = await SeedAsync("packs-theirs");
        var token = CreateToken(mine.OwnerClerk, orgRole: Roles.BoutiqueOwner);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Get, $"/api/v1/orgs/{theirs.OrgId}/blossoms/top-up-packs", token));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── E-12: billing periods ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetBillingPeriods_AsAManager_ReportsAPeriodWhosePriceIsNullNotZero()
    {
        var seeded = await SeedAsync("periods");
        var token = CreateToken(seeded.ManagerClerk, orgRole: Roles.BoutiqueManager);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Get, $"/api/v1/orgs/{seeded.OrgId}/billing/periods", token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var period = Assert.Single(body.EnumerateArray().ToList());

        Assert.Equal("Bloom", period.GetProperty("planTier").GetString());
        Assert.True(period.GetProperty("hasSubscriptionRow").GetBoolean());
        Assert.True(period.GetProperty("isClosed").GetBoolean());
        Assert.Equal(150m, period.GetProperty("monthlyBlossomLimit").GetDecimal());
        // C-4: the stored price is 0, so the field is null with a flag — never "LKR 0".
        Assert.Equal(JsonValueKind.Null, period.GetProperty("planListPriceLkr").ValueKind);
        Assert.False(period.GetProperty("subscriptionPricesConfigured").GetBoolean());
    }

    [Fact]
    public async Task GetBillingPeriods_AsASupervisor_Returns403()
    {
        // A supervisor holds `billing:view:self` (the balance) but not `billing:view` (the history).
        var seeded = await SeedAsync("periods-supervisor");
        var token = CreateToken(seeded.SupervisorClerk, orgRole: Roles.BoutiqueSupervisor);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Get, $"/api/v1/orgs/{seeded.OrgId}/billing/periods", token));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetBillingPeriods_ForAnotherOrganization_Returns403()
    {
        var mine = await SeedAsync("periods-mine");
        var theirs = await SeedAsync("periods-theirs");
        var token = CreateToken(mine.ManagerClerk, orgRole: Roles.BoutiqueManager);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Get, $"/api/v1/orgs/{theirs.OrgId}/billing/periods", token));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetBillingPeriods_WithTakeOutsideTheRange_Returns400RatherThanClamping()
    {
        var seeded = await SeedAsync("periods-take");
        var token = CreateToken(seeded.ManagerClerk, orgRole: Roles.BoutiqueManager);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Get, $"/api/v1/orgs/{seeded.OrgId}/billing/periods?take=500", token));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
