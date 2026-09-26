using Aveline.Api.Authorization;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Payments;
using Aveline.Api.Modules.Revenue;
using Aveline.Api.Modules.Revenue.DTOs;
using Aveline.Api.Modules.Revenue.Models;
using Aveline.Api.Modules.Revenue.Services;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Aveline.Api.Tests;

/// <summary>
/// Revenue Ledger R3 (issue #344) — the read layer, and the null-versus-zero contract that is the
/// whole reason it is not a thin projection.
///
/// The business-KPI surface already recorded the failure mode this guards: *a frontend that ignores
/// the data-quality block will display zeros that look like real measurements*. Money has its own
/// version of that trap, and it is sharper than the others because the numbers are currency:
/// a subscription with `PriceLkr = 0` is not a free plan, it is an unconfigured price, and it must
/// not enter MRR as a `0` that reads as "we earn nothing".
/// </summary>
public class RevenueStatisticsQueryTests
{
    /// <summary>
    /// One isolated store per test. A shared in-memory database let one test's organizations answer
    /// another test's aggregates, which is a trap this repository has hit before: the InMemory
    /// provider keys its store by name and the name is global.
    /// </summary>
    private sealed class Store : IDisposable
    {
        private readonly string _name = $"RevStats_{Guid.NewGuid()}";

        public AppDbContext Context() =>
            new(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(databaseName: _name)
                .Options);

        public void Dispose() { }
    }

    private static readonly DateTime WindowFrom = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime WindowTo = new(2026, 9, 5, 0, 0, 0, DateTimeKind.Utc);

    private static RevenueStatisticsService CreateService(
        AppDbContext context, int maxWindowDays = 400, string providerKey = PaymentsOptions.ManualProviderKey) =>
        new(context,
            Options.Create(new RevenueOptions { MaxWindowDays = maxWindowDays, CacheSeconds = 60 }),
            Options.Create(new PaymentsOptions { Provider = providerKey }),
            NullLogger<RevenueStatisticsService>.Instance);

    /// <summary>
    /// A clock the family can be tested against. `TimeProvider` is injected rather than read, so a
    /// default window is deterministic.
    /// </summary>
    private sealed class FixedClock(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now, TimeSpan.Zero);
    }

    private static async Task<Guid> SeedOrganizationAsync(Store store, string suffix, PlanTier tier = PlanTier.Bloom)
    {
        await using var context = store.Context();
        var ownerId = Guid.CreateVersion7();
        context.Users.Add(new User
        {
            Id = ownerId, ClerkId = $"rs_{suffix}_{ownerId:N}", Email = $"rs_{suffix}@aveline.lk",
            FirstName = "R", LastName = "S", Username = $"rs_{suffix}_{ownerId:N}",
            UserRole = Roles.Owner, OrganizationRole = string.Empty,
        });
        var org = new Organization
        {
            Name = $"Rev Stats {suffix}", Slug = $"rev-stats-{suffix}", OwnerUserId = ownerId,
            PlanTier = tier,
        };
        context.Organizations.Add(org);
        await context.SaveChangesAsync();
        return org.Id;
    }

    private static async Task SeedSubscriptionAsync(
        Store store, Guid orgId, PlanTier tier, decimal priceLkr, BillingCycle cycle = BillingCycle.Monthly,
        SubscriptionStatus status = SubscriptionStatus.Active)
    {
        await using var context = store.Context();
        context.OrganizationSubscriptions.Add(new OrganizationSubscription
        {
            OrganizationId = orgId,
            PlanTier = tier,
            BillingCycle = cycle,
            Status = status,
            CurrentPeriodStart = WindowFrom,
            CurrentPeriodEnd = WindowTo,
            PriceLkr = priceLkr,
        });
        await context.SaveChangesAsync();
    }

    private static async Task SeedEntryAsync(
        Store store,
        Guid orgId,
        decimal amount,
        IncomeEntryKind kind,
        IncomeChargeBasis basis,
        DateTime occurredAt,
        string sourceRef,
        IncomeEntryStatus status = IncomeEntryStatus.Recorded,
        IncomeSourceKind sourceKind = IncomeSourceKind.SubscriptionBilling)
    {
        await using var context = store.Context();
        context.IncomeLedgerEntries.Add(new IncomeLedgerEntry
        {
            OrganizationId = orgId,
            Kind = kind,
            SourceKind = sourceKind,
            SourceRef = sourceRef,
            ChargeBasis = basis,
            Status = status,
            Amount = amount,
            Reason = "A reason long enough to satisfy the rule.",
            OccurredAt = occurredAt,
            RecordedByUserId = Guid.CreateVersion7(),
        });
        await context.SaveChangesAsync();
    }

    // ── Window validation ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AWindowOverTheCap_IsRejectedNamingTheEffectiveLimit()
    {
        using var store = new Store();
        await using var context = store.Context();
        var service = CreateService(context, maxWindowDays: 90);

        var result = await service.GetOverviewAsync(
            new RevenueWindow(WindowFrom, WindowFrom.AddDays(200)), default);

        Assert.False(result.IsValid);
        // The effective limit is named, so the caller can correct the request rather than guess.
        Assert.Contains("90", result.Message);
    }

    [Fact]
    public async Task AnInvertedWindow_IsRejected()
    {
        using var store = new Store();
        await using var context = store.Context();
        var service = CreateService(context);

        var result = await service.GetOverviewAsync(new RevenueWindow(WindowTo, WindowFrom), default);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void AWindowAtTheCap_IsAccepted()
    {
        var options = new RevenueOptions { MaxWindowDays = 400 };

        var valid = RevenueWindowValidation.TryCreate(
            options, WindowFrom, WindowFrom.AddDays(400), "day", out var window, out var message);

        Assert.True(valid, message);
        Assert.NotNull(window);
    }

    // ── Null versus zero, and the MRR trap ──────────────────────────────────────────────────

    /// <summary>
    /// The headline honesty rule. Every subscription today has `PriceLkr = 0` because
    /// `SubscriptionService` never assigns it, so MRR is **`null`**, not `0`: a `0` would read as
    /// "we earn nothing" rather than "the price is not configured".
    /// </summary>
    [Fact]
    public async Task Mrr_IsNullWhenNoSubscriptionHasAPrice()
    {
        using var store = new Store();
        var orgId = await SeedOrganizationAsync(store, "mrr-zero");
        await SeedSubscriptionAsync(store, orgId, PlanTier.Bloom, priceLkr: 0m);
        await using var context = store.Context();

        var result = await CreateService(context).GetOverviewAsync(
            new RevenueWindow(WindowFrom, WindowTo), default);

        Assert.True(result.IsValid);
        var overview = result.Value!;
        Assert.Null(overview.Mrr);
        Assert.Null(overview.Arr);
        Assert.Null(overview.Arpu);
        Assert.False(overview.DataQuality.SubscriptionPricesConfigured);
        // And the reason is stated, not left for the reader to infer.
        Assert.Contains(
            overview.DataQuality.Notes,
            note => note.Contains("price", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Mrr_SumsPricedActiveSubscriptions_AndNormalisesAnAnnualCycle()
    {
        using var store = new Store();
        var monthly = await SeedOrganizationAsync(store, "mrr-monthly");
        var annual = await SeedOrganizationAsync(store, "mrr-annual");
        await SeedSubscriptionAsync(store, monthly, PlanTier.Bloom, priceLkr: 4500m);
        await SeedSubscriptionAsync(store, annual, PlanTier.Orchid, priceLkr: 120000m, cycle: BillingCycle.Annual);
        await using var context = store.Context();

        var result = await CreateService(context).GetOverviewAsync(
            new RevenueWindow(WindowFrom, WindowTo), default);

        var overview = result.Value!;
        // 4500 + 120000/12 = 14500
        Assert.Equal(14500m, overview.Mrr);
        Assert.Equal(174000m, overview.Arr);
        Assert.Equal(2, overview.PayingOrganizations);
        Assert.Equal(7250m, overview.Arpu);
        Assert.True(overview.DataQuality.SubscriptionPricesConfigured);
    }

    [Fact]
    public async Task Mrr_IgnoresACancelledSubscription()
    {
        using var store = new Store();
        var orgId = await SeedOrganizationAsync(store, "mrr-cancelled");
        await SeedSubscriptionAsync(
            store, orgId, PlanTier.Bloom, priceLkr: 4500m,
            status: SubscriptionStatus.Cancelled);
        await using var context = store.Context();

        var result = await CreateService(context).GetOverviewAsync(
            new RevenueWindow(WindowFrom, WindowTo), default);

        Assert.Null(result.Value!.Mrr);
    }

    [Fact]
    public async Task Arpu_IsNullRatherThanADivideByZero()
    {
        using var store = new Store();
        await using var context = store.Context();

        var result = await CreateService(context).GetOverviewAsync(
            new RevenueWindow(WindowFrom, WindowTo), default);

        Assert.Null(result.Value!.Arpu);
        Assert.Null(result.Value!.Mrr);
    }

    // ── Buckets ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TheTimeseries_HasADenseBucketAxis_WithIsPartialOnBothClippedEdges()
    {
        using var store = new Store();
        var orgId = await SeedOrganizationAsync(store, "dense");
        await SeedEntryAsync(store, orgId, 100m, IncomeEntryKind.SubscriptionCharge,
            IncomeChargeBasis.Derived, WindowFrom, "d-1");
        await using var context = store.Context();

        var result = await CreateService(context).GetTimeseriesAsync(
            new RevenueWindow(WindowFrom.AddHours(6), WindowTo.AddHours(-6)), "day", default);

        var series = result.Value!.Series;
        // Four calendar days, not one: the axis is dense and aligned to midnight, so a day with no
        // rows is a real zero in a period that was observed.
        Assert.Equal(4, series.Count);
        Assert.Equal(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), series[0].BucketStart);

        // The window runs 06:00 on the 1st to 18:00 on the 4th, so it clips the first and last
        // buckets. The two in between are whole days.
        Assert.True(series[0].IsPartial, "the leading bucket is clipped by `from`");
        Assert.False(series[1].IsPartial, "a whole day inside the window");
        Assert.False(series[2].IsPartial, "a whole day inside the window");
        Assert.True(series[3].IsPartial, "the trailing bucket is clipped by `to`");
    }

    [Fact]
    public async Task TheTimeseries_LeavesAMeasuredZeroAsZero()
    {
        using var store = new Store();
        await using var context = store.Context();

        var result = await CreateService(context).GetTimeseriesAsync(
            new RevenueWindow(WindowFrom, WindowTo), "day", default);

        var series = result.Value!.Series;
        Assert.Equal(4, series.Count);
        Assert.All(series, point =>
        {
            Assert.Equal(0m, point.Derived);
            Assert.Equal(0m, point.Verified);
            Assert.Equal(0m, point.Refunded);
        });
    }

    [Fact]
    public async Task TheTimeseries_ReportsDerivedVerifiedAndRefundedSeparately()
    {
        using var store = new Store();
        var orgId = await SeedOrganizationAsync(store, "split");
        await SeedEntryAsync(store, orgId, 5000m, IncomeEntryKind.SubscriptionCharge,
            IncomeChargeBasis.Derived, WindowFrom.AddHours(1), "s-1");
        await SeedEntryAsync(store, orgId, 5000m, IncomeEntryKind.SubscriptionCharge,
            IncomeChargeBasis.Verified, WindowFrom.AddHours(2), "s-2");
        await SeedEntryAsync(store, orgId, 1500m, IncomeEntryKind.Refund,
            IncomeChargeBasis.Verified, WindowFrom.AddHours(3), "s-3");
        await using var context = store.Context();

        var result = await CreateService(context).GetTimeseriesAsync(
            new RevenueWindow(WindowFrom, WindowTo), "day", default);

        var first = result.Value!.Series[0];
        // Three separate series, never merged into one line: the gap between the first two is the
        // most important number on the surface.
        Assert.Equal(5000m, first.Derived);
        Assert.Equal(5000m, first.Verified);
        Assert.Equal(1500m, first.Refunded);
    }

    [Fact]
    public async Task TwelveMonths_ProducesTwelveBuckets()
    {
        using var store = new Store();
        await using var context = store.Context();

        var result = await CreateService(context).GetTimeseriesAsync(
            new RevenueWindow(WindowFrom, WindowFrom.AddMonths(12)), "month", default);

        Assert.Equal(12, result.Value!.Series.Count);
    }

    [Fact]
    public async Task AVoidedEntry_ContributesToNoSeries()
    {
        using var store = new Store();
        var orgId = await SeedOrganizationAsync(store, "voided");
        await SeedEntryAsync(store, orgId, 5000m, IncomeEntryKind.SubscriptionCharge,
            IncomeChargeBasis.Derived, WindowFrom.AddHours(1), "v-1",
            status: IncomeEntryStatus.Voided);
        await using var context = store.Context();

        var result = await CreateService(context).GetTimeseriesAsync(
            new RevenueWindow(WindowFrom, WindowTo), "day", default);

        Assert.All(result.Value!.Series, point => Assert.Equal(0m, point.Derived));
    }

    // ── The unverified gap ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// The gap counts only derived rows that nothing live took over, and it is the **absolute**
    /// distance between the two figures rather than a signed subtraction: a charge with no receipt
    /// and a receipt with no charge are equally findings, and reporting the second as a negative
    /// number would read like a typo.
    /// </summary>
    [Fact]
    public async Task TheReconciliation_CountsOnlyDerivedRowsWithNoVerifiedCounterpart()
    {
        using var store = new Store();
        var orgId = await SeedOrganizationAsync(store, "gap");

        // Settled: the derived expectation was taken over by its receipt, so the derived row is
        // voided and neither figure double-counts it.
        await SeedEntryAsync(store, orgId, 5000m, IncomeEntryKind.SubscriptionCharge,
            IncomeChargeBasis.Derived, WindowFrom.AddHours(1), "settled",
            status: IncomeEntryStatus.Voided);
        await SeedEntryAsync(store, orgId, 5000m, IncomeEntryKind.SubscriptionCharge,
            IncomeChargeBasis.Verified, WindowFrom.AddHours(2), "settled");

        // Billed, never collected: the derived row is live and has no counterpart.
        await SeedEntryAsync(store, orgId, 2000m, IncomeEntryKind.SubscriptionCharge,
            IncomeChargeBasis.Derived, WindowFrom.AddHours(3), "outstanding");

        // A receipt with no matching charge — just as much a finding, and the reason the gap is
        // reported as a magnitude rather than a signed difference.
        await SeedEntryAsync(store, orgId, 500m, IncomeEntryKind.SubscriptionCharge,
            IncomeChargeBasis.Verified, WindowFrom.AddHours(4), "overpaid");

        await using var context = store.Context();

        var result = await CreateService(context).GetLedgerAsync(
            new RevenueLedgerQuery(new RevenueWindow(WindowFrom, WindowTo), 1, 50), default);

        var page = result.Value!;
        Assert.Equal(1, page.DataQuality.DerivedEntriesUnverified);
        Assert.Equal(2000m, page.Reconciliation.DerivedTotal);
        Assert.Equal(5500m, page.Reconciliation.VerifiedTotal);
        // |2000 - 5500| = 3500, absolute.
        Assert.Equal(3500m, page.Reconciliation.UnverifiedGap);
        // Nothing is outstanding: more was collected than billed.
        Assert.True(page.Reconciliation.IsBalanced);
    }

    [Fact]
    public async Task TheReconciliation_IsBalancedWhenEverythingBilledWasCollected()
    {
        using var store = new Store();
        var orgId = await SeedOrganizationAsync(store, "balanced");
        await SeedEntryAsync(store, orgId, 5000m, IncomeEntryKind.SubscriptionCharge,
            IncomeChargeBasis.Derived, WindowFrom.AddHours(1), "b-1");
        await SeedEntryAsync(store, orgId, 5000m, IncomeEntryKind.SubscriptionCharge,
            IncomeChargeBasis.Verified, WindowFrom.AddHours(2), "b-2");
        await using var context = store.Context();

        var result = await CreateService(context).GetLedgerAsync(
            new RevenueLedgerQuery(new RevenueWindow(WindowFrom, WindowTo), 1, 50), default);

        Assert.True(result.Value!.Reconciliation.IsBalanced);
        Assert.Equal(0m, result.Value!.Reconciliation.UnverifiedGap);
    }

    // ── Collections ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CollectionRate_IsNullRatherThanZeroWhenNothingWasBilled()
    {
        using var store = new Store();
        await using var context = store.Context();

        var result = await CreateService(context).GetCollectionsAsync(
            new RevenueWindow(WindowFrom, WindowTo), "day", default);

        // A window with nothing billed has no collection rate. `0` would read as "nothing was
        // collected", which is a different and false claim.
        Assert.All(result.Value!.Series, point => Assert.Null(point.CollectionRate));
    }

    [Fact]
    public async Task CollectionRate_ReportsOverCollectionRatherThanClipping()
    {
        using var store = new Store();
        var orgId = await SeedOrganizationAsync(store, "over");
        await SeedEntryAsync(store, orgId, 1000m, IncomeEntryKind.SubscriptionCharge,
            IncomeChargeBasis.Derived, WindowFrom.AddHours(1), "o-1");
        await SeedEntryAsync(store, orgId, 1400m, IncomeEntryKind.SubscriptionCharge,
            IncomeChargeBasis.Verified, WindowFrom.AddHours(2), "o-2");
        await using var context = store.Context();

        var result = await CreateService(context).GetCollectionsAsync(
            new RevenueWindow(WindowFrom, WindowTo), "day", default);

        var first = result.Value!.Series[0];
        // A receipt with no matching charge is exactly what an operator needs to see.
        Assert.Equal(140m, first.CollectionRate);
        Assert.Equal(-400m, first.Outstanding);
    }

    // ── Blossom pack sales ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BlossomSales_CountOnlyReferencedTopUps_AndStateTheUnreferencedDifference()
    {
        using var store = new Store();
        var orgId = await SeedOrganizationAsync(store, "sales");
        // One sale, two rows: the derived expectation and the verified receipt share the dedup
        // reference, so the pack is counted once.
        await SeedEntryAsync(store, orgId, 9000m, IncomeEntryKind.TopUpPurchase,
            IncomeChargeBasis.Derived, WindowFrom.AddHours(1), "pay-1",
            sourceKind: IncomeSourceKind.BlossomTopUp);
        await SeedEntryAsync(store, orgId, 9000m, IncomeEntryKind.TopUpPurchase,
            IncomeChargeBasis.Verified, WindowFrom.AddHours(2), "pay-1",
            sourceKind: IncomeSourceKind.BlossomTopUp);
        // A second sale, never collected.
        await SeedEntryAsync(store, orgId, 4000m, IncomeEntryKind.TopUpPurchase,
            IncomeChargeBasis.Derived, WindowFrom.AddHours(3), "pay-2",
            sourceKind: IncomeSourceKind.BlossomTopUp);

        await using var context = store.Context();

        var result = await CreateService(context).GetBlossomSalesAsync(
            new RevenueWindow(WindowFrom, WindowTo), "day", default);

        var sales = result.Value!;
        Assert.Equal(2, sales.PacksSold);
        Assert.Equal(13000m, sales.ListPriceLkr);
        Assert.Equal(9000m, sales.VerifiedLkr);
        Assert.Equal(69.23m, sales.Conversion);
    }

    // ── The data-quality vocabulary ─────────────────────────────────────────────────────────

    [Fact]
    public async Task TheQualityBlock_AlwaysStatesThatNoProviderSettlesMoney()
    {
        using var store = new Store();
        await using var context = store.Context();

        var result = await CreateService(context).GetOverviewAsync(
            new RevenueWindow(WindowFrom, WindowTo), default);

        var quality = result.Value!.DataQuality;
        Assert.False(quality.RevenueProviderSettlementAvailable);
        Assert.Contains(
            quality.Notes,
            note => note.Contains("provider", StringComparison.OrdinalIgnoreCase));
        Assert.NotEqual(default, quality.CheckedAt);
    }

    /// <summary>
    /// Plan §9.4's read surface: the flag is a property of the configured provider, not a constant.
    /// `manual` settles nothing by itself, so the false branch and its prose survive for the
    /// default deployment; a provider client flips both.
    /// </summary>
    [Theory]
    [InlineData("manual", false)]
    [InlineData("mock", true)]
    [InlineData("stripe", true)]
    public async Task TheQualityBlock_TracksWhetherTheConfiguredProviderSettlesMoney(
        string providerKey, bool settlesMoney)
    {
        using var store = new Store();
        await using var context = store.Context();

        var result = await CreateService(context, providerKey: providerKey).GetOverviewAsync(
            new RevenueWindow(WindowFrom, WindowTo), default);

        var quality = result.Value!.DataQuality;
        Assert.Equal(settlesMoney, quality.RevenueProviderSettlementAvailable);
        Assert.Contains(
            quality.Notes,
            note => note.Contains("provider", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The factory and the live read have to answer the same question the same way; asserting both
    /// is what stops one from being "fixed" while the other keeps the old constant.
    /// </summary>
    [Theory]
    [InlineData("manual", false)]
    [InlineData("mock", true)]
    public void TheCleanFactory_UsesTheSameProviderRuleAsTheLiveRead(string providerKey, bool settlesMoney)
    {
        var quality = IncomeDataQualityDto.Clean(
            DateTime.UtcNow, new PaymentsOptions { Provider = providerKey });

        Assert.Equal(settlesMoney, quality.RevenueProviderSettlementAvailable);
    }
}
