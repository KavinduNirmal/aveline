using Aveline.Api.Common.Jobs;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Infrastructure.Eventing;
using Aveline.Api.Modules.Audit.Repositories;
using Aveline.Api.Modules.Audit.Services;
using Aveline.Api.Modules.Billing.Domain;
using Aveline.Api.Modules.Billing.Jobs;
using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Billing.Repositories;
using Aveline.Api.Modules.Billing.Services;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Revenue.Models;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;

namespace Aveline.Api.Tests;

/// <summary>
/// Issue #404 (Payments P1), the phase exit criterion that needs real PostgreSQL: a subscription
/// priced by <c>SubscriptionService</c> must make <c>BillingPeriodRolloverJob</c> write a
/// <c>Derived</c> <c>SubscriptionCharge</c>. The in-memory rollover writer already covers the job's
/// own arithmetic (<c>RevenueWritePathsTests</c>); this test covers the seam the phase adds — the
/// price comes from the price book through the service, on the real provider and the real schema.
/// </summary>
public class SubscriptionPricePostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg16")
        .WithDatabase("aveline_test")
        .WithUsername("aveline")
        .WithPassword("aveline_test_pw")
        .Build();

    private DbContextOptions<AppDbContext> _options = null!;
    private ServiceProvider _jobServices = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(
                _postgres.GetConnectionString(),
                npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .Options;

        await using (var context = new AppDbContext(_options))
        {
            await context.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS vector");
            await context.Database.MigrateAsync();
        }

        // The rollover job resolves its collaborators from a scope; only the two it asks for are
        // registered, against the same container.
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options => options.UseNpgsql(
            _postgres.GetConnectionString(),
            npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName)));
        services.AddScoped<IEntitlementRepository, EntitlementRepository>();
        services.AddScoped<IEntitlementResolver, EntitlementResolver>();
        _jobServices = services.BuildServiceProvider();
    }

    public async Task DisposeAsync()
    {
        await _jobServices.DisposeAsync();
        await _postgres.DisposeAsync();
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

    private BillingPeriodRolloverJob CreateRolloverJob() => new(
        _jobServices.GetRequiredService<IServiceScopeFactory>(),
        new NoopJobLock(),
        NullLogger<BillingPeriodRolloverJob>.Instance);

    private static SubscriptionService CreateSubscriptionService(AppDbContext context)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Billing:MaxAdjustmentBlossoms"] = "100000",
            })
            .Build();

        return new SubscriptionService(
            context,
            new EntitlementResolver(new EntitlementRepository(context)),
            new BlossomService(
                new BlossomLedgerRepository(context),
                new UsageRepository(context),
                new EntitlementResolver(new EntitlementRepository(context)),
                new InMemoryEventBus(),
                new AuditService(
                    new AuditRepository(context), new AuditRedactor(),
                    new HttpContextAccessor(), NullLogger<AuditService>.Instance),
                configuration,
                NullLogger<BlossomService>.Instance),
            new InMemoryEventBus(),
            new AuditService(
                new AuditRepository(context), new AuditRedactor(),
                new HttpContextAccessor(), NullLogger<AuditService>.Instance),
            new SubscriptionPriceResolver(new PricingRepository(context)),
            NullLogger<SubscriptionService>.Instance);
    }

    /// <summary>An organisation on Seed plus the Bloom plan-allowance price, already effective.</summary>
    private async Task<Guid> SeedAsync()
    {
        await using var context = new AppDbContext(_options);
        var ownerId = Guid.CreateVersion7();
        context.Users.Add(new User
        {
            Id = ownerId, ClerkId = $"sp_{ownerId:N}", Email = "sp@aveline.lk", FirstName = "S",
            LastName = "P", Username = $"sp_{ownerId:N}", UserRole = "owner",
            OrganizationRole = "org:boutique_owner",
        });
        var organization = new Organization
        {
            Name = "Subscription Price", Slug = $"sp-{ownerId:N}", OwnerUserId = ownerId,
            PlanTier = PlanTier.Seed,
        };
        context.Organizations.Add(organization);

        context.BlossomPriceEntries.Add(new BlossomPriceEntry
        {
            PlanTier = PlanTier.Bloom,
            SkuKind = BlossomSkuKind.PlanAllowance,
            BlossomQuantity = 750m,
            PriceLkr = 3500m,
            EffectiveFrom = DateTime.UtcNow.AddMonths(-1),
            Status = BlossomRuleStatus.Active,
            ChangeReason = "Phase 1 price-book seed for the rollover acceptance test.",
            CreatedByUserId = ownerId,
        });
        await context.SaveChangesAsync();
        return organization.Id;
    }

    [Fact]
    public async Task APricedSubscription_CausesTheRolloverToWriteADerivedCharge()
    {
        var organizationId = await SeedAsync();
        var periodStart = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);

        await using (var context = new AppDbContext(_options))
        {
            // The phase's whole point: the plan change prices the subscription from the book (G9).
            await CreateSubscriptionService(context).ChangePlanAsync(new ChangePlanCommand(
                organizationId, PlanTier.Bloom, Effective: "immediate", Reason: null,
                ActorUserId: Guid.CreateVersion7(), IdempotencyKey: null, IdempotencyScope: null));

            var subscription = await context.OrganizationSubscriptions
                .SingleAsync(row => row.OrganizationId == organizationId);
            Assert.Equal(3500m, subscription.PriceLkr);

            // The service anchors a new subscription to the month it was created. Move it to a
            // closed period so the rollover has a boundary to act on, and give that period an
            // account to close.
            subscription.CurrentPeriodStart = periodStart;
            subscription.CurrentPeriodEnd = periodStart.AddMonths(1);
            context.UsageAccounts.Add(new UsageAccount
            {
                OrganizationId = organizationId,
                PeriodStart = periodStart,
                PeriodEnd = periodStart.AddMonths(1),
                MonthlyBlossomLimit = 150m,
                BlossomRemaining = 150m,
                PlanTierSnapshot = PlanTier.Bloom,
                Status = UsageAccountStatus.Active,
            });
            await context.SaveChangesAsync();
        }

        await CreateRolloverJob().RunAsync(default);

        await using var verify = new AppDbContext(_options);
        var charge = await verify.IncomeLedgerEntries.SingleAsync(entry =>
            entry.OrganizationId == organizationId
            && entry.Kind == IncomeEntryKind.SubscriptionCharge);

        // Derived, never Verified: a period boundary is not a receipt (Revenue Ledger R2).
        Assert.Equal(IncomeChargeBasis.Derived, charge.ChargeBasis);
        Assert.Equal(3500m, charge.Amount);
        Assert.Equal("LKR", charge.Currency);
        Assert.Equal(periodStart.ToString("o"), charge.SourceRef);
    }
}
