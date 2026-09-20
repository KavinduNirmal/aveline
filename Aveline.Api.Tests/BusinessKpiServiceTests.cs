using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Admin.Models;
using Aveline.Api.Modules.Analytics;
using Aveline.Api.Modules.Analytics.DTOs;
using Aveline.Api.Modules.Analytics.Services;
using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Shared.Models;
using Aveline.Api.Modules.Statistics.Models;
using Aveline.Api.Modules.Statistics.Telemetry;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Tests;

/// <summary>
/// Business KPIs phase 2 (§5.3 S-44/S-45/S-46, §5.4.3–§5.4.5): the dense bucket axis, the
/// null-versus-zero rule, the active-user distinct count, and the D-8 plan-mix invariant
/// against the in-memory provider.
/// </summary>
public class BusinessKpiServiceTests
{
    private static readonly DateTime To = new(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed record Harness(BusinessKpiService Service, AppDbContext Context);

    private static Harness Build()
    {
        var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"BusinessKpiService_{Guid.NewGuid()}")
            .Options);

        return new Harness(
            new BusinessKpiService(
                context,
                new ClaimIdentityMap(),
                new FixedTimeProvider(new DateTimeOffset(To, TimeSpan.Zero))),
            context);
    }

    private static BusinessWindow Window(int days, string granularity = "day", DateTime? from = null)
    {
        var to = To;
        return new BusinessWindow(
            from ?? to.AddDays(-days),
            to,
            granularity,
            BusinessKpiValidation.CountBuckets(from ?? to.AddDays(-days), to, granularity));
    }

    private static User AUser(DateTime createdAt, Guid? id = null) => new()
    {
        Id = id ?? Guid.CreateVersion7(),
        ClerkId = $"user_{Guid.NewGuid():N}",
        Email = $"{Guid.NewGuid():N}@example.test",
        FirstName = "New",
        LastName = "User",
        Username = $"u_{Guid.NewGuid():N}",
        UserRole = "owner",
        OrganizationRole = "boutique_owner",
        CreatedAt = createdAt,
    };

    private static Organization AnOrganization(DateTime createdAt, Guid? id = null, bool isActive = true) => new()
    {
        Id = id ?? Guid.CreateVersion7(),
        Name = "Atelier",
        Slug = $"atelier-{Guid.NewGuid():N}",
        OwnerUserId = Guid.CreateVersion7(),
        PlanTier = PlanTier.Seed,
        IsActive = isActive,
        CreatedAt = createdAt,
    };

    // ── S-44 growth ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GrowthCountsUsersOrganizationsAndRequestsPerDay()
    {
        var harness = Build();
        var day1 = To.Date.AddDays(-3);
        var day2 = To.Date.AddDays(-2);

        harness.Context.Users.AddRange(AUser(day1.AddHours(3)), AUser(day1.AddHours(5)), AUser(day2));
        harness.Context.Organizations.Add(AnOrganization(day1));
        harness.Context.AdminApprovalRequests.AddRange(
            new AdminApprovalRequest { RequestedAt = day1, Status = AdminApprovalStatus.Approved },
            new AdminApprovalRequest { RequestedAt = day1, Status = AdminApprovalStatus.Pending },
            new AdminApprovalRequest { RequestedAt = day2, Status = AdminApprovalStatus.Rejected });
        await harness.Context.SaveChangesAsync();

        var result = await harness.Service.GetGrowthAsync(Window(7), cacheKey: null);

        var p1 = Assert.Single(result.Series, point => point.BucketStart == day1);
        Assert.Equal(2, p1.NewUsers);
        Assert.Equal(1, p1.NewOrganizations);
        Assert.Equal(2, p1.NewAdminRequests);
        Assert.Equal(1, p1.ApprovedAdminRequests);

        var p2 = Assert.Single(result.Series, point => point.BucketStart == day2);
        Assert.Equal(1, p2.NewUsers);
        Assert.Equal(0, p2.NewOrganizations);
        Assert.Equal(1, p2.NewAdminRequests);
        Assert.Equal(0, p2.ApprovedAdminRequests);
    }

    [Fact]
    public async Task GrowthTotalsSumTheWindowAndPreviousTotalsCoverThePrecedingWindow()
    {
        var harness = Build();
        // Window: the trailing 4 days. Previous: the 4 days before that.
        harness.Context.Users.AddRange(
            AUser(To.AddDays(-1)),
            AUser(To.AddDays(-2)),
            AUser(To.AddDays(-5)),
            AUser(To.AddDays(-6)),
            AUser(To.AddDays(-6).AddHours(1)));
        await harness.Context.SaveChangesAsync();

        var result = await harness.Service.GetGrowthAsync(Window(4), cacheKey: null);

        Assert.Equal(2, result.Totals.NewUsers);
        Assert.Equal(3, result.PreviousTotals.NewUsers);
    }

    [Fact]
    public async Task GrowthEmitsZeroForAnEmptyBucketInsideTheObservedPeriod()
    {
        var harness = Build();
        harness.Context.Users.Add(AUser(To.AddDays(-5)));
        await harness.Context.SaveChangesAsync();

        var result = await harness.Service.GetGrowthAsync(Window(7), cacheKey: null);

        var insideEmpty = Assert.Single(result.Series, point => point.BucketStart == To.Date.AddDays(-3));
        Assert.Equal(0, insideEmpty.NewUsers);
        // ObservedFrom is a day boundary: the earliest row date, not the earliest instant.
        Assert.Equal(To.Date.AddDays(-5), result.ObservedFrom);
    }

    [Fact]
    public async Task GrowthReturnsNothingObservedWhenEverySourceIsEmpty()
    {
        var harness = Build();

        var result = await harness.Service.GetGrowthAsync(Window(7), cacheKey: null);

        Assert.Null(result.ObservedFrom);
        Assert.Equal(8, result.Series.Count);
        Assert.All(result.Series, point => Assert.Equal(0, point.NewUsers));
        Assert.Equal(GrowthTotalsDto.Empty, result.Totals);
    }

    [Fact]
    public async Task GrowthExcludesSoftDeletedUsers()
    {
        var harness = Build();
        var deleted = AUser(To.AddDays(-2));
        deleted.DeletedAt = To.AddDays(-1);
        harness.Context.Users.Add(deleted);
        await harness.Context.SaveChangesAsync();

        var result = await harness.Service.GetGrowthAsync(Window(7), cacheKey: null);

        Assert.Equal(0, result.Totals.NewUsers);
        Assert.Null(result.ObservedFrom);
    }

    [Fact]
    public async Task GrowthMarksTheOpenTrailingBucketPartial()
    {
        var harness = Build();

        var result = await harness.Service.GetGrowthAsync(Window(7), cacheKey: null);

        // The last bucket is [today, tomorrow) and `to` is noon today, so it has not closed.
        Assert.True(result.Series[^1].IsPartial);
        // The first bucket is clipped by `from`, so it is partial as well.
        Assert.True(result.Series[0].IsPartial);
        Assert.Equal(2, result.Series.Count(point => point.IsPartial));
    }

    [Fact]
    public async Task GrowthMarksALeadingClippedBucketPartial()
    {
        var harness = Build();
        // Start six hours into a calendar day, so the first bucket is clipped by `from` rather
        // than coinciding with its boundary.
        var from = To.AddDays(-3).Date.AddHours(6);
        harness.Context.Users.Add(AUser(To.AddDays(-3).AddHours(3)));
        await harness.Context.SaveChangesAsync();
        var window = new BusinessWindow(
            from, To, "day", BusinessKpiValidation.CountBuckets(from, To, "day"));

        var result = await harness.Service.GetGrowthAsync(window, cacheKey: null);

        Assert.Equal(To.AddDays(-3).Date, result.Series[0].BucketStart);
        Assert.True(result.Series[0].IsPartial);
        Assert.True(result.Series[^1].IsPartial);
    }

    [Fact]
    public async Task GrowthFoldsDaysIntoIsoWeeks()
    {
        var harness = Build();
        // Sunday 20 Sep 2026 is `to`; the previous Monday is 14 Sep.
        harness.Context.Users.AddRange(
            AUser(new DateTime(2026, 9, 14, 1, 0, 0, DateTimeKind.Utc)),
            AUser(new DateTime(2026, 9, 18, 1, 0, 0, DateTimeKind.Utc)),
            AUser(new DateTime(2026, 9, 20, 1, 0, 0, DateTimeKind.Utc)));
        await harness.Context.SaveChangesAsync();

        var result = await harness.Service.GetGrowthAsync(Window(7, "week"), cacheKey: null);

        // 13 Sep (a Sunday) truncates to the ISO week of Monday 7 Sep; the window therefore
        // covers two week buckets, and all three users fall into the second.
        Assert.Equal(2, result.Series.Count);
        var week = Assert.Single(result.Series, point => point.BucketStart == new DateTime(2026, 9, 14, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal(3, week.NewUsers);
    }

    [Fact]
    public async Task GrowthFoldsDaysIntoCalendarMonthsAcrossAYearBoundary()
    {
        var harness = Build();
        var to = new DateTime(2027, 1, 10, 0, 0, 0, DateTimeKind.Utc);
        harness.Context.Users.AddRange(
            AUser(new DateTime(2026, 12, 20, 0, 0, 0, DateTimeKind.Utc)),
            AUser(new DateTime(2027, 1, 5, 0, 0, 0, DateTimeKind.Utc)),
            AUser(new DateTime(2027, 1, 6, 0, 0, 0, DateTimeKind.Utc)));
        await harness.Context.SaveChangesAsync();

        var window = new BusinessWindow(
            to.AddDays(-30), to, "month", BusinessKpiValidation.CountBuckets(to.AddDays(-30), to, "month"));
        var result = await harness.Service.GetGrowthAsync(window, cacheKey: null);

        var december = Assert.Single(result.Series, point => point.BucketStart == new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal(1, december.NewUsers);
        var january = Assert.Single(result.Series, point => point.BucketStart == new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal(2, january.NewUsers);
    }

    [Fact]
    public async Task GrowthEchoesTheWindowItComputedOver()
    {
        var harness = Build();
        var window = Window(30);

        var result = await harness.Service.GetGrowthAsync(window, cacheKey: null);

        Assert.Equal(window.From, result.Window.From);
        Assert.Equal(window.To, result.Window.To);
        Assert.Equal("day", result.Window.Granularity);
        Assert.Equal("UTC", result.Window.TimeZone);
        Assert.Equal(window.BucketCount, result.Window.BucketCount);
    }

    [Fact]
    public async Task GrowthReportsCleanDataQualityWhenEverySourceAnswered()
    {
        var harness = Build();

        var result = await harness.Service.GetGrowthAsync(Window(7), cacheKey: null);

        Assert.True(result.DataQuality.UserAttributionAvailable);
        Assert.Empty(result.DataQuality.Notes);
    }

    // ── S-45 active users ─────────────────────────────────────────────────────────────────

    private static ApiRequestMetric DayMetric(DateTime day, Guid? userId, Guid? organizationId = null, long requests = 1) => new()
    {
        OrganizationId = organizationId,
        UserId = userId,
        RouteTemplate = "/api/v1/things/{id}",
        HttpMethod = "GET",
        StatusCode = 200,
        StatusClass = "2xx",
        WindowStart = day.Date,
        WindowSize = "day",
        RequestCount = requests,
        BucketCounts = [1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1],
    };

    [Fact]
    public async Task ActiveUsersCountsDistinctUsersPerDay()
    {
        var harness = Build();
        var user1 = Guid.CreateVersion7();
        var user2 = Guid.CreateVersion7();
        var org = Guid.CreateVersion7();
        var day = To.Date.AddDays(-2);

        harness.Context.ApiRequestMetrics.AddRange(
            DayMetric(day, user1, org),
            DayMetric(day, user1, org, requests: 5),
            DayMetric(day, user2, org));
        await harness.Context.SaveChangesAsync();

        var result = await harness.Service.GetActiveUsersAsync(Window(7), cacheKey: null);

        var point = Assert.Single(result.Series, p => p.BucketStart == day);
        Assert.Equal(2, point.ActiveUsers);
        Assert.Equal(1, point.ActiveOrganizations);
    }

    [Fact]
    public async Task ActiveUsersIsNullNotZeroWhenNoRowIsAttributed()
    {
        var harness = Build();
        // Rows exist, but every one is unattributed (BR-6.1).
        harness.Context.ApiRequestMetrics.AddRange(
            DayMetric(To.Date.AddDays(-2), userId: null),
            DayMetric(To.Date.AddDays(-1), userId: null));
        await harness.Context.SaveChangesAsync();

        var result = await harness.Service.GetActiveUsersAsync(Window(7), cacheKey: null);

        Assert.All(result.Series, point => Assert.Null(point.ActiveUsers));
        Assert.False(result.DataQuality.UserAttributionAvailable);
        Assert.Contains(result.DataQuality.Notes, note => note.Contains("attribution", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ActiveUsersCountsZeroWhenAttributionWorksButTheDayWasQuiet()
    {
        var harness = Build();
        // An attributed row exists inside the window, so the measure is available.
        harness.Context.ApiRequestMetrics.Add(DayMetric(To.Date.AddDays(-1), Guid.CreateVersion7()));
        await harness.Context.SaveChangesAsync();

        var result = await harness.Service.GetActiveUsersAsync(Window(7), cacheKey: null);

        var quiet = Assert.Single(result.Series, point => point.BucketStart == To.Date.AddDays(-4));
        Assert.Equal(0, quiet.ActiveUsers);
    }

    [Fact]
    public async Task ActiveUsersIgnoresHourRows()
    {
        var harness = Build();
        var hour = DayMetric(To.Date.AddDays(-2), Guid.CreateVersion7());
        hour.WindowSize = "hour";
        hour.WindowStart = To.Date.AddDays(-2).AddHours(5);
        harness.Context.ApiRequestMetrics.Add(hour);
        await harness.Context.SaveChangesAsync();

        var result = await harness.Service.GetActiveUsersAsync(Window(7), cacheKey: null);

        Assert.All(result.Series, point => Assert.Null(point.ActiveUsers));
    }

    [Fact]
    public async Task ActiveUsersReportsRollingDauWauAndMauOverTheTrailingWindows()
    {
        var harness = Build();
        var recent = Guid.CreateVersion7();
        var weekly = Guid.CreateVersion7();
        var monthly = Guid.CreateVersion7();

        harness.Context.ApiRequestMetrics.AddRange(
            DayMetric(To.Date, recent),
            DayMetric(To.Date.AddDays(-5), weekly),
            DayMetric(To.Date.AddDays(-20), monthly));
        await harness.Context.SaveChangesAsync();

        var result = await harness.Service.GetActiveUsersAsync(Window(40), cacheKey: null);

        Assert.Equal(1, result.Rolling.Dau);
        Assert.Equal(2, result.Rolling.Wau);
        Assert.Equal(3, result.Rolling.Mau);
        Assert.NotNull(result.Rolling.Stickiness);
        Assert.Equal(1d / 3d, result.Rolling.Stickiness!.Value, 6);
    }

    [Fact]
    public async Task RollingReadingsAreNullWhenNothingIsAttributed()
    {
        var harness = Build();

        var result = await harness.Service.GetActiveUsersAsync(Window(40), cacheKey: null);

        Assert.Null(result.Rolling.Dau);
        Assert.Null(result.Rolling.Wau);
        Assert.Null(result.Rolling.Mau);
        Assert.Null(result.Rolling.Stickiness);
    }

    [Fact]
    public async Task StickinessIsNullWhenMauIsUnavailable()
    {
        var harness = Build();
        // Attributed rows only outside the trailing 30 days: Mau is 0, so stickiness is null.
        harness.Context.ApiRequestMetrics.Add(DayMetric(To.Date.AddDays(-35), Guid.CreateVersion7()));
        await harness.Context.SaveChangesAsync();

        var result = await harness.Service.GetActiveUsersAsync(Window(40), cacheKey: null);

        // No attributed row inside the trailing 30 days, so Mau is unavailable (null), never 0.
        Assert.Null(result.Rolling.Mau);
        Assert.Null(result.Rolling.Stickiness);
    }

    [Fact]
    public async Task ActiveUsersSurfacesTheUnresolvedAttributionCount()
    {
        var harness = Build();
        harness.Context.ApiRequestMetrics.Add(DayMetric(To.Date.AddDays(-1), Guid.CreateVersion7()));
        await harness.Context.SaveChangesAsync();

        var result = await harness.Service.GetActiveUsersAsync(Window(7), cacheKey: null);

        // The service reads the process-wide map's counter; with no traffic through the
        // middleware it is zero, and the field is present rather than absent.
        Assert.Equal(0, result.DataQuality.UnresolvedAttributionCount);
    }

    [Fact]
    public async Task ActiveUsersMarksTheOpenTrailingBucketPartial()
    {
        var harness = Build();
        harness.Context.ApiRequestMetrics.Add(DayMetric(To.Date.AddDays(-1), Guid.CreateVersion7()));
        await harness.Context.SaveChangesAsync();

        var result = await harness.Service.GetActiveUsersAsync(Window(7), cacheKey: null);

        Assert.True(result.Series[^1].IsPartial);
        Assert.Equal(2, result.Series.Count(point => point.IsPartial));
    }

    // ── S-46 plan mix ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PlanMixClassifiesOnlySeedAsFree()
    {
        var harness = Build();
        foreach (var tier in new[]
                 {
                     PlanTier.Seed, PlanTier.Seed, PlanTier.Bloom, PlanTier.Orchid, PlanTier.Enterprise,
                 })
        {
            var organization = AnOrganization(To);
            organization.PlanTier = tier;
            harness.Context.Organizations.Add(organization);
        }

        await harness.Context.SaveChangesAsync();

        var result = await harness.Service.GetPlanMixAsync(cacheKey: null);

        Assert.Equal(2, result.Free.OrganizationCount);
        Assert.Equal(3, result.Premium.OrganizationCount);
        Assert.Equal(5, result.OrganizationsTotal);
        Assert.True(Assert.Single(result.Tiers, tier => tier.PlanTier == "Seed").IsFree);
        Assert.All(
            result.Tiers.Where(tier => tier.PlanTier != "Seed"),
            tier => Assert.False(tier.IsFree));
    }

    [Fact]
    public async Task PlanMixCountsAnOrganizationWithNoBillingRowInTheTierAxis()
    {
        // The D-8 regression test: an organization that never changed or cancelled its plan has
        // no OrganizationSubscriptions row, so a naive subscription count would miss it.
        var harness = Build();
        var billingOrg = AnOrganization(To);
        billingOrg.PlanTier = PlanTier.Bloom;
        var freeOrg = AnOrganization(To);
        freeOrg.PlanTier = PlanTier.Seed;
        harness.Context.Organizations.AddRange(billingOrg, freeOrg);
        harness.Context.OrganizationSubscriptions.Add(new OrganizationSubscription
        {
            OrganizationId = billingOrg.Id,
            PlanTier = PlanTier.Bloom,
            Status = SubscriptionStatus.Active,
            PriceLkr = 3500m,
        });
        await harness.Context.SaveChangesAsync();

        var result = await harness.Service.GetPlanMixAsync(cacheKey: null);

        Assert.Equal(2, result.OrganizationsTotal);
        Assert.Equal(1, result.OrganizationsWithBillingRow);
        Assert.Equal(1, result.Free.OrganizationCount);
        Assert.Equal(1, result.Premium.OrganizationCount);
        Assert.Equal(1, Assert.Single(result.Tiers, tier => tier.PlanTier == "Bloom").BilledSubscriptionCount);
        Assert.Equal(0, Assert.Single(result.Tiers, tier => tier.PlanTier == "Seed").BilledSubscriptionCount);
        // BilledSubscriptionCount is smaller than the organization total, visibly.
        Assert.NotEqual(result.OrganizationsTotal, result.Tiers.Sum(tier => tier.BilledSubscriptionCount));
    }

    [Fact]
    public async Task PlanMixExcludesASuspendedOrganizationFromTheActiveCountButKeepsItInTheTotal()
    {
        var harness = Build();
        var active = AnOrganization(To);
        active.PlanTier = PlanTier.Rose;
        var suspended = AnOrganization(To, isActive: false);
        suspended.PlanTier = PlanTier.Rose;
        suspended.SuspendedAt = To.AddDays(-1);
        harness.Context.Organizations.AddRange(active, suspended);
        await harness.Context.SaveChangesAsync();

        var result = await harness.Service.GetPlanMixAsync(cacheKey: null);

        var rose = Assert.Single(result.Tiers, tier => tier.PlanTier == "Rose");
        Assert.Equal(2, rose.OrganizationCount);
        Assert.Equal(1, rose.ActiveOrganizationCount);
    }

    [Fact]
    public async Task PlanMixDoesNotCountPastDueOrCancelledSubscriptionsAsBilled()
    {
        var harness = Build();
        var org = AnOrganization(To);
        org.PlanTier = PlanTier.Orchid;
        harness.Context.Organizations.Add(org);
        harness.Context.OrganizationSubscriptions.Add(new OrganizationSubscription
        {
            OrganizationId = org.Id,
            PlanTier = PlanTier.Orchid,
            Status = SubscriptionStatus.PastDue,
            PriceLkr = 9000m,
        });
        await harness.Context.SaveChangesAsync();

        var result = await harness.Service.GetPlanMixAsync(cacheKey: null);

        var orchid = Assert.Single(result.Tiers, tier => tier.PlanTier == "Orchid");
        Assert.Equal(1, orchid.OrganizationCount);
        Assert.Equal(0, orchid.BilledSubscriptionCount);
        Assert.Equal(0m, orchid.MonthlyPriceLkr);
        Assert.Equal(1, result.OrganizationsWithBillingRow);
    }

    [Fact]
    public async Task PlanMixCountsUsersThroughActiveMembershipsNotTheLegacyUserColumn()
    {
        var harness = Build();
        var org = AnOrganization(To);
        org.PlanTier = PlanTier.Bloom;
        var member = AUser(To);
        var nonMember = AUser(To);
        // The legacy denormalised column points at the same org string for both users; only the
        // membership row should count.
        member.OrganizationId = org.Id.ToString();
        nonMember.OrganizationId = org.Id.ToString();
        harness.Context.Organizations.Add(org);
        harness.Context.Users.AddRange(member, nonMember);
        harness.Context.OrganizationMemberships.Add(new OrganizationMembership
        {
            OrganizationId = org.Id,
            UserId = member.Id,
            BoutiqueRole = "org:boutique_owner",
            Status = MembershipStatus.Active,
        });
        await harness.Context.SaveChangesAsync();

        var result = await harness.Service.GetPlanMixAsync(cacheKey: null);

        Assert.Equal(1, Assert.Single(result.Tiers, tier => tier.PlanTier == "Bloom").UserCount);
        Assert.Equal(1, result.Free.UserCount + result.Premium.UserCount);
    }

    [Fact]
    public async Task PlanMixSumsListPricesOverBilledRowsOnly()
    {
        var harness = Build();
        var bloom = AnOrganization(To);
        bloom.PlanTier = PlanTier.Bloom;
        var orchid = AnOrganization(To);
        orchid.PlanTier = PlanTier.Orchid;
        harness.Context.Organizations.AddRange(bloom, orchid);
        harness.Context.OrganizationSubscriptions.AddRange(
            new OrganizationSubscription
            {
                OrganizationId = bloom.Id,
                PlanTier = PlanTier.Bloom,
                Status = SubscriptionStatus.Active,
                PriceLkr = 3500m,
            },
            new OrganizationSubscription
            {
                OrganizationId = orchid.Id,
                PlanTier = PlanTier.Orchid,
                Status = SubscriptionStatus.Trialing,
                PriceLkr = 9000m,
            });
        await harness.Context.SaveChangesAsync();

        var result = await harness.Service.GetPlanMixAsync(cacheKey: null);

        Assert.Equal(12500m, result.TotalMonthlyPriceLkr);
        Assert.Equal(12500m, result.Free.MonthlyPriceLkr + result.Premium.MonthlyPriceLkr);
    }

    [Fact]
    public async Task PlanMixShareOfOrganizationsIsZeroRatherThanNaNWhenThereAreNone()
    {
        var harness = Build();

        var result = await harness.Service.GetPlanMixAsync(cacheKey: null);

        Assert.Equal(0, result.OrganizationsTotal);
        Assert.Equal(0d, result.Free.ShareOfOrganizations);
        Assert.Equal(0d, result.Premium.ShareOfOrganizations);
    }

    [Fact]
    public async Task PlanMixAlwaysReportsAllFiveTiersEvenWhenEmpty()
    {
        var harness = Build();

        var result = await harness.Service.GetPlanMixAsync(cacheKey: null);

        Assert.Equal(
            new[] { "Bloom", "Enterprise", "Orchid", "Rose", "Seed" },
            result.Tiers.Select(tier => tier.PlanTier).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(To, result.AsOf);
    }

    [Fact]
    public async Task PlanMixSharesSumToOne()
    {
        var harness = Build();
        foreach (var tier in new[] { PlanTier.Seed, PlanTier.Bloom, PlanTier.Orchid })
        {
            var organization = AnOrganization(To);
            organization.PlanTier = tier;
            harness.Context.Organizations.Add(organization);
        }

        await harness.Context.SaveChangesAsync();

        var result = await harness.Service.GetPlanMixAsync(cacheKey: null);

        Assert.Equal(1d / 3d, result.Free.ShareOfOrganizations, 6);
        Assert.Equal(2d / 3d, result.Premium.ShareOfOrganizations, 6);
    }
}
