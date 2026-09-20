using Aveline.Api.Authorization;
using Aveline.Api.Common.Jobs;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Billing.Jobs;
using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Revenue.Models;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aveline.Api.Tests;

/// <summary>
/// Revenue Ledger R2 (issue #343) — the subscription-charge writer inside the period rollover.
///
/// There is no payment-provider client in this repository, so this cannot book collected money. It
/// writes <see cref="IncomeChargeBasis.Derived"/>: what the list price says *should* be billed. The
/// interesting assertions are therefore the negative ones — the cases that write **nothing**.
///
/// Each test owns its own store, because a shared one would let one test's organization roll over
/// inside another's assertions.
/// </summary>
public class RevenueRolloverWriterTests
{
    private sealed class NoopJobLock : IDistributedJobLock
    {
        public Task<IAsyncDisposable?> TryAcquireAsync(
            string jobName, TimeSpan? lease = null, CancellationToken cancellationToken = default)
            => Task.FromResult<IAsyncDisposable?>(new Handle());

        private sealed class Handle : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    /// <summary>One isolated store plus the scope factory the job runs against it with.</summary>
    private sealed class Fixture : IDisposable
    {
        private readonly ServiceProvider _provider;

        public Fixture()
        {
            DatabaseName = $"RevRollover_{Guid.NewGuid()}";
            var services = new ServiceCollection();
            services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(DatabaseName));
            services.AddScoped<Modules.Billing.Domain.IEntitlementResolver,
                Modules.Billing.Domain.EntitlementResolver>();
            services.AddScoped<Modules.Billing.Repositories.IEntitlementRepository,
                Modules.Billing.Repositories.EntitlementRepository>();
            _provider = services.BuildServiceProvider();
        }

        public string DatabaseName { get; }

        public AppDbContext Context() =>
            new(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(databaseName: DatabaseName)
                .Options);

        public BillingPeriodRolloverJob CreateJob() => new(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            new NoopJobLock(),
            NullLogger<BillingPeriodRolloverJob>.Instance);

        public void Dispose() => _provider.Dispose();
    }

    /// <summary>
    /// Seeds an organization whose billing period has already ended, so the rollover has work to do.
    /// </summary>
    private static async Task<Guid> SeedEndedPeriodAsync(
        Fixture fixture,
        PlanTier tier,
        decimal priceLkr,
        SubscriptionStatus status = SubscriptionStatus.Active,
        bool seedSubscription = true)
    {
        var end = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var start = end.AddMonths(-1);

        await using var context = fixture.Context();
        var ownerId = Guid.CreateVersion7();
        context.Users.Add(new User
        {
            Id = ownerId, ClerkId = $"rr_{ownerId:N}", Email = "rr@aveline.lk", FirstName = "R",
            LastName = "R", Username = $"rr_{ownerId:N}", UserRole = Roles.Owner,
            OrganizationRole = "org:boutique_owner",
        });
        var org = new Organization
        {
            Name = "Rev Rollover", Slug = $"rr-{ownerId:N}", OwnerUserId = ownerId, PlanTier = tier,
        };
        context.Organizations.Add(org);
        await context.SaveChangesAsync();

        if (seedSubscription)
        {
            context.OrganizationSubscriptions.Add(new OrganizationSubscription
            {
                OrganizationId = org.Id,
                PlanTier = tier,
                BillingCycle = BillingCycle.Monthly,
                Status = status,
                CurrentPeriodStart = start,
                CurrentPeriodEnd = end,
                PriceLkr = priceLkr,
            });
        }

        context.UsageAccounts.Add(new UsageAccount
        {
            OrganizationId = org.Id,
            PeriodStart = start,
            PeriodEnd = end,
            MonthlyBlossomLimit = 150m,
            BlossomRemaining = 150m,
            PlanTierSnapshot = tier,
            Status = UsageAccountStatus.Active,
        });
        await context.SaveChangesAsync();
        return org.Id;
    }

    [Fact]
    public async Task TheRollover_WritesOneDerivedChargeForAPricedSubscription()
    {
        using var fixture = new Fixture();
        var orgId = await SeedEndedPeriodAsync(fixture, PlanTier.Bloom, priceLkr: 4500m);

        await fixture.CreateJob().RunAsync(default);

        await using var verify = fixture.Context();
        var entry = await verify.IncomeLedgerEntries.SingleAsync();
        Assert.Equal(orgId, entry.OrganizationId);
        Assert.Equal(IncomeEntryKind.SubscriptionCharge, entry.Kind);
        Assert.Equal(IncomeChargeBasis.Derived, entry.ChargeBasis);
        Assert.Equal(4500m, entry.Amount);
        Assert.Equal(IncomeSourceKind.SubscriptionBilling, entry.SourceKind);
        // The charge is for the period that just closed, not the one that just opened.
        Assert.Equal(new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc), entry.PeriodStart);
        Assert.Equal(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), entry.PeriodEnd);
        // A job-written row has no human actor; `null` is the established convention for that.
        Assert.Null(entry.RecordedByUserId);
    }

    /// <summary>
    /// The case that stops revenue being invented. `SubscriptionService` never assigns `PriceLkr`,
    /// so in production today this writer records nothing — which is the honest outcome, not a bug.
    /// </summary>
    [Fact]
    public async Task TheRollover_WritesNothingForAZeroPricedSubscription()
    {
        using var fixture = new Fixture();
        await SeedEndedPeriodAsync(fixture, PlanTier.Seed, priceLkr: 0m);

        await fixture.CreateJob().RunAsync(default);

        await using var verify = fixture.Context();
        Assert.Empty(await verify.IncomeLedgerEntries.ToListAsync());
    }

    [Fact]
    public async Task TheRollover_WritesNothingForACancelledSubscription()
    {
        using var fixture = new Fixture();
        await SeedEndedPeriodAsync(
            fixture, PlanTier.Bloom, priceLkr: 4500m, status: SubscriptionStatus.Cancelled);

        await fixture.CreateJob().RunAsync(default);

        await using var verify = fixture.Context();
        Assert.Empty(await verify.IncomeLedgerEntries.ToListAsync());
    }

    [Fact]
    public async Task TheRollover_WritesNothingWhenThereIsNoSubscriptionRow()
    {
        using var fixture = new Fixture();
        await SeedEndedPeriodAsync(
            fixture, PlanTier.Bloom, priceLkr: 4500m, seedSubscription: false);

        await fixture.CreateJob().RunAsync(default);

        await using var verify = fixture.Context();
        Assert.Empty(await verify.IncomeLedgerEntries.ToListAsync());
    }

    /// <summary>
    /// A re-run over the **same** period cannot double-book it. The dedup reference is the period
    /// start in ISO-8601, so a second pass collides on the ledger's own identity rather than writing
    /// a second charge.
    /// </summary>
    [Fact]
    public async Task TheRollover_RunTwiceOverTheSamePeriod_WritesOneCharge()
    {
        using var fixture = new Fixture();
        await SeedEndedPeriodAsync(fixture, PlanTier.Bloom, priceLkr: 4500m);

        await fixture.CreateJob().RunAsync(default);

        // Rewind to the same ended period the first run charged, so the second run has the same
        // work to do. Without the dedup guard this is the exact double-book.
        await using (var context = fixture.Context())
        {
            foreach (var account in context.UsageAccounts)
            {
                account.PeriodStart = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
                account.PeriodEnd = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
                account.IsClosed = false;
                account.ClosedAt = null;
            }
            foreach (var subscription in context.OrganizationSubscriptions)
            {
                subscription.CurrentPeriodStart = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
                subscription.CurrentPeriodEnd = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
            }
            await context.SaveChangesAsync();
        }

        await fixture.CreateJob().RunAsync(default);

        await using var verify = fixture.Context();
        Assert.Single(await verify.IncomeLedgerEntries.ToListAsync());
    }

    /// <summary>
    /// The rollover and the revenue writer are one unit of work, so a period that rolled over always
    /// has its allowance row and its charge row together.
    /// </summary>
    [Fact]
    public async Task TheRollover_WritesTheChargeAlongsideThePeriodAllowance()
    {
        using var fixture = new Fixture();
        await SeedEndedPeriodAsync(fixture, PlanTier.Bloom, priceLkr: 4500m);

        await fixture.CreateJob().RunAsync(default);

        await using var verify = fixture.Context();
        Assert.Contains(
            await verify.BlossomLedgerEntries.ToListAsync(),
            entry => entry.EntryType == BlossomLedgerEntryType.PeriodAllocation);
        Assert.Single(await verify.IncomeLedgerEntries.ToListAsync());
    }
}
