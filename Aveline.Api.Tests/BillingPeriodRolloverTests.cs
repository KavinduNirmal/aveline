using Aveline.Api.Authorization;
using Aveline.Api.Common.Jobs;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Infrastructure.Eventing;
using Aveline.Api.Modules.Audit;
using Aveline.Api.Modules.Billing;
using Aveline.Api.Modules.Billing.Jobs;
using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Notifications.Models;
using Aveline.Api.Modules.Notifications.Services;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Payments;
using Aveline.Api.Modules.Payments.Domain;
using Aveline.Api.Modules.Payments.Providers;
using Aveline.Api.Modules.Payments.Services;
using Aveline.Api.Modules.Revenue;
using Aveline.Api.Modules.Revenue.Models;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Aveline.Api.Tests;

/// <summary>
/// Plan §10 Phase 4 — subscription renewal at the period rollover (issue #404).
///
/// The rollover already writes the <c>Derived</c> <c>SubscriptionCharge</c> once P1 prices the
/// subscription (<c>RevenueRolloverWriterTests</c> is that baseline). This class covers the half
/// Phase 4 adds: a <c>SubscriptionRenewal</c> intent through the payment module, and — when the
/// provider settles it — the <c>Verified</c> receipt that takes over the derived expectation's
/// identity, so <c>Derived</c> and <c>Verified</c> agree on the same amount and period.
///
/// The dunning schedule is exercised through the injected <see cref="TimeProvider"/>: attempts at
/// day 1, 3 and 7 after the failed period boundary, <c>PastDue</c> throughout, and <c>Expired</c>
/// at day 14 (decision Q3).
/// </summary>
public class BillingPeriodRolloverTests
{
    private static readonly DateTime PeriodStart = new(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime PeriodEnd = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

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

    /// <summary>A clock the dunning tests can move forward, standing in for the passage of days.</summary>
    private sealed class MutableClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class StubHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Aveline.Api.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    /// <summary>Captures what the job asked the notification module to deliver.</summary>
    private sealed class RecordingDispatcher : INotificationDispatcher
    {
        public List<Notification> Dispatched { get; } = [];

        public Task<NotificationRecord?> DispatchAsync(
            Notification notification, CancellationToken cancellationToken = default)
        {
            Dispatched.Add(notification);
            return Task.FromResult<NotificationRecord?>(null);
        }
    }

    /// <summary>
    /// One isolated store wired through the real modules, so the rollover, the payment stack and
    /// the revenue ledger are the production ones rather than a test double.
    /// </summary>
    private sealed class Fixture : IDisposable
    {
        private readonly ServiceProvider _provider;

        public Fixture(bool providerSettles = true)
        {
            Clock = new MutableClock(new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero));
            Dispatcher = new RecordingDispatcher();

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Payments:Provider"] = "mock",
                    ["Payments:Mock:Enabled"] = "true",
                    ["Payments:Mock:AutoSettle"] = "true",
                    ["Payments:Mock:WebhookSigningSecret"] = "whsec_rollover_test_secret",
                    ["Payments:Currency"] = "LKR",
                })
                .Build();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<TimeProvider>(Clock);
            services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(DatabaseName));
            services.AddSingleton<IEventBus, InMemoryEventBus>();
            services.AddBillingModule();
            services.AddRevenueModule(configuration);
            services.AddPaymentsModule(configuration, new StubHostEnvironment("Development"));
            services.AddAuditModule();
            // The notification module's own dispatcher needs SignalR; the seam under test is the
            // dispatcher call, so the fixture records it rather than wiring the whole channel stack.
            services.AddSingleton<INotificationDispatcher>(Dispatcher);
            // The mock resolves to RequiresAction with no credential; a default credential is what
            // makes a server-initiated renewal settle in place, exactly as a stored card would.
            services.AddKeyedSingleton<IPaymentProvider>(MockPaymentProvider.ProviderKey, (provider, _) =>
                new MockPaymentProvider(
                    provider.GetRequiredService<IOptions<PaymentsOptions>>(),
                    Clock,
                    NullLogger<MockPaymentProvider>.Instance,
                    provider.GetRequiredService<PaymentMetrics>(),
                    providerSettles ? MockPaymentProvider.SucceedToken : "4000000000000002"));

            _provider = services.BuildServiceProvider();
        }

        public string DatabaseName { get; } = $"Rollover_{Guid.NewGuid()}";

        public MutableClock Clock { get; }

        public RecordingDispatcher Dispatcher { get; }

        public AppDbContext Context() => new(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(databaseName: DatabaseName)
                .Options);

        public BillingPeriodRolloverJob CreateJob() => new(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            new NoopJobLock(),
            NullLogger<BillingPeriodRolloverJob>.Instance);

        public Task<int> RunRolloverAsync() => CreateJob().RunAsync(default);

        public void Dispose() => _provider.Dispose();
    }

    /// <summary>An organisation with a priced Bloom subscription whose period has already ended.</summary>
    private static async Task<Guid> SeedEndedPeriodAsync(
        Fixture fixture, decimal priceLkr = 3500m, bool cancelAtPeriodEnd = false)
    {
        await using var context = fixture.Context();
        var ownerId = Guid.CreateVersion7();
        context.Users.Add(new User
        {
            Id = ownerId,
            ClerkId = $"ro_{ownerId:N}",
            Email = "ro@aveline.lk",
            FirstName = "R",
            LastName = "O",
            Username = $"ro_{ownerId:N}",
            UserRole = "owner",
            OrganizationRole = "org:boutique_owner",
        });
        var organization = new Organization
        {
            Name = "Rollover Boutique",
            Slug = $"ro-{ownerId:N}",
            OwnerUserId = ownerId,
            PlanTier = PlanTier.Bloom,
        };
        context.Organizations.Add(organization);

        context.OrganizationSubscriptions.Add(new OrganizationSubscription
        {
            OrganizationId = organization.Id,
            PlanTier = PlanTier.Bloom,
            BillingCycle = BillingCycle.Monthly,
            Status = SubscriptionStatus.Active,
            CurrentPeriodStart = PeriodStart,
            CurrentPeriodEnd = PeriodEnd,
            PriceLkr = priceLkr,
            CancelAtPeriodEnd = cancelAtPeriodEnd,
            // A cancellation is requested through the API, which stamps CancelledAt; the rollover
            // only honours the request when the period boundary arrives.
            CancelledAt = cancelAtPeriodEnd ? PeriodStart : null,
        });

        context.UsageAccounts.Add(new UsageAccount
        {
            OrganizationId = organization.Id,
            PeriodStart = PeriodStart,
            PeriodEnd = PeriodEnd,
            MonthlyBlossomLimit = 750m,
            BlossomRemaining = 750m,
            PlanTierSnapshot = PlanTier.Bloom,
            Status = UsageAccountStatus.Active,
        });

        await context.SaveChangesAsync();
        return organization.Id;
    }

    /// <summary>
    /// The phase's first failing test: the <c>Derived</c> half already lands once P1 prices the
    /// subscription, so the <c>Verified</c> half is what this asserts.
    /// </summary>
    [Fact]
    public async Task PricedSubscription_WritesBothDerivedAndVerifiedOnSettlement()
    {
        using var fixture = new Fixture(providerSettles: true);
        var organizationId = await SeedEndedPeriodAsync(fixture);

        await fixture.RunRolloverAsync();

        await using var verify = fixture.Context();
        var entries = await verify.IncomeLedgerEntries
            .Where(entry => entry.OrganizationId == organizationId)
            .ToListAsync();

        var derived = Assert.Single(entries, entry => entry.ChargeBasis == IncomeChargeBasis.Derived);
        var verified = Assert.Single(entries, entry => entry.ChargeBasis == IncomeChargeBasis.Verified);

        // The two rows agree on the amount and the period: the receipt is for the charge the list
        // price said should be billed, not a second, independently-derived number.
        Assert.Equal(3500m, derived.Amount);
        Assert.Equal(3500m, verified.Amount);
        Assert.Equal(derived.PeriodStart, verified.PeriodStart);
        Assert.Equal(derived.PeriodEnd, verified.PeriodEnd);
        Assert.Equal(IncomeEntryKind.SubscriptionCharge, verified.Kind);
        // The derived expectation is superseded, never deleted (append-only).
        Assert.Equal(derived.Id, verified.SupersedesEntryId);
        Assert.Equal(IncomeEntryStatus.Voided, derived.Status);
    }

    [Fact]
    public async Task SuccessfulRenewal_AdvancesThePeriodAndLeavesTheSubscriptionActive()
    {
        using var fixture = new Fixture(providerSettles: true);
        var organizationId = await SeedEndedPeriodAsync(fixture);

        await fixture.RunRolloverAsync();

        await using var verify = fixture.Context();
        var subscription = await verify.OrganizationSubscriptions
            .SingleAsync(row => row.OrganizationId == organizationId);

        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
        Assert.Equal(PeriodEnd, subscription.CurrentPeriodStart);
        Assert.Equal(PeriodEnd.AddMonths(1), subscription.CurrentPeriodEnd);
        Assert.Equal(0, subscription.RenewalAttemptCount);
        Assert.Null(subscription.NextRenewalAttemptAt);

        var intent = await verify.PaymentIntents.SingleAsync(
            row => row.OrganizationId == organizationId);
        Assert.Equal(PaymentPurpose.SubscriptionRenewal, intent.Purpose);
        Assert.True(intent.SettledAt is not null);
    }

    /// <summary>
    /// Plan §9.5 F5 (Phase 6): a subscription whose owner scheduled cancellation is not renewed,
    /// moves to <c>Cancelled</c>, and the closed period's <c>Derived</c> charge is not written. The
    /// renewal intent is what must not be created, and the expectation is what must not be billed.
    /// </summary>
    [Fact]
    public async Task CancelAtPeriodEnd_TransitionsToCancelled_StopsTheDerivedCharge_AndDoesNotRenew()
    {
        using var fixture = new Fixture(providerSettles: true);
        var organizationId = await SeedEndedPeriodAsync(fixture, cancelAtPeriodEnd: true);

        await fixture.RunRolloverAsync();

        await using var verify = fixture.Context();
        Assert.False(await verify.PaymentIntents.AnyAsync(
            row => row.OrganizationId == organizationId));

        // Phase 6: the closed period is not billed for a subscription that is ending, so neither
        // the Derived expectation nor a Verified receipt exists.
        Assert.Empty(await verify.IncomeLedgerEntries
            .Where(entry => entry.OrganizationId == organizationId)
            .ToListAsync());

        var subscription = await verify.OrganizationSubscriptions
            .SingleAsync(row => row.OrganizationId == organizationId);
        Assert.Equal(SubscriptionStatus.Cancelled, subscription.Status);
        Assert.True(subscription.CancelAtPeriodEnd);
        Assert.NotNull(subscription.CancelledAt);
        Assert.Equal(PeriodEnd, subscription.CurrentPeriodEnd);
    }

    /// <summary>
    /// The guard on the other side: a subscription that has not scheduled cancellation is still
    /// billed for the closed period, so the Phase 6 change does not silently stop ordinary revenue.
    /// </summary>
    [Fact]
    public async Task WithoutCancelAtPeriodEnd_TheClosedPeriodIsStillCharged()
    {
        using var fixture = new Fixture(providerSettles: true);
        var organizationId = await SeedEndedPeriodAsync(fixture);

        await fixture.RunRolloverAsync();

        await using var verify = fixture.Context();
        Assert.True(await verify.IncomeLedgerEntries.AnyAsync(entry =>
            entry.OrganizationId == organizationId
            && entry.ChargeBasis == IncomeChargeBasis.Derived));
    }

    [Fact]
    public async Task FailedRenewal_MovesTheSubscriptionToPastDueAndSchedulesDayOne()
    {
        using var fixture = new Fixture(providerSettles: false);
        var organizationId = await SeedEndedPeriodAsync(fixture);

        await fixture.RunRolloverAsync();

        await using var verify = fixture.Context();
        var subscription = await verify.OrganizationSubscriptions
            .SingleAsync(row => row.OrganizationId == organizationId);

        Assert.Equal(SubscriptionStatus.PastDue, subscription.Status);
        Assert.Equal(PeriodEnd, subscription.DunningStartedAt);
        Assert.Equal(1, subscription.RenewalAttemptCount);
        Assert.Equal(PeriodEnd.AddDays(1), subscription.NextRenewalAttemptAt);

        // The Derived charge still exists — the period boundary is what it always was.
        Assert.True(await verify.IncomeLedgerEntries.AnyAsync(entry =>
            entry.OrganizationId == organizationId
            && entry.ChargeBasis == IncomeChargeBasis.Derived));
    }

    /// <summary>
    /// The approved dunning schedule (Q3): the three retries land on days 1, 3 and 7 after the
    /// failed boundary, the status stays <c>PastDue</c> throughout, and day 14 expires it.
    /// </summary>
    [Fact]
    public async Task Dunning_RetriesOnDaysOneThreeAndSeven_ThenExpiresAtDayFourteen()
    {
        using var fixture = new Fixture(providerSettles: false);
        var organizationId = await SeedEndedPeriodAsync(fixture);

        // Day 0: the rollover's own attempt, from the failed period boundary.
        await fixture.RunRolloverAsync();
        await AssertDunningStateAsync(fixture, organizationId, 1, PeriodEnd.AddDays(1));

        // Day 1: retry one.
        fixture.Clock.Now = new DateTimeOffset(PeriodEnd.AddDays(1), TimeSpan.Zero);
        await fixture.RunRolloverAsync();
        await AssertDunningStateAsync(fixture, organizationId, 2, PeriodEnd.AddDays(3));

        // Day 3: retry two.
        fixture.Clock.Now = new DateTimeOffset(PeriodEnd.AddDays(3), TimeSpan.Zero);
        await fixture.RunRolloverAsync();
        await AssertDunningStateAsync(fixture, organizationId, 3, PeriodEnd.AddDays(7));

        // Day 7: retry three, and the schedule is exhausted.
        fixture.Clock.Now = new DateTimeOffset(PeriodEnd.AddDays(7), TimeSpan.Zero);
        await fixture.RunRolloverAsync();
        await AssertDunningStateAsync(fixture, organizationId, 4, nextAttemptAt: null);

        // A run between day 7 and day 14 attempts nothing, and does not expire early.
        fixture.Clock.Now = new DateTimeOffset(PeriodEnd.AddDays(13), TimeSpan.Zero);
        await fixture.RunRolloverAsync();
        await AssertDunningStateAsync(fixture, organizationId, 4, nextAttemptAt: null);

        // Day 14: expired, with everything still on disk.
        fixture.Clock.Now = new DateTimeOffset(PeriodEnd.AddDays(14), TimeSpan.Zero);
        await fixture.RunRolloverAsync();

        await using var verify = fixture.Context();
        var subscription = await verify.OrganizationSubscriptions
            .SingleAsync(row => row.OrganizationId == organizationId);
        Assert.Equal(SubscriptionStatus.Expired, subscription.Status);
        Assert.Equal(4, subscription.RenewalAttemptCount);
        Assert.Null(subscription.NextRenewalAttemptAt);

        // Expiry is a state, not a deletion: the charge, the intents and the account all survive.
        Assert.True(await verify.IncomeLedgerEntries.AnyAsync(
            entry => entry.OrganizationId == organizationId));
        Assert.True(await verify.UsageAccounts.AnyAsync(
            account => account.OrganizationId == organizationId));
        Assert.Equal(
            4,
            await verify.PaymentIntents.CountAsync(row => row.OrganizationId == organizationId));
    }

    private static async Task AssertDunningStateAsync(
        Fixture fixture, Guid organizationId, int expectedAttemptCount, DateTime? nextAttemptAt)
    {
        await using var verify = fixture.Context();
        var subscription = await verify.OrganizationSubscriptions
            .SingleAsync(row => row.OrganizationId == organizationId);

        Assert.Equal(SubscriptionStatus.PastDue, subscription.Status);
        Assert.Equal(expectedAttemptCount, subscription.RenewalAttemptCount);
        Assert.Equal(nextAttemptAt, subscription.NextRenewalAttemptAt);
    }

    [Fact]
    public async Task PastDueRenewal_RaisesANotificationThroughTheDispatcher()
    {
        using var fixture = new Fixture(providerSettles: false);
        await SeedEndedPeriodAsync(fixture);

        await fixture.RunRolloverAsync();

        var notification = Assert.Single(
            fixture.Dispatcher.Dispatched,
            delivered => delivered.Type == NotificationType.SubscriptionPastDue);
        Assert.Equal([Roles.BoutiqueOwner, Roles.Owner], notification.Target.Roles);
    }

    [Fact]
    public async Task ExpiredRenewal_RaisesANotificationThroughTheDispatcher()
    {
        using var fixture = new Fixture(providerSettles: false);
        await SeedEndedPeriodAsync(fixture);

        fixture.Clock.Now = new DateTimeOffset(PeriodEnd.AddDays(14), TimeSpan.Zero);
        await fixture.RunRolloverAsync();

        var notification = Assert.Single(
            fixture.Dispatcher.Dispatched,
            delivered => delivered.Type == NotificationType.SubscriptionExpired);
        Assert.Equal([Roles.BoutiqueOwner, Roles.Owner], notification.Target.Roles);
    }
}
