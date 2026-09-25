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
using Testcontainers.PostgreSql;

namespace Aveline.Api.Tests;

/// <summary>
/// Phase 4's exit criterion that needs real PostgreSQL: <c>PastDue</c> is reachable, <c>Expired</c>
/// is reached on day 14, and the renewal receipt takes over the derived charge's period on the real
/// schema.
/// </summary>
/// <remarks>
/// The in-memory provider has no filtered unique index and no transaction ordering, so it cannot
/// prove that superseding the <c>Derived</c> charge with the <c>Verified</c> receipt is possible at
/// all: the two rows share the ledger's dedup identity until the void nulls the old reference. That
/// is exactly what this case exists to run, alongside the dunning columns' real column types.
/// </remarks>
public class SubscriptionRenewalPostgresTests : IAsyncLifetime
{
    private static readonly DateTime PeriodStart = new(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime PeriodEnd = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg16")
        .WithDatabase("aveline_test")
        .WithUsername("aveline")
        .WithPassword("aveline_test_pw")
        .Build();

    private DbContextOptions<AppDbContext> _options = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(
                _postgres.GetConnectionString(),
                npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .Options;

        await using var context = new AppDbContext(_options);
        await context.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS vector");
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private sealed class MutableClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

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

    private sealed class StubHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Aveline.Api.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    private sealed class NullDispatcher : INotificationDispatcher
    {
        public Task<NotificationRecord?> DispatchAsync(
            Notification notification, CancellationToken cancellationToken = default)
            => Task.FromResult<NotificationRecord?>(null);
    }

    private static IConfiguration Configuration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Payments:Provider"] = "mock",
            ["Payments:Mock:Enabled"] = "true",
            ["Payments:Mock:AutoSettle"] = "true",
            ["Payments:Mock:WebhookSigningSecret"] = "whsec_renewal_postgres_secret",
            ["Payments:Currency"] = "LKR",
        })
        .Build();

    private (ServiceProvider Provider, MutableClock Clock) BuildJobServices(
        string credential, DateTimeOffset now)
    {
        var clock = new MutableClock(now);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(clock);
        services.AddDbContext<AppDbContext>(options => options.UseNpgsql(
            _postgres.GetConnectionString(),
            npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName)));
        services.AddSingleton<IEventBus, InMemoryEventBus>();
        services.AddBillingModule();
        services.AddRevenueModule(Configuration());
        services.AddPaymentsModule(Configuration(), new StubHostEnvironment());
        services.AddAuditModule();
        services.AddSingleton<INotificationDispatcher, NullDispatcher>();
        services.AddKeyedSingleton<IPaymentProvider>(MockPaymentProvider.ProviderKey, (provider, _) =>
            new MockPaymentProvider(
                provider.GetRequiredService<IOptions<PaymentsOptions>>(),
                clock,
                NullLogger<MockPaymentProvider>.Instance,
                provider.GetRequiredService<PaymentMetrics>(),
                credential));

        return (services.BuildServiceProvider(), clock);
    }

    private static BillingPeriodRolloverJob CreateJob(ServiceProvider provider) => new(
        provider.GetRequiredService<IServiceScopeFactory>(),
        new NoopJobLock(),
        NullLogger<BillingPeriodRolloverJob>.Instance);

    private async Task<Guid> SeedEndedPeriodAsync()
    {
        await using var context = new AppDbContext(_options);
        var ownerId = Guid.CreateVersion7();
        context.Users.Add(new User
        {
            Id = ownerId,
            ClerkId = $"rp_{ownerId:N}",
            Email = "rp@aveline.lk",
            FirstName = "R",
            LastName = "P",
            Username = $"rp_{ownerId:N}",
            UserRole = "owner",
            OrganizationRole = "org:boutique_owner",
        });
        var organization = new Organization
        {
            Name = "Renewal Postgres",
            Slug = $"rp-{ownerId:N}",
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
            PriceLkr = 3500m,
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

    [Fact]
    public async Task ASettledRenewal_TakesOverTheDerivedChargeOnTheRealSchema()
    {
        var organizationId = await SeedEndedPeriodAsync();
        var (provider, _) = BuildJobServices(MockPaymentProvider.SucceedToken, new DateTimeOffset(PeriodEnd, TimeSpan.Zero));

        try
        {
            await CreateJob(provider).RunAsync(default);
        }
        finally
        {
            await provider.DisposeAsync();
        }

        await using var verify = new AppDbContext(_options);
        var entries = await verify.IncomeLedgerEntries
            .Where(entry => entry.OrganizationId == organizationId)
            .ToListAsync();

        var derived = Assert.Single(entries, entry => entry.ChargeBasis == IncomeChargeBasis.Derived);
        var verified = Assert.Single(entries, entry => entry.ChargeBasis == IncomeChargeBasis.Verified);
        Assert.Equal(3500m, derived.Amount);
        Assert.Equal(3500m, verified.Amount);
        Assert.Equal(derived.PeriodStart, verified.PeriodStart);
        Assert.Equal(derived.PeriodEnd, verified.PeriodEnd);
        Assert.Equal(IncomeEntryStatus.Voided, derived.Status);
        Assert.Equal(derived.Id, verified.SupersedesEntryId);

        var subscription = await verify.OrganizationSubscriptions
            .SingleAsync(row => row.OrganizationId == organizationId);
        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
        Assert.Equal(PeriodEnd.AddMonths(1), subscription.CurrentPeriodEnd);
    }

    /// <summary>
    /// The acceptance criterion: <c>PastDue</c> is reachable, the day-1/3/7 retries land, and day 14
    /// is <c>Expired</c>. Everything the subscription owns is still on disk afterwards.
    /// </summary>
    [Fact]
    public async Task AFailedRenewal_ReachesPastDue_AndDayFourteenReachesExpired()
    {
        var organizationId = await SeedEndedPeriodAsync();

        var (provider, clock) = BuildJobServices(
            "4000000000000002", new DateTimeOffset(PeriodEnd, TimeSpan.Zero));

        try
        {
            await CreateJob(provider).RunAsync(default);

            await using (var afterDayZero = new AppDbContext(_options))
            {
                var subscription = await afterDayZero.OrganizationSubscriptions
                    .SingleAsync(row => row.OrganizationId == organizationId);
                Assert.Equal(SubscriptionStatus.PastDue, subscription.Status);
                Assert.Equal(1, subscription.RenewalAttemptCount);
                Assert.Equal(PeriodEnd.AddDays(1), subscription.NextRenewalAttemptAt);
            }

            foreach (var day in new[] { 1, 3, 7 })
            {
                clock.Now = new DateTimeOffset(PeriodEnd.AddDays(day), TimeSpan.Zero);
                await CreateJob(provider).RunAsync(default);
            }

            await using (var afterDaySeven = new AppDbContext(_options))
            {
                var subscription = await afterDaySeven.OrganizationSubscriptions
                    .SingleAsync(row => row.OrganizationId == organizationId);
                Assert.Equal(SubscriptionStatus.PastDue, subscription.Status);
                Assert.Equal(4, subscription.RenewalAttemptCount);
                Assert.Null(subscription.NextRenewalAttemptAt);
            }

            clock.Now = new DateTimeOffset(PeriodEnd.AddDays(14), TimeSpan.Zero);
            await CreateJob(provider).RunAsync(default);
        }
        finally
        {
            await provider.DisposeAsync();
        }

        await using var verify = new AppDbContext(_options);
        var expired = await verify.OrganizationSubscriptions
            .SingleAsync(row => row.OrganizationId == organizationId);
        Assert.Equal(SubscriptionStatus.Expired, expired.Status);
        Assert.Null(expired.NextRenewalAttemptAt);

        // No deletion: the charge, the attempts and the Blossom account all survive expiry.
        Assert.True(await verify.IncomeLedgerEntries.AnyAsync(
            entry => entry.OrganizationId == organizationId));
        Assert.True(await verify.UsageAccounts.AnyAsync(
            account => account.OrganizationId == organizationId));
        Assert.Equal(
            4,
            await verify.PaymentIntents.CountAsync(row => row.OrganizationId == organizationId));
    }
}
