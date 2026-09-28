using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Infrastructure.Eventing;
using Aveline.Api.Modules.Audit.Repositories;
using Aveline.Api.Modules.Audit.Services;
using Aveline.Api.Modules.Billing.Domain;
using Aveline.Api.Modules.Billing.Repositories;
using Aveline.Api.Modules.Billing.Services;
using Aveline.Api.Modules.Payments.Domain;
using Aveline.Api.Modules.Payments.Models;
using Aveline.Api.Modules.Payments.Repositories;
using Aveline.Api.Modules.Payments.Services;
using Aveline.Api.Modules.Revenue.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aveline.Api.Tests;

/// <summary>
/// Plan §10 Phase 7 and §6.6's intent-expiry row. P3 read `Expired` off <c>ExpiresAt &lt; now</c>
/// without persisting it ("the state is derivable, so a job is only needed to make it durable").
/// This is that job: it writes the terminal state once, so a client poll, a reconciliation pass and
/// the row itself all agree, and nothing later can resurrect the charge.
/// </summary>
public class PaymentIntentExpirySweepTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly AppDbContext _context;
    private readonly PaymentIntentExpiryService _sweep;
    private readonly Guid _orgId = Guid.CreateVersion7();

    public PaymentIntentExpirySweepTests()
    {
        _context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"PaymentExpiry_{Guid.NewGuid()}")
            .Options);

        _sweep = new PaymentIntentExpiryService(_context, new FixedClock(Now));
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private async Task<PaymentIntent> SeedIntentAsync(
        PaymentProviderStatus status,
        DateTime? expiresAt,
        DateTime? settledAt = null,
        string providerIntentId = "mock_seed")
    {
        var intent = new PaymentIntent
        {
            OrganizationId = _orgId,
            Provider = "mock",
            ProviderIntentId = providerIntentId,
            ExternalRef = providerIntentId,
            Purpose = PaymentPurpose.BlossomTopUp,
            Status = status,
            AmountMinor = 3500,
            Currency = "LKR",
            PriceLkr = 35m,
            SkuCode = "pack_500",
            BlossomQuantity = 500m,
            Description = "Top-up purchase pack_500 (500 Blossoms).",
            CreatedAt = Now.UtcDateTime.AddHours(-1),
            UpdatedAt = Now.UtcDateTime.AddHours(-1),
            ExpiresAt = expiresAt,
            SettledAt = settledAt,
        };

        _context.PaymentIntents.Add(intent);
        await _context.SaveChangesAsync();
        return intent;
    }

    [Fact]
    public async Task Sweep_MarksAnUnsettledPastExpiryIntentExpired()
    {
        var intent = await SeedIntentAsync(
            PaymentProviderStatus.RequiresAction, Now.UtcDateTime.AddMinutes(-5));

        var marked = await _sweep.SweepAsync();

        Assert.Equal(1, marked);
        var row = await _context.PaymentIntents.SingleAsync(candidate => candidate.Id == intent.Id);
        Assert.Equal(PaymentProviderStatus.Expired, row.Status);
        Assert.Null(row.SettledAt);
        Assert.Equal(Now.UtcDateTime, row.UpdatedAt);
    }

    /// <summary>
    /// Acceptance criterion: the sweep "leaves an already-settled one alone". A charge the provider
    /// confirmed is settled money, and an expiry timestamp is not a reason to take it back.
    /// </summary>
    [Fact]
    public async Task Sweep_LeavesAnAlreadySettledIntentAlone()
    {
        var settledAt = Now.UtcDateTime.AddMinutes(-30);
        var intent = await SeedIntentAsync(
            PaymentProviderStatus.Succeeded, Now.UtcDateTime.AddMinutes(-5), settledAt);

        var marked = await _sweep.SweepAsync();

        Assert.Equal(0, marked);
        var row = await _context.PaymentIntents.SingleAsync(candidate => candidate.Id == intent.Id);
        Assert.Equal(PaymentProviderStatus.Succeeded, row.Status);
        Assert.Equal(settledAt, row.SettledAt);
    }

    [Fact]
    public async Task Sweep_LeavesAFutureExpiryIntentAlone()
    {
        var intent = await SeedIntentAsync(
            PaymentProviderStatus.RequiresAction, Now.UtcDateTime.AddMinutes(5));

        var marked = await _sweep.SweepAsync();

        Assert.Equal(0, marked);
        var row = await _context.PaymentIntents.SingleAsync(candidate => candidate.Id == intent.Id);
        Assert.Equal(PaymentProviderStatus.RequiresAction, row.Status);
    }

    [Fact]
    public async Task Sweep_LeavesAnIntentWithNoExpiryAlone()
    {
        var intent = await SeedIntentAsync(PaymentProviderStatus.Processing, expiresAt: null);

        var marked = await _sweep.SweepAsync();

        Assert.Equal(0, marked);
        var row = await _context.PaymentIntents.SingleAsync(candidate => candidate.Id == intent.Id);
        Assert.Equal(PaymentProviderStatus.Processing, row.Status);
    }

    [Fact]
    public async Task Sweep_LeavesAlreadyTerminalStatesAlone()
    {
        var failed = await SeedIntentAsync(
            PaymentProviderStatus.Failed, Now.UtcDateTime.AddMinutes(-5), providerIntentId: "mock_failed");
        var cancelled = await SeedIntentAsync(
            PaymentProviderStatus.Cancelled, Now.UtcDateTime.AddMinutes(-5), providerIntentId: "mock_cancelled");
        var expired = await SeedIntentAsync(
            PaymentProviderStatus.Expired, Now.UtcDateTime.AddMinutes(-5), providerIntentId: "mock_expired");

        var marked = await _sweep.SweepAsync();

        Assert.Equal(0, marked);
        Assert.Equal(
            PaymentProviderStatus.Failed,
            (await _context.PaymentIntents.SingleAsync(row => row.Id == failed.Id)).Status);
        Assert.Equal(
            PaymentProviderStatus.Cancelled,
            (await _context.PaymentIntents.SingleAsync(row => row.Id == cancelled.Id)).Status);
        Assert.Equal(
            PaymentProviderStatus.Expired,
            (await _context.PaymentIntents.SingleAsync(row => row.Id == expired.Id)).Status);
    }

    [Fact]
    public async Task Sweep_IsIdempotent()
    {
        await SeedIntentAsync(PaymentProviderStatus.RequiresAction, Now.UtcDateTime.AddMinutes(-5));

        Assert.Equal(1, await _sweep.SweepAsync());
        Assert.Equal(0, await _sweep.SweepAsync());
    }

    /// <summary>
    /// "Do not resurrect or settle an expired intent." Once the sweep has written <c>Expired</c>, a
    /// late success webhook is refused by the settlement state guard, the inbox row keeps a null
    /// <c>ProcessedAt</c>, and nothing is granted.
    /// </summary>
    [Fact]
    public async Task ASweptIntent_IsNotSettledByALateSucceededWebhook()
    {
        var intent = await SeedIntentAsync(
            PaymentProviderStatus.RequiresAction, Now.UtcDateTime.AddMinutes(-5));

        _context.PaymentProviderEvents.Add(new PaymentProviderEvent
        {
            Provider = "mock",
            ProviderEventId = "evt_late_success",
            EventType = PaymentWebhookEventType.IntentSucceeded,
            ProviderIntentId = intent.ProviderIntentId,
            AmountMinor = intent.AmountMinor,
            Currency = intent.Currency,
            OccurredAt = Now.UtcDateTime,
            ReceivedAt = Now.UtcDateTime,
            RawPayload = "{}",
        });
        await _context.SaveChangesAsync();

        Assert.Equal(1, await _sweep.SweepAsync());

        var settlement = CreateSettlement();
        await Assert.ThrowsAsync<PaymentIntentStateException>(() => settlement.SettleAsync(
            new SettlePaymentCommand(
                "mock",
                new PaymentWebhookEvent(
                    "evt_late_success",
                    PaymentWebhookEventType.IntentSucceeded,
                    intent.ProviderIntentId,
                    null,
                    new Money(intent.AmountMinor, intent.Currency),
                    null,
                    Now,
                    "{}"))));

        var row = await _context.PaymentIntents.SingleAsync(candidate => candidate.Id == intent.Id);
        Assert.Equal(PaymentProviderStatus.Expired, row.Status);
        Assert.Null(row.SettledAt);

        var inbox = await _context.PaymentProviderEvents.SingleAsync(
            envelope => envelope.ProviderEventId == "evt_late_success");
        Assert.Null(inbox.ProcessedAt);
        Assert.False(await _context.IncomeLedgerEntries.AnyAsync());
        Assert.False(await _context.BlossomLedgerEntries.AnyAsync());
    }

    /// <summary>
    /// The settlement service only needs its real collaborators to reach the state guard; the grant
    /// path is never exercised here (the guard throws first), so no Blossom account is seeded.
    /// </summary>
    private PaymentSettlementService CreateSettlement()
    {
        var bus = new InMemoryEventBus();
        var audit = new AuditService(
            new AuditRepository(_context), new AuditRedactor(), new HttpContextAccessor(),
            NullLogger<AuditService>.Instance);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Billing:MaxAdjustmentBlossoms"] = "10000",
                ["Billing:LowBalanceThresholdPercent"] = "20",
            })
            .Build();

        var blossoms = new BlossomService(
            new BlossomLedgerRepository(_context),
            new UsageRepository(_context),
            new EntitlementResolver(new EntitlementRepository(_context)),
            bus,
            audit,
            configuration,
            NullLogger<BlossomService>.Instance);

        return new PaymentSettlementService(
            _context,
            new PaymentIntentRepository(_context),
            new PaymentProviderEventRepository(_context),
            blossoms,
            new IncomeLedgerService(_context, NullLogger<IncomeLedgerService>.Instance),
            bus,
            audit,
            new PaymentMetrics(),
            new FixedClock(Now),
            NullLogger<PaymentSettlementService>.Instance);
    }
}
