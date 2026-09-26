using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Billing.Repositories;
using Aveline.Api.Modules.Billing.Services;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Tests;

/// <summary>
/// Issue #404 (Payments P1) — the price-book resolution rule (G7, FR-1.11). The resolver reads
/// <c>BlossomPriceEntries</c> where <c>SkuKind = PlanAllowance</c> and applies, in order:
/// an <c>OrganizationId</c> override, the <c>PlanTier</c> list price, then the global row
/// (<c>OrganizationId IS NULL</c> and <c>PlanTier IS NULL</c>); within the winning scope the newest
/// <c>EffectiveFrom &lt;= at</c> inside its effective window.
///
/// The shape mirrors <c>EntitlementResolver</c> (<c>Domain/EntitlementResolver.cs:33-51</c>).
/// The null-versus-zero distinction is load-bearing: <c>null</c> means "no price row exists"
/// (no charge, MRR stays null), while a real zero row is a deliberate free plan.
/// </summary>
public class SubscriptionPriceResolverTests
{
    private static readonly DateTime At = new(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc);

    private readonly AppDbContext _context = new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"PriceResolution_{Guid.NewGuid()}")
            .Options);

    private readonly Guid _organizationId = Guid.CreateVersion7();

    private ISubscriptionPriceResolver Resolver() =>
        new SubscriptionPriceResolver(new PricingRepository(_context));

    private void AddRow(
        decimal priceLkr,
        DateTime effectiveFrom,
        DateTime? effectiveTo = null,
        PlanTier? planTier = null,
        Guid? organizationId = null,
        BlossomRuleStatus status = BlossomRuleStatus.Active,
        BlossomSkuKind skuKind = BlossomSkuKind.PlanAllowance,
        string? skuCode = null) =>
        _context.BlossomPriceEntries.Add(new BlossomPriceEntry
        {
            PlanTier = planTier,
            OrganizationId = organizationId,
            SkuKind = skuKind,
            SkuCode = skuCode,
            BlossomQuantity = 750m,
            PriceLkr = priceLkr,
            EffectiveFrom = effectiveFrom,
            EffectiveTo = effectiveTo,
            Status = status,
            ChangeReason = "A price-book row seeded by the subscription price resolver test.",
            CreatedByUserId = Guid.CreateVersion7(),
        });

    private Task<decimal?> ResolveAsync(PlanTier tier, BillingCycle cycle = BillingCycle.Monthly) =>
        Resolver().ResolveAsync(_organizationId, tier, cycle, at: At);

    [Fact]
    public async Task ResolvesThePlanTierPrice()
    {
        AddRow(3500m, At.AddMonths(-3), planTier: PlanTier.Bloom);
        await _context.SaveChangesAsync();

        Assert.Equal(3500m, await ResolveAsync(PlanTier.Bloom));
    }

    [Fact]
    public async Task AnOrganizationOverrideBeatsThePlanTierPrice()
    {
        AddRow(3500m, At.AddMonths(-3), planTier: PlanTier.Bloom);
        AddRow(2900m, At.AddMonths(-3), planTier: PlanTier.Bloom, organizationId: _organizationId);
        await _context.SaveChangesAsync();

        Assert.Equal(2900m, await ResolveAsync(PlanTier.Bloom));
    }

    [Fact]
    public async Task TheGlobalRowIsTheLastResortWhenNoTierPriceExists()
    {
        AddRow(2500m, At.AddMonths(-3));
        await _context.SaveChangesAsync();

        Assert.Equal(2500m, await ResolveAsync(PlanTier.Orchid));
    }

    [Fact]
    public async Task PicksTheNewestRowEffectiveAtTheInstant()
    {
        AddRow(3000m, At.AddMonths(-6), effectiveTo: At.AddMonths(-3), planTier: PlanTier.Bloom);
        AddRow(3500m, At.AddMonths(-3), planTier: PlanTier.Bloom);
        await _context.SaveChangesAsync();

        // Both rows are in the book; the one whose window contains `at` wins, and when two do,
        // the latest EffectiveFrom wins.
        Assert.Equal(3500m, await ResolveAsync(PlanTier.Bloom));
        Assert.Equal(3000m, await Resolver().ResolveAsync(
            _organizationId, PlanTier.Bloom, BillingCycle.Monthly, at: At.AddMonths(-4)));
    }

    [Fact]
    public async Task ReturnsNullWhenNoRowExists()
    {
        Assert.Null(await ResolveAsync(PlanTier.Bloom));
    }

    [Fact]
    public async Task ReturnsNullWhenEveryRowIsFutureDated()
    {
        AddRow(3500m, At.AddDays(1), planTier: PlanTier.Bloom);
        await _context.SaveChangesAsync();

        Assert.Null(await ResolveAsync(PlanTier.Bloom));
    }

    [Fact]
    public async Task IgnoresAnExpiredRow()
    {
        AddRow(3500m, At.AddMonths(-6), effectiveTo: At.AddMonths(-1), planTier: PlanTier.Bloom);
        await _context.SaveChangesAsync();

        Assert.Null(await ResolveAsync(PlanTier.Bloom));
    }

    [Fact]
    public async Task IgnoresANonActiveRow()
    {
        AddRow(3500m, At.AddMonths(-3), planTier: PlanTier.Bloom, status: BlossomRuleStatus.Draft);
        await _context.SaveChangesAsync();

        Assert.Null(await ResolveAsync(PlanTier.Bloom));
    }

    [Fact]
    public async Task IgnoresAnotherOrganizationsOverride()
    {
        AddRow(1m, At.AddMonths(-3), organizationId: Guid.CreateVersion7());
        await _context.SaveChangesAsync();

        Assert.Null(await ResolveAsync(PlanTier.Bloom));
    }

    [Fact]
    public async Task IgnoresARowForADifferentSkuKind()
    {
        AddRow(500m, At.AddMonths(-3), planTier: PlanTier.Bloom, skuKind: BlossomSkuKind.TopUpPack,
            skuCode: "blossom_pack_500");
        await _context.SaveChangesAsync();

        Assert.Null(await ResolveAsync(PlanTier.Bloom));
    }

    [Fact]
    public async Task AZeroPricedRowIsFreeWhileAMissingRowIsNull()
    {
        // The distinction the issue calls out: a zero row is a real free plan, a missing row is
        // not a price at all.
        AddRow(0m, At.AddMonths(-3), planTier: PlanTier.Bloom);
        await _context.SaveChangesAsync();

        Assert.Equal(0m, await ResolveAsync(PlanTier.Bloom));
        Assert.Null(await ResolveAsync(PlanTier.Rose));
    }

    [Fact]
    public async Task AnAnnualSubscriptionHasNoPriceInTheBookToday()
    {
        // `BillingCycle.Annual` has no behaviour (the plan's Q4 keeps annual billing out of scope),
        // and the price book has no cycle column, so inferring an annual price from a monthly row
        // would under-bill. The honest answer is that no annual price exists.
        AddRow(3500m, At.AddMonths(-3), planTier: PlanTier.Bloom);
        await _context.SaveChangesAsync();

        Assert.Equal(3500m, await ResolveAsync(PlanTier.Bloom, BillingCycle.Monthly));
        Assert.Null(await ResolveAsync(PlanTier.Bloom, BillingCycle.Annual));
    }
}
