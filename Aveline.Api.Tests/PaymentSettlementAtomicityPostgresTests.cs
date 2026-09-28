using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Infrastructure.Eventing;
using Aveline.Api.Modules.Audit.Repositories;
using Aveline.Api.Modules.Audit.Services;
using Aveline.Api.Modules.Billing.Domain;
using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Billing.Repositories;
using Aveline.Api.Modules.Billing.Services;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Payments.Domain;
using Aveline.Api.Modules.Payments.Models;
using Aveline.Api.Modules.Payments.Repositories;
using Aveline.Api.Modules.Payments.Services;
using Aveline.Api.Modules.Revenue.Models;
using Aveline.Api.Modules.Revenue.Services;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;

namespace Aveline.Api.Tests;

/// <summary>
/// The acceptance criterion that the in-memory provider cannot demonstrate: settlement is **one**
/// database transaction, so a failure after the Blossom grant rolls the grant back. The in-memory
/// provider has no transactions, which is exactly why this case runs against PostgreSQL.
/// </summary>
public class PaymentSettlementAtomicityPostgresTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 2, 10, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("pgvector/pgvector:pg16")
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

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>The forced failure the acceptance criterion names: the income write cannot land.</summary>
    private sealed class FailingIncomeLedger : IIncomeLedgerService
    {
        public Task<IncomeLedgerEntry> RecordAsync(
            RecordIncomeCommand command, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Forced income-ledger failure.");

        // The P6 extraction adds a second append-only write; this double fails both so the
        // atomicity case keeps exercising the same forced failure.
        public Task<IncomeLedgerEntry> VerifyAsync(
            VerifyIncomeCommand command, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Forced income-ledger failure.");
    }

    [Fact]
    public async Task AForcedFailureInTheIncomeWrite_LeavesNoBlossomGrant_AndNoSucceededIntent()
    {
        Guid orgId;
        Guid intentId;
        Guid inboxId;
        string providerIntentId;

        await using (var seed = new AppDbContext(_options))
        {
            var ownerId = Guid.CreateVersion7();
            seed.Users.Add(new User
            {
                Id = ownerId,
                ClerkId = $"atomic_{ownerId:N}",
                Email = "atomic@aveline.lk",
                FirstName = "Atom",
                LastName = "Ic",
                Username = $"atomic_{ownerId:N}",
                UserRole = "owner",
                OrganizationRole = "org:boutique_owner",
            });
            var org = new Organization
            {
                Name = "Atomic Boutique",
                Slug = $"atomic-{ownerId:N}",
                OwnerUserId = ownerId,
            };
            seed.Organizations.Add(org);

            intentId = Guid.CreateVersion7();
            providerIntentId = $"mock_{intentId:N}";
            seed.PaymentIntents.Add(new PaymentIntent
            {
                Id = intentId,
                OrganizationId = org.Id,
                Provider = "mock",
                ProviderIntentId = providerIntentId,
                ExternalRef = providerIntentId,
                Purpose = PaymentPurpose.BlossomTopUp,
                Status = PaymentProviderStatus.RequiresAction,
                AmountMinor = 900000,
                Currency = "LKR",
                PriceLkr = 9000m,
                SkuCode = "pack_500",
                BlossomQuantity = 500m,
                Description = "Top-up purchase pack_500 (500 Blossoms).",
                IdempotencyKey = "atomic-key",
                CreatedByUserId = ownerId,
                CreatedAt = Now.UtcDateTime,
                UpdatedAt = Now.UtcDateTime,
            });

            inboxId = Guid.CreateVersion7();
            seed.PaymentProviderEvents.Add(new PaymentProviderEvent
            {
                Id = inboxId,
                Provider = "mock",
                ProviderEventId = "evt_atomic_1",
                EventType = PaymentWebhookEventType.IntentSucceeded,
                ProviderIntentId = providerIntentId,
                AmountMinor = 900000,
                Currency = "LKR",
                OccurredAt = Now.UtcDateTime,
                ReceivedAt = Now.UtcDateTime,
                RawPayload = "{}",
            });

            await seed.SaveChangesAsync();
            orgId = org.Id;
        }

        await using (var context = new AppDbContext(_options))
        {
            var bus = new InMemoryEventBus();
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Billing:MaxAdjustmentBlossoms"] = "10000",
                    ["Billing:LowBalanceThresholdPercent"] = "20",
                })
                .Build();

            var audit = new AuditService(
                new AuditRepository(context), new AuditRedactor(), new HttpContextAccessor(),
                NullLogger<AuditService>.Instance);

            var blossoms = new BlossomService(
                new BlossomLedgerRepository(context),
                new UsageRepository(context),
                new EntitlementResolver(new EntitlementRepository(context)),
                bus,
                audit,
                configuration,
                NullLogger<BlossomService>.Instance);

            var settlement = new PaymentSettlementService(
                context,
                new PaymentIntentRepository(context),
                new PaymentProviderEventRepository(context),
                blossoms,
                new FailingIncomeLedger(),
                bus,
                audit,
                new PaymentMetrics(),
                new FixedClock(Now),
                NullLogger<PaymentSettlementService>.Instance);

            await Assert.ThrowsAsync<InvalidOperationException>(() => settlement.SettleAsync(
                new SettlePaymentCommand(
                    "mock",
                    new PaymentWebhookEvent(
                        "evt_atomic_1",
                        PaymentWebhookEventType.IntentSucceeded,
                        providerIntentId,
                        null,
                        new Money(900000, "LKR"),
                        null,
                        Now,
                        "{}"))));
        }

        await using var verify = new AppDbContext(_options);

        Assert.Empty(await verify.BlossomLedgerEntries
            .Where(row => row.OrganizationId == orgId).ToListAsync());
        Assert.Empty(await verify.UsageAccounts
            .Where(row => row.OrganizationId == orgId).ToListAsync());
        Assert.Empty(await verify.IncomeLedgerEntries
            .Where(row => row.OrganizationId == orgId).ToListAsync());

        var intent = await verify.PaymentIntents.SingleAsync(row => row.Id == intentId);
        Assert.Equal(PaymentProviderStatus.RequiresAction, intent.Status);
        Assert.Null(intent.SettledAt);

        var inbox = await verify.PaymentProviderEvents.SingleAsync(row => row.Id == inboxId);
        Assert.Null(inbox.ProcessedAt);
    }
}
