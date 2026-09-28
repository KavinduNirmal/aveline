using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Analytics.Jobs;
using Aveline.Api.Modules.Analytics.Models;
using Aveline.Api.Modules.Audit.Models;
using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Organizations.Models;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Tests;

/// <summary>
/// Business KPIs phase 3 (§5.4.9): the one-shot audit-ledger backfill. It reconstructs a tier
/// from <c>AfterJson</c> and **must not** reproduce the shipped
/// <c>BillingStatisticsService</c> default of <c>"Grow"</c>, a tier that does not exist in
/// <see cref="PlanTier"/> (the plan's D-1 defect).
/// </summary>
public class SubscriptionBackfillTests
{
    private static readonly DateTime From = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

    private static AppDbContext Context() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"Backfill_{Guid.NewGuid()}")
            .Options);

    private static Organization AnOrganization(Guid id, PlanTier tier = PlanTier.Seed) =>
        new()
        {
            Id = id,
            Name = "Atelier",
            Slug = $"atelier-{id:N}",
            OwnerUserId = Guid.CreateVersion7(),
            PlanTier = tier,
            CreatedAt = From.AddDays(-30),
        };

    private static AuditLogEntry APlanChange(
        Guid organizationId,
        string? beforeJson,
        string? afterJson,
        DateTime? createdAt = null) =>
        new()
        {
            OrganizationId = organizationId,
            Action = "org.plan.changed",
            EntityType = "Organization",
            EntityId = organizationId.ToString(),
            BeforeJson = beforeJson,
            AfterJson = afterJson,
            CreatedAt = createdAt ?? From.AddDays(1),
        };

    // ── D-1: never a phantom tier ─────────────────────────────────────────────────────────

    [Fact]
    public void AnUnparseablePayloadYieldsNullRatherThanAPhantomTier()
    {
        var tier = SubscriptionBackfill.ReconstructTier("{ this is not json", "PlanTier");

        Assert.Null(tier);
    }

    [Fact]
    public void AJsonPayloadWithoutThePropertyYieldsNull()
    {
        Assert.Null(SubscriptionBackfill.ReconstructTier("""{"SomethingElse":"Bloom"}""", "PlanTier"));
        Assert.Null(SubscriptionBackfill.ReconstructTier(null, "PlanTier"));
        Assert.Null(SubscriptionBackfill.ReconstructTier("   ", "PlanTier"));
    }

    [Fact]
    public void ATierOutsideTheEnumYieldsNull()
    {
        // The shipped defect's literal. "Grow" is not in PlanTier, so it must never be emitted.
        Assert.Null(SubscriptionBackfill.ReconstructTier("""{"PlanTier":"Grow"}""", "PlanTier"));
        Assert.Null(SubscriptionBackfill.ReconstructTier("""{"PlanTier":"Blossom"}""", "PlanTier"));
    }

    [Theory]
    [InlineData("Seed")]
    [InlineData("Bloom")]
    [InlineData("Orchid")]
    [InlineData("Rose")]
    [InlineData("Enterprise")]
    public void AValidTierIsReconstructedCaseInsensitively(string tier)
    {
        Assert.Equal(
            Enum.Parse<PlanTier>(tier),
            SubscriptionBackfill.ReconstructTier($$"""{"PlanTier":"{{tier}}"}""", "PlanTier"));
        Assert.Equal(
            Enum.Parse<PlanTier>(tier),
            SubscriptionBackfill.ReconstructTier($$"""{"PlanTier":"{{tier.ToLowerInvariant()}}"}""", "PlanTier"));
    }

    [Fact]
    public void ANumericTierYieldsNullRatherThanACast()
    {
        Assert.Null(SubscriptionBackfill.ReconstructTier("""{"PlanTier":3}""", "PlanTier"));
    }

    // ── The backfill itself ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task APlanChangeProducesBackfilledRowsForTheDaysItGoverned()
    {
        await using var db = Context();
        var organizationId = Guid.CreateVersion7();
        db.Organizations.Add(AnOrganization(organizationId, PlanTier.Enterprise));
        db.AuditLogEntries.AddRange(
            APlanChange(
                organizationId,
                """{"PlanTier":"Seed"}""",
                """{"PlanTier":"Bloom"}""",
                From.AddDays(1)),
            APlanChange(
                organizationId,
                """{"PlanTier":"Bloom"}""",
                """{"PlanTier":"Orchid"}""",
                From.AddDays(3)));
        await db.SaveChangesAsync();

        var written = await SubscriptionBackfill.RunAsync(
            db, From, From.AddDays(4), maxWindowDays: 400);

        var rows = await db.OrganizationSubscriptionSnapshots
            .OrderBy(row => row.SnapshotDay)
            .ToListAsync();
        Assert.Equal(5, written);
        Assert.All(rows, row => Assert.True(row.IsBackfilled));
        Assert.All(rows, row => Assert.False(row.HasBillingRow));
        // Before the earliest change the organization's live tier is the only evidence.
        Assert.Equal(PlanTier.Enterprise, rows[0].PlanTier);
        // Then each change takes effect on the day it happened.
        Assert.Equal(
            new[] { PlanTier.Enterprise, PlanTier.Bloom, PlanTier.Bloom, PlanTier.Orchid, PlanTier.Orchid },
            rows.Select(row => row.PlanTier).ToArray());
    }

    [Fact]
    public async Task AnUnparseableAuditRowIsSkippedAndNeverWritesAPhantomTier()
    {
        await using var db = Context();
        var organizationId = Guid.CreateVersion7();
        db.Organizations.Add(AnOrganization(organizationId, PlanTier.Seed));
        db.AuditLogEntries.Add(APlanChange(organizationId, "not json", "{ also not json", From.AddDays(1)));
        await db.SaveChangesAsync();

        await SubscriptionBackfill.RunAsync(db, From, From.AddDays(3), maxWindowDays: 400);

        var rows = await db.OrganizationSubscriptionSnapshots.ToListAsync();
        // The unparseable payload contributes no tier of its own, so every row falls back to the
        // organization's live tier and never to a literal outside the enum.
        Assert.NotEmpty(rows);
        Assert.All(rows, row => Assert.Equal(PlanTier.Seed, row.PlanTier));
        Assert.DoesNotContain(rows, row => row.PlanTier.ToString() == "Grow");
    }

    [Fact]
    public async Task TheBackfillIsIdempotent()
    {
        await using var db = Context();
        var organizationId = Guid.CreateVersion7();
        db.Organizations.Add(AnOrganization(organizationId, PlanTier.Orchid));
        db.AuditLogEntries.Add(APlanChange(
            organizationId, """{"PlanTier":"Seed"}""", """{"PlanTier":"Orchid"}""", From.AddDays(1)));
        await db.SaveChangesAsync();

        await SubscriptionBackfill.RunAsync(db, From, From.AddDays(3), maxWindowDays: 400);
        var first = await db.OrganizationSubscriptionSnapshots.CountAsync();
        await SubscriptionBackfill.RunAsync(db, From, From.AddDays(3), maxWindowDays: 400);
        var second = await db.OrganizationSubscriptionSnapshots.CountAsync();

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task TheBackfillIsCappedAtTheConfiguredWindow()
    {
        await using var db = Context();
        var organizationId = Guid.CreateVersion7();
        db.Organizations.Add(AnOrganization(organizationId));
        db.AuditLogEntries.Add(APlanChange(
            organizationId, """{"PlanTier":"Seed"}""", """{"PlanTier":"Bloom"}""", From));
        await db.SaveChangesAsync();

        var written = await SubscriptionBackfill.RunAsync(
            db, From.AddDays(-400), From, maxWindowDays: 5);

        Assert.True(written <= 10, $"A capped backfill wrote {written} rows for a 5-day cap.");
    }

    [Fact]
    public async Task TheBackfillDoesNotOverwriteARealSnapshotDay()
    {
        await using var db = Context();
        var organizationId = Guid.CreateVersion7();
        db.Organizations.Add(AnOrganization(organizationId, PlanTier.Rose));
        db.OrganizationSubscriptionSnapshots.Add(new OrganizationSubscriptionSnapshot
        {
            OrganizationId = organizationId,
            SnapshotDay = From.AddDays(1),
            PlanTier = PlanTier.Rose,
            Status = SubscriptionStatus.Active,
            BillingCycle = BillingCycle.Monthly,
            IsBackfilled = false,
        });
        db.AuditLogEntries.Add(APlanChange(
            organizationId, """{"PlanTier":"Seed"}""", """{"PlanTier":"Bloom"}""", From));
        await db.SaveChangesAsync();

        await SubscriptionBackfill.RunAsync(db, From, From.AddDays(3), maxWindowDays: 400);

        var real = await db.OrganizationSubscriptionSnapshots
            .SingleAsync(row => row.SnapshotDay == From.AddDays(1));
        Assert.False(real.IsBackfilled);
        Assert.Equal(PlanTier.Rose, real.PlanTier);
    }

    [Fact]
    public async Task AnOrganizationWithNoAuditHistoryGetsNoBackfilledRow()
    {
        await using var db = Context();
        var organizationId = Guid.CreateVersion7();
        db.Organizations.Add(AnOrganization(organizationId, PlanTier.Enterprise));
        await db.SaveChangesAsync();

        var written = await SubscriptionBackfill.RunAsync(
            db, From, From.AddDays(3), maxWindowDays: 400);

        Assert.Equal(0, written);
        Assert.Empty(await db.OrganizationSubscriptionSnapshots.ToListAsync());
    }

    [Fact]
    public async Task ReconstructedBucketsCountOrganizationsNotBillingRows()
    {
        // The same D-8 invariant, asserted on the historical path.
        await using var db = Context();
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();
        db.Organizations.AddRange(AnOrganization(first, PlanTier.Bloom), AnOrganization(second, PlanTier.Bloom));
        db.AuditLogEntries.Add(APlanChange(
            first, """{"PlanTier":"Seed"}""", """{"PlanTier":"Bloom"}""", From.AddDays(1)));
        db.AuditLogEntries.Add(APlanChange(
            second, """{"PlanTier":"Seed"}""", """{"PlanTier":"Bloom"}""", From.AddDays(2)));
        await db.SaveChangesAsync();

        await SubscriptionBackfill.RunAsync(db, From, From.AddDays(3), maxWindowDays: 400);

        var rows = await db.OrganizationSubscriptionSnapshots
            .Where(row => row.SnapshotDay == From.AddDays(3))
            .ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(0, rows.Count(row => row.HasBillingRow));
    }
}
