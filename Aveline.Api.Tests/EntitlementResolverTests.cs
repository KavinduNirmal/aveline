using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Billing.Domain;
using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Billing.Repositories;
using Aveline.Api.Modules.Organizations.Models;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Tests;

/// <summary>
/// Issue #193 — the single source of plan truth (BR-2.15, BR-2.16). Resolves tiers and
/// per-org overrides with effective dating.
/// </summary>
public class EntitlementResolverTests
{
    private static readonly DateTime Now = new(2026, 9, 15, 0, 0, 0, DateTimeKind.Utc);

    private readonly AppDbContext _context;

    public EntitlementResolverTests()
    {
        _context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"Entitlements_{Guid.NewGuid()}")
            .Options);
    }

    private EntitlementResolver CreateResolver() => new(new EntitlementRepository(_context));

    private async Task<Guid> SeedOrganizationAsync(PlanTier tier)
    {
        var ownerId = Guid.CreateVersion7();
        _context.Users.Add(new Modules.Shared.Models.User
        {
            Id = ownerId,
            ClerkId = $"owner_{ownerId:N}",
            Email = "owner@aveline.lk",
            FirstName = "Entitlement",
            LastName = "Owner",
            Username = $"owner_{ownerId:N}",
            UserRole = "owner",
            OrganizationRole = "org:boutique_owner",
        });

        var org = new Organization
        {
            Name = "Entitlement Boutique",
            Slug = $"ent-{ownerId:N}",
            OwnerUserId = ownerId,
            PlanTier = tier,
        };
        _context.Organizations.Add(org);
        await _context.SaveChangesAsync();
        return org.Id;
    }

    private void SeedPlan(PlanTier tier, string key, decimal? number = null, bool? flag = null, string? text = null,
        DateTime? from = null, DateTime? to = null)
    {
        _context.PlanEntitlements.Add(new PlanEntitlement
        {
            PlanTier = tier,
            Key = key,
            ValueType = number is not null
                ? EntitlementValueType.Decimal
                : flag is not null ? EntitlementValueType.Boolean : EntitlementValueType.String,
            ValueDecimal = number,
            ValueBool = flag,
            ValueText = text,
            IsEnabled = true,
            EffectiveFrom = from ?? Now.AddDays(-60),
            EffectiveTo = to,
            CreatedAt = Now.AddDays(-60),
        });
    }

    [Fact]
    public async Task GetAsync_ReturnsTheTierValue()
    {
        var orgId = await SeedOrganizationAsync(PlanTier.Bloom);
        SeedPlan(PlanTier.Bloom, "blossoms.monthly", number: 750m);
        SeedPlan(PlanTier.Bloom, "api.access", flag: false);
        await _context.SaveChangesAsync();

        var resolver = CreateResolver();

        var blossoms = await resolver.GetAsync(orgId, "blossoms.monthly", Now);
        Assert.NotNull(blossoms);
        Assert.Equal(750m, blossoms!.Number);
        Assert.Equal("Plan", blossoms.Source);

        var api = await resolver.GetAsync(orgId, "api.access", Now);
        Assert.False(api!.Flag);
    }

    [Fact]
    public async Task GetAsync_OverrideTakesPrecedenceOverTheTierRow()
    {
        var orgId = await SeedOrganizationAsync(PlanTier.Enterprise);
        SeedPlan(PlanTier.Enterprise, "blossoms.monthly", number: 9999m);
        _context.PlanEntitlementOverrides.Add(new PlanEntitlementOverride
        {
            OrganizationId = orgId,
            Key = "blossoms.monthly",
            ValueType = EntitlementValueType.Decimal,
            ValueDecimal = 25000m,
            EffectiveFrom = Now.AddDays(-1),
            Reason = "Negotiated Enterprise contract.",
            CreatedByUserId = Guid.CreateVersion7(),
            CreatedAt = Now.AddDays(-1),
        });
        await _context.SaveChangesAsync();

        var value = await CreateResolver().GetAsync(orgId, "blossoms.monthly", Now);

        Assert.Equal(25000m, value!.Number);
        Assert.Equal("Override", value.Source);
    }

    [Fact]
    public async Task GetAsync_UsesTheMostRecentEffectiveRow_AndIgnoresExpiredOnes()
    {
        var orgId = await SeedOrganizationAsync(PlanTier.Bloom);
        SeedPlan(PlanTier.Bloom, "blossoms.monthly", number: 500m);
        SeedPlan(PlanTier.Bloom, "blossoms.monthly", number: 750m, from: Now.AddDays(-10));
        SeedPlan(PlanTier.Bloom, "blossoms.monthly", number: 9999m, from: Now.AddDays(-30), to: Now.AddDays(-5));
        await _context.SaveChangesAsync();

        var value = await CreateResolver().GetAsync(orgId, "blossoms.monthly", Now);

        Assert.Equal(750m, value!.Number);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsEveryEffectiveKey()
    {
        var orgId = await SeedOrganizationAsync(PlanTier.Orchid);
        SeedPlan(PlanTier.Orchid, "blossoms.monthly", number: 2000m);
        SeedPlan(PlanTier.Orchid, "staff.max", number: 10m);
        SeedPlan(PlanTier.Orchid, "analytics.level", text: "advanced");
        await _context.SaveChangesAsync();

        var all = await CreateResolver().GetAllAsync(orgId, Now);

        Assert.Equal(3, all.Count);
        Assert.Equal(2000m, all["blossoms.monthly"].Number);
        Assert.Equal("advanced", all["analytics.level"].Text);
    }

    [Fact]
    public async Task GetDecimalAsync_MissingKey_ReturnsFallback()
    {
        var orgId = await SeedOrganizationAsync(PlanTier.Seed);

        var value = await CreateResolver().GetDecimalAsync(orgId, "does.not.exist", 42m, Now);

        Assert.Equal(42m, value);
    }
}
