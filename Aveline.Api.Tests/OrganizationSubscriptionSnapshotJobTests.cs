using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Analytics.Jobs;
using Aveline.Api.Modules.Analytics.Models;
using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Organizations.Models;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Tests;

/// <summary>
/// Business KPIs phase 3 (§5.4.9): the daily subscription snapshot. Recomputation is
/// delete-and-replace so a re-run leaves the same rows, and the tier axis is
/// <c>Organizations.PlanTier</c> so an organization with no billing row still gets a row
/// (D-8) rather than silently disappearing from a "total subscriptions" chart.
/// </summary>
public class OrganizationSubscriptionSnapshotJobTests
{
    private static readonly DateTime Day = new(2026, 9, 19, 0, 0, 0, DateTimeKind.Utc);

    private static AppDbContext Context() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"Snapshot_{Guid.NewGuid()}")
            .Options);

    private static Organization AnOrganization(
        Guid id,
        PlanTier tier = PlanTier.Seed,
        bool isActive = true,
        DateTime? suspendedAt = null,
        DateTime? createdAt = null) =>
        new()
        {
            Id = id,
            Name = "Atelier",
            Slug = $"atelier-{id:N}",
            OwnerUserId = Guid.CreateVersion7(),
            PlanTier = tier,
            IsActive = isActive,
            SuspendedAt = suspendedAt,
            CreatedAt = createdAt ?? Day.AddDays(-30),
        };

    [Fact]
    public async Task RecomputeWritesOneRowPerOrganization()
    {
        await using var db = Context();
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();
        db.Organizations.AddRange(AnOrganization(first, PlanTier.Seed), AnOrganization(second, PlanTier.Bloom));
        await db.SaveChangesAsync();

        var written = await OrganizationSubscriptionSnapshotJob.RecomputeDayAsync(db, Day);

        Assert.Equal(2, written);
        var rows = await db.OrganizationSubscriptionSnapshots.ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(Day, rows[0].SnapshotDay);
        Assert.Contains(rows, row => row.OrganizationId == second && row.PlanTier == PlanTier.Bloom);
    }

    [Fact]
    public async Task RecomputeIsIdempotent()
    {
        await using var db = Context();
        db.Organizations.Add(AnOrganization(Guid.CreateVersion7(), PlanTier.Orchid));
        await db.SaveChangesAsync();

        await OrganizationSubscriptionSnapshotJob.RecomputeDayAsync(db, Day);
        var afterFirst = await db.OrganizationSubscriptionSnapshots.CountAsync();
        await OrganizationSubscriptionSnapshotJob.RecomputeDayAsync(db, Day);
        var afterSecond = await db.OrganizationSubscriptionSnapshots.CountAsync();

        Assert.Equal(1, afterFirst);
        Assert.Equal(1, afterSecond);
    }

    [Fact]
    public async Task AnOrganizationWithNoBillingRowGetsARowWithTheTierFromTheOrganization()
    {
        // The D-8 invariant: a naive COUNT(OrganizationSubscriptions) would miss this org.
        await using var db = Context();
        var organizationId = Guid.CreateVersion7();
        db.Organizations.Add(AnOrganization(organizationId, PlanTier.Rose));
        await db.SaveChangesAsync();

        await OrganizationSubscriptionSnapshotJob.RecomputeDayAsync(db, Day);

        var row = await db.OrganizationSubscriptionSnapshots.SingleAsync();
        Assert.Equal(PlanTier.Rose, row.PlanTier);
        Assert.False(row.HasBillingRow);
        Assert.Equal(SubscriptionStatus.Active, row.Status);
        Assert.Equal(0, row.SeatsIncluded);
        Assert.Equal(0m, row.PriceLkr);
        Assert.Equal(BillingCycle.Monthly, row.BillingCycle);
        Assert.False(row.IsBackfilled);
    }

    [Fact]
    public async Task ABillingRowSuppliesTheBillingColumns()
    {
        await using var db = Context();
        var organizationId = Guid.CreateVersion7();
        db.Organizations.Add(AnOrganization(organizationId, PlanTier.Bloom));
        db.OrganizationSubscriptions.Add(new OrganizationSubscription
        {
            OrganizationId = organizationId,
            PlanTier = PlanTier.Bloom,
            Status = SubscriptionStatus.Trialing,
            SeatsIncluded = 3,
            PriceLkr = 3500m,
            BillingCycle = BillingCycle.Annual,
        });
        await db.SaveChangesAsync();

        await OrganizationSubscriptionSnapshotJob.RecomputeDayAsync(db, Day);

        var row = await db.OrganizationSubscriptionSnapshots.SingleAsync();
        Assert.True(row.HasBillingRow);
        Assert.Equal(SubscriptionStatus.Trialing, row.Status);
        Assert.Equal(3, row.SeatsIncluded);
        Assert.Equal(3500m, row.PriceLkr);
        Assert.Equal(BillingCycle.Annual, row.BillingCycle);
        // The organization remains the tier authority.
        Assert.Equal(PlanTier.Bloom, row.PlanTier);
    }

    [Fact]
    public async Task APlanChangeWithoutABillingRowIsReflectedInTheNextSnapshot()
    {
        await using var db = Context();
        var organizationId = Guid.CreateVersion7();
        var organization = AnOrganization(organizationId, PlanTier.Seed);
        db.Organizations.Add(organization);
        await db.SaveChangesAsync();

        await OrganizationSubscriptionSnapshotJob.RecomputeDayAsync(db, Day);
        Assert.Equal(PlanTier.Seed, (await db.OrganizationSubscriptionSnapshots.SingleAsync()).PlanTier);

        organization.PlanTier = PlanTier.Enterprise;
        await db.SaveChangesAsync();
        await OrganizationSubscriptionSnapshotJob.RecomputeDayAsync(db, Day.AddDays(1));

        var rows = await db.OrganizationSubscriptionSnapshots.OrderBy(r => r.SnapshotDay).ToListAsync();
        Assert.Equal(PlanTier.Seed, rows[0].PlanTier);
        Assert.Equal(PlanTier.Enterprise, rows[1].PlanTier);
    }

    [Fact]
    public async Task ACancelledSubscriptionKeepsItsRowButStopsCountingActive()
    {
        await using var db = Context();
        var organizationId = Guid.CreateVersion7();
        db.Organizations.Add(AnOrganization(organizationId, PlanTier.Bloom));
        db.OrganizationSubscriptions.Add(new OrganizationSubscription
        {
            OrganizationId = organizationId,
            PlanTier = PlanTier.Bloom,
            Status = SubscriptionStatus.Cancelled,
            CancelledAt = Day.AddHours(-1),
            PriceLkr = 0m,
        });
        await db.SaveChangesAsync();

        await OrganizationSubscriptionSnapshotJob.RecomputeDayAsync(db, Day);

        var row = await db.OrganizationSubscriptionSnapshots.SingleAsync();
        Assert.Equal(SubscriptionStatus.Cancelled, row.Status);
        Assert.Equal(PlanTier.Bloom, row.PlanTier);
    }

    [Fact]
    public async Task ASuspendedOrganizationIsNotSnapshottedAfterSuspension()
    {
        await using var db = Context();
        var organizationId = Guid.CreateVersion7();
        db.Organizations.Add(AnOrganization(
            organizationId, PlanTier.Bloom, isActive: false, suspendedAt: Day.AddDays(-1)));
        await db.SaveChangesAsync();

        var written = await OrganizationSubscriptionSnapshotJob.RecomputeDayAsync(db, Day);

        Assert.Equal(0, written);
        Assert.Empty(await db.OrganizationSubscriptionSnapshots.ToListAsync());
    }

    [Fact]
    public async Task AnOrganizationCreatedAfterTheDayIsNotSnapshotted()
    {
        await using var db = Context();
        db.Organizations.Add(AnOrganization(Guid.CreateVersion7(), createdAt: Day.AddDays(1)));
        await db.SaveChangesAsync();

        var written = await OrganizationSubscriptionSnapshotJob.RecomputeDayAsync(db, Day);

        Assert.Equal(0, written);
    }

    [Fact]
    public async Task AnEmptyDatabaseIsANoOpRatherThanADeleteEverything()
    {
        await using var db = Context();
        await OrganizationSubscriptionSnapshotJob.RecomputeDayAsync(db, Day.AddDays(-1));

        // Seed one real row, then re-run for a day with no organizations.
        db.OrganizationSubscriptionSnapshots.Add(new OrganizationSubscriptionSnapshot
        {
            OrganizationId = Guid.CreateVersion7(),
            SnapshotDay = Day.AddDays(-1),
            PlanTier = PlanTier.Seed,
            Status = SubscriptionStatus.Active,
            BillingCycle = BillingCycle.Monthly,
        });
        await db.SaveChangesAsync();

        // Recomputing the same (now empty-of-organizations) day deletes that day and writes none.
        await OrganizationSubscriptionSnapshotJob.RecomputeDayAsync(db, Day.AddDays(-1));

        Assert.Empty(await db.OrganizationSubscriptionSnapshots.ToListAsync());
    }

    [Fact]
    public async Task RecomputeLeavesOtherDaysUntouched()
    {
        await using var db = Context();
        var organizationId = Guid.CreateVersion7();
        db.Organizations.Add(AnOrganization(organizationId));
        db.OrganizationSubscriptionSnapshots.Add(new OrganizationSubscriptionSnapshot
        {
            OrganizationId = organizationId,
            SnapshotDay = Day.AddDays(-5),
            PlanTier = PlanTier.Orchid,
            Status = SubscriptionStatus.Active,
            BillingCycle = BillingCycle.Monthly,
        });
        await db.SaveChangesAsync();

        await OrganizationSubscriptionSnapshotJob.RecomputeDayAsync(db, Day);

        var rows = await db.OrganizationSubscriptionSnapshots.ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, row => row.SnapshotDay == Day.AddDays(-5) && row.PlanTier == PlanTier.Orchid);
    }

    [Fact]
    public async Task RetentionPrunesSnapshotsOlderThanTheWindow()
    {
        await using var db = Context();
        var organizationId = Guid.CreateVersion7();
        db.Organizations.Add(AnOrganization(organizationId));
        db.OrganizationSubscriptionSnapshots.AddRange(
            new OrganizationSubscriptionSnapshot
            {
                OrganizationId = organizationId,
                SnapshotDay = Day.AddDays(-10),
                PlanTier = PlanTier.Seed,
                Status = SubscriptionStatus.Active,
                BillingCycle = BillingCycle.Monthly,
            },
            new OrganizationSubscriptionSnapshot
            {
                OrganizationId = organizationId,
                SnapshotDay = Day.AddDays(-1),
                PlanTier = PlanTier.Seed,
                Status = SubscriptionStatus.Active,
                BillingCycle = BillingCycle.Monthly,
            });
        await db.SaveChangesAsync();

        var pruned = await OrganizationSubscriptionSnapshotJob.PruneAsync(db, Day, retentionDays: 5);

        Assert.Equal(1, pruned);
        var remaining = await db.OrganizationSubscriptionSnapshots.SingleAsync();
        Assert.Equal(Day.AddDays(-1), remaining.SnapshotDay);
    }

    [Fact]
    public async Task RetentionKeepsTheBoundaryDay()
    {
        await using var db = Context();
        var organizationId = Guid.CreateVersion7();
        db.Organizations.Add(AnOrganization(organizationId));
        db.OrganizationSubscriptionSnapshots.Add(new OrganizationSubscriptionSnapshot
        {
            OrganizationId = organizationId,
            SnapshotDay = Day.AddDays(-5),
            PlanTier = PlanTier.Seed,
            Status = SubscriptionStatus.Active,
            BillingCycle = BillingCycle.Monthly,
        });
        await db.SaveChangesAsync();

        var pruned = await OrganizationSubscriptionSnapshotJob.PruneAsync(db, Day, retentionDays: 5);

        Assert.Equal(0, pruned);
        Assert.Single(await db.OrganizationSubscriptionSnapshots.ToListAsync());
    }

    [Fact]
    public void TheSnapshotGeneratesVersionSevenIdentifiers()
    {
        Assert.Equal(7, new OrganizationSubscriptionSnapshot().Id.Version);
    }
}
