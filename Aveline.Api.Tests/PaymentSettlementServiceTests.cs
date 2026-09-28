using System.Diagnostics.Metrics;
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
using Aveline.Api.Modules.Revenue.Domain;
using Aveline.Api.Modules.Revenue.Models;
using Aveline.Api.Modules.Revenue.Services;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aveline.Api.Tests;

/// <summary>
/// Plan §6.4 steps 8-10 and every guard of §6.6, at the service layer (P2-B2). The atomicity of the
/// one-transaction settlement is proven against PostgreSQL by
/// <see cref="PaymentSettlementAtomicityPostgresTests"/>; the in-memory provider has no
/// transactions to roll back, which is why that case is not asserted here.
/// </summary>
public class PaymentSettlementServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 2, 10, 12, 0, 0, TimeSpan.Zero);

    private readonly string _databaseName = $"PaymentSettlement_{Guid.NewGuid()}";
    private readonly AppDbContext _context;

    public PaymentSettlementServiceTests()
    {
        _context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: _databaseName)
            .Options);
    }

    private AppDbContext NewContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(databaseName: _databaseName)
        .Options);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private AuditService CreateAudit() => new(
        new AuditRepository(_context), new AuditRedactor(), new HttpContextAccessor(),
        NullLogger<AuditService>.Instance);

    private PaymentSettlementService CreateSettlement(
        IIncomeLedgerService? incomeLedger = null, PaymentMetrics? metrics = null)
    {
        var bus = new InMemoryEventBus();
        var audit = CreateAudit();
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
            incomeLedger ?? new IncomeLedgerService(_context, NullLogger<IncomeLedgerService>.Instance),
            bus,
            audit,
            metrics ?? new PaymentMetrics(),
            new FixedClock(Now),
            NullLogger<PaymentSettlementService>.Instance);
    }

    private sealed record Seeded(
        Guid OrgId,
        Guid IntentId,
        string ProviderIntentId,
        Guid InboxEventId,
        Guid ActorUserId);

    private async Task<Seeded> SeedAsync(
        long amountMinor = 900000,
        string currency = "LKR",
        decimal blossomQuantity = 500m,
        DateTime? expiresAt = null,
        PaymentProviderStatus status = PaymentProviderStatus.RequiresAction,
        string providerEventId = "evt_settlement_1",
        PaymentPurpose purpose = PaymentPurpose.BlossomTopUp)
    {
        var actorId = Guid.CreateVersion7();
        _context.Users.Add(new User
        {
            Id = actorId,
            ClerkId = $"ps_{actorId:N}",
            Email = "ps@aveline.lk",
            FirstName = "Pay",
            LastName = "Settle",
            Username = $"ps_{actorId:N}",
            UserRole = "owner",
            OrganizationRole = "org:boutique_owner",
        });
        var org = new Organization
        {
            Name = "Payment Settlement Boutique",
            Slug = $"ps-{actorId:N}",
            OwnerUserId = actorId,
        };
        _context.Organizations.Add(org);

        var intentId = Guid.CreateVersion7();
        var providerIntentId = $"mock_{intentId:N}";
        _context.PaymentIntents.Add(new PaymentIntent
        {
            Id = intentId,
            OrganizationId = org.Id,
            Provider = "mock",
            ProviderIntentId = providerIntentId,
            ExternalRef = providerIntentId,
            Purpose = purpose,
            Status = status,
            AmountMinor = amountMinor,
            Currency = currency,
            PriceLkr = amountMinor / 100m,
            SkuCode = purpose == PaymentPurpose.BlossomTopUp ? "pack_500" : null,
            BlossomQuantity = purpose == PaymentPurpose.BlossomTopUp ? blossomQuantity : null,
            Description = "Top-up purchase pack_500 (500 Blossoms).",
            IdempotencyKey = "settlement-key",
            CreatedByUserId = actorId,
            CreatedAt = Now.UtcDateTime,
            UpdatedAt = Now.UtcDateTime,
            ExpiresAt = expiresAt,
        });

        var envelopeId = Guid.CreateVersion7();
        _context.PaymentProviderEvents.Add(new PaymentProviderEvent
        {
            Id = envelopeId,
            Provider = "mock",
            ProviderEventId = providerEventId,
            EventType = PaymentWebhookEventType.IntentSucceeded,
            ProviderIntentId = providerIntentId,
            AmountMinor = amountMinor,
            Currency = currency,
            OccurredAt = Now.UtcDateTime,
            ReceivedAt = Now.UtcDateTime,
            RawPayload = "{}",
        });

        await _context.SaveChangesAsync();
        return new Seeded(org.Id, intentId, providerIntentId, envelopeId, actorId);
    }

    private static SettlePaymentCommand SucceededEvent(
        Seeded seeded, long? amountMinor = null, string? currency = null) =>
        new(
            "mock",
            new PaymentWebhookEvent(
                "evt_settlement_1",
                PaymentWebhookEventType.IntentSucceeded,
                seeded.ProviderIntentId,
                null,
                new Money(amountMinor ?? 900000, currency ?? "LKR"),
                null,
                Now,
                "{}"));

    [Fact]
    public async Task Settle_ASucceededEvent_GrantsBlossoms_AndWritesAVerifiedIncomeRow()
    {
        var seeded = await SeedAsync();
        var settlement = CreateSettlement();

        var outcome = await settlement.SettleAsync(SucceededEvent(seeded));

        Assert.Equal(SettlementOutcomeKind.Settled, outcome.Kind);

        await using var verify = NewContext();
        var intent = await verify.PaymentIntents.SingleAsync(row => row.Id == seeded.IntentId);
        Assert.Equal(PaymentProviderStatus.Succeeded, intent.Status);
        Assert.NotNull(intent.SettledAt);

        var grant = await verify.BlossomLedgerEntries.SingleAsync();
        Assert.Equal(500m, grant.BlossomDelta);
        Assert.Equal(BlossomSourceKind.PaymentProvider, grant.SourceKind);
        Assert.Equal(seeded.ProviderIntentId, grant.SourceRef);
        // Both halves of the idempotency tuple, or the ledger's dedup is silently disabled (C9).
        Assert.Equal(seeded.ProviderIntentId, grant.IdempotencyKey);
        Assert.Equal("payments.topup", grant.IdempotencyScope);

        var income = await verify.IncomeLedgerEntries.SingleAsync();
        Assert.Equal(IncomeEntryKind.TopUpPurchase, income.Kind);
        Assert.Equal(IncomeSourceKind.BlossomTopUp, income.SourceKind);
        // D4: a provider confirmed it, so it is a receipt and not an expectation.
        Assert.Equal(IncomeChargeBasis.Verified, income.ChargeBasis);
        Assert.Equal(seeded.ProviderIntentId, income.SourceRef);

        var inbox = await verify.PaymentProviderEvents.SingleAsync(row => row.Id == seeded.InboxEventId);
        Assert.NotNull(inbox.ProcessedAt);
    }

    /// <summary>
    /// M7's second half and the acceptance criterion: a replayed settlement adds no second grant,
    /// no second income row, and cannot move the intent twice.
    /// </summary>
    [Fact]
    public async Task Settle_AReplayedEvent_IsANoOp_AndAddsNoSecondBlossomRow()
    {
        var seeded = await SeedAsync();
        var settlement = CreateSettlement();

        var first = await settlement.SettleAsync(SucceededEvent(seeded));
        var second = await settlement.SettleAsync(SucceededEvent(seeded));

        Assert.Equal(SettlementOutcomeKind.Settled, first.Kind);
        Assert.Equal(SettlementOutcomeKind.AlreadySettled, second.Kind);

        await using var verify = NewContext();
        Assert.Single(await verify.PaymentIntents.ToListAsync());
        Assert.Single(await verify.BlossomLedgerEntries.ToListAsync());
        Assert.Single(await verify.IncomeLedgerEntries.ToListAsync());
    }

    /// <summary>M10 — an amount that does not match is a security event, not a reconciliation.</summary>
    [Fact]
    public async Task Settle_AnAmountMismatch_StoresTheEventUnprocessed_AndLeavesTheBalanceUnchanged()
    {
        var seeded = await SeedAsync();
        var metrics = new PaymentMetrics();
        var settlement = CreateSettlement(metrics: metrics);
        var captured = new List<string>();
        using var listener = CaptureSettlementOutcomes(captured);

        var mismatch = await Assert.ThrowsAsync<PaymentIntentMismatchException>(() =>
            settlement.SettleAsync(SucceededEvent(seeded, amountMinor: 900001)));

        Assert.Equal(409, mismatch.StatusCode);
        Assert.Equal("payment-intent-mismatch", mismatch.ErrorCode);

        await using var verify = NewContext();
        var intent = await verify.PaymentIntents.SingleAsync(row => row.Id == seeded.IntentId);
        Assert.Equal(PaymentProviderStatus.RequiresAction, intent.Status);
        Assert.Null(intent.SettledAt);
        Assert.Empty(await verify.BlossomLedgerEntries.ToListAsync());
        Assert.Empty(await verify.IncomeLedgerEntries.ToListAsync());

        var inbox = await verify.PaymentProviderEvents.SingleAsync(row => row.Id == seeded.InboxEventId);
        Assert.Null(inbox.ProcessedAt);
        Assert.NotNull(inbox.ProcessingError);
        Assert.Contains("mismatch", captured);
    }

    /// <summary>M11 — the currency guard, asserted against the same inbox row shape as M10.</summary>
    [Fact]
    public async Task Settle_ACurrencyMismatch_StoresTheEventUnprocessed_AndLeavesTheBalanceUnchanged()
    {
        var seeded = await SeedAsync();
        var settlement = CreateSettlement();

        await Assert.ThrowsAsync<PaymentIntentMismatchException>(() =>
            settlement.SettleAsync(SucceededEvent(seeded, currency: "USD")));

        await using var verify = NewContext();
        Assert.Empty(await verify.BlossomLedgerEntries.ToListAsync());
        Assert.Empty(await verify.IncomeLedgerEntries.ToListAsync());
        var inbox = await verify.PaymentProviderEvents.SingleAsync(row => row.Id == seeded.InboxEventId);
        Assert.Null(inbox.ProcessedAt);
        Assert.NotNull(inbox.ProcessingError);
    }

    /// <summary>An intent that outlived its expiry cannot be settled by a late event.</summary>
    [Fact]
    public async Task Settle_AnExpiredIntent_ThrowsState_AndRecordsTheEventUnprocessed()
    {
        var seeded = await SeedAsync(expiresAt: Now.UtcDateTime.AddMinutes(-1));
        var settlement = CreateSettlement();

        var state = await Assert.ThrowsAsync<PaymentIntentStateException>(() =>
            settlement.SettleAsync(SucceededEvent(seeded)));

        Assert.Equal(409, state.StatusCode);
        await using var verify = NewContext();
        Assert.Empty(await verify.BlossomLedgerEntries.ToListAsync());
        var inbox = await verify.PaymentProviderEvents.SingleAsync(row => row.Id == seeded.InboxEventId);
        Assert.Null(inbox.ProcessedAt);
    }

    /// <summary>
    /// The (Provider, ProviderIntentId) lookup is the settlement's identity: an event for a charge
    /// Aveline never created settles nothing and is recorded unprocessed.
    /// </summary>
    [Fact]
    public async Task Settle_AnEventForAnUnknownIntent_RecordsTheEventUnprocessed()
    {
        var seeded = await SeedAsync();
        var settlement = CreateSettlement();
        var command = new SettlePaymentCommand(
            "mock",
            new PaymentWebhookEvent(
                "evt_settlement_1",
                PaymentWebhookEventType.IntentSucceeded,
                "mock_unknown_intent",
                null,
                new Money(900000, "LKR"),
                null,
                Now,
                "{}"));

        await Assert.ThrowsAsync<PaymentIntentNotFoundException>(() => settlement.SettleAsync(command));

        await using var verify = NewContext();
        var inbox = await verify.PaymentProviderEvents.SingleAsync(row => row.Id == seeded.InboxEventId);
        Assert.Null(inbox.ProcessedAt);
        Assert.Empty(await verify.BlossomLedgerEntries.ToListAsync());
    }

    /// <summary>
    /// P5 left the proration settlement as an open handoff (plan §6.4, deliverable 4): a settled
    /// <c>SubscriptionProration</c> intent must become a <c>Verified</c> receipt rather than being
    /// left outstanding with an unprocessed inbox row.
    /// </summary>
    [Fact]
    public async Task Settle_ASettledProrationIntent_WritesAVerifiedReceipt()
    {
        var seeded = await SeedAsync(purpose: PaymentPurpose.SubscriptionProration);
        var settlement = CreateSettlement();

        var outcome = await settlement.SettleAsync(SucceededEvent(seeded));

        Assert.Equal(SettlementOutcomeKind.Settled, outcome.Kind);

        await using var verify = NewContext();
        var intent = await verify.PaymentIntents.SingleAsync(row => row.Id == seeded.IntentId);
        Assert.Equal(PaymentProviderStatus.Succeeded, intent.Status);
        Assert.NotNull(intent.SettledAt);

        // A subscription charge has no Blossom effect; it is money the journal must record.
        Assert.Empty(await verify.BlossomLedgerEntries.ToListAsync());

        var receipt = await verify.IncomeLedgerEntries.SingleAsync();
        Assert.Equal(IncomeChargeBasis.Verified, receipt.ChargeBasis);
        Assert.Equal(IncomeEntryKind.SubscriptionCharge, receipt.Kind);
        Assert.Equal(seeded.ProviderIntentId, receipt.SourceRef);
        Assert.Equal(9000m, receipt.Amount);
        Assert.True(RevenueReconciliation.Of([receipt]).IsBalanced);

        var inbox = await verify.PaymentProviderEvents.SingleAsync(row => row.Id == seeded.InboxEventId);
        Assert.NotNull(inbox.ProcessedAt);
        Assert.Null(inbox.ProcessingError);
    }

    /// <summary>
    /// The fail-safe that still holds after P10: an event type with no handler is stored
    /// **unprocessed** with a <c>ProcessingError</c> and logged, so the reconciliation backlog
    /// surfaces it rather than forgetting it. A dispute is no longer one of these (it has its own
    /// type and effect); this event is a genuinely unrecognised provider type.
    /// </summary>
    [Fact]
    public async Task Settle_AnUnknownEventType_LeavesTheEventUnprocessed_AndSettlesNothing()
    {
        var seeded = await SeedAsync();
        var settlement = CreateSettlement();
        var command = new SettlePaymentCommand(
            "mock",
            new PaymentWebhookEvent(
                "evt_settlement_1",
                PaymentWebhookEventType.Unknown,
                seeded.ProviderIntentId,
                null,
                new Money(900000, "LKR"),
                null,
                Now,
                "{\"type\":\"charge.frobnicated\"}"));

        var outcome = await settlement.SettleAsync(command);

        Assert.Equal(SettlementOutcomeKind.Ignored, outcome.Kind);

        await using var verify = NewContext();
        Assert.Empty(await verify.IncomeLedgerEntries.ToListAsync());
        Assert.Empty(await verify.BlossomLedgerEntries.ToListAsync());
        Assert.Equal(
            PaymentProviderStatus.RequiresAction,
            (await verify.PaymentIntents.SingleAsync(row => row.Id == seeded.IntentId)).Status);

        var inbox = await verify.PaymentProviderEvents.SingleAsync(row => row.Id == seeded.InboxEventId);
        Assert.Null(inbox.ProcessedAt);
        Assert.NotNull(inbox.ProcessingError);
    }

    /// <summary>
    /// P10 deliverable 2 (plan §9.6): a provider dispute now has an explicit type, and its effect is
    /// a reversal recorded through <see cref="IIncomeLedgerService"/>. The ledger is append-only, so
    /// the reversal is a **new** <see cref="IncomeEntryKind.Refund"/> row and the original receipt is
    /// untouched; the settled charge is not silently rewritten either.
    /// </summary>
    [Fact]
    public async Task Settle_ADisputeEvent_AppendsARefundReversal_AndMarksTheEventProcessed()
    {
        var seeded = await SeedAsync();
        var settlement = CreateSettlement();

        // A chargeback reverses something: the charge has to have settled first.
        await settlement.SettleAsync(SucceededEvent(seeded));
        await SeedDisputeInboxAsync(seeded, "evt_dispute_1");

        var outcome = await settlement.SettleAsync(DisputeEvent(seeded, "evt_dispute_1"));

        Assert.Equal(SettlementOutcomeKind.Reversed, outcome.Kind);

        await using var verify = NewContext();

        // The original receipt is untouched: append-only means a correction is a new row.
        var receipt = await verify.IncomeLedgerEntries.SingleAsync(
            row => row.Kind == IncomeEntryKind.TopUpPurchase);
        Assert.Equal(IncomeEntryStatus.Recorded, receipt.Status);
        Assert.Equal(seeded.ProviderIntentId, receipt.SourceRef);

        var reversal = await verify.IncomeLedgerEntries.SingleAsync(
            row => row.Kind == IncomeEntryKind.Refund);
        // The provider's bank asserted it, so it is a receipt; it just moves money the other way.
        Assert.Equal(IncomeChargeBasis.Verified, reversal.ChargeBasis);
        // Attributed to no person: a dispute is the provider's act, and the ledger refuses an
        // unattributable row unless the source is System.
        Assert.Equal(IncomeSourceKind.System, reversal.SourceKind);
        Assert.Null(reversal.RecordedByUserId);
        Assert.Equal(9000m, reversal.Amount);
        // A reversal does not supersede the expectation it corrects; it stands beside it.
        Assert.Null(reversal.SupersedesEntryId);
        Assert.Contains(seeded.ProviderIntentId, reversal.SourceRef);

        // No silent mutation of a settled charge: the intent stays Succeeded, the money moves by row.
        Assert.Equal(
            PaymentProviderStatus.Succeeded,
            (await verify.PaymentIntents.SingleAsync(row => row.Id == seeded.IntentId)).Status);

        var inbox = await verify.PaymentProviderEvents.SingleAsync(
            row => row.ProviderEventId == "evt_dispute_1");
        Assert.NotNull(inbox.ProcessedAt);
        Assert.Null(inbox.ProcessingError);
    }

    /// <summary>
    /// Two dispute deliveries for the same charge must not reverse it twice. The ledger's
    /// <c>(SourceKind, SourceRef)</c> identity is what refuses the second row; the fail-safe stores
    /// the second event **unprocessed** rather than silently dropping it.
    /// </summary>
    [Fact]
    public async Task Settle_AReversedCharge_ThatIsDisputedAgain_IsRefusedByTheLedgerIdentity()
    {
        var seeded = await SeedAsync();
        var settlement = CreateSettlement();
        await settlement.SettleAsync(SucceededEvent(seeded));

        await SeedDisputeInboxAsync(seeded, "evt_dispute_1");
        await settlement.SettleAsync(DisputeEvent(seeded, "evt_dispute_1"));

        await SeedDisputeInboxAsync(seeded, "evt_dispute_2");
        await Assert.ThrowsAsync<DuplicateRevenueEntryException>(() =>
            settlement.SettleAsync(DisputeEvent(seeded, "evt_dispute_2")));

        await using var verify = NewContext();
        Assert.Single(await verify.IncomeLedgerEntries
            .Where(row => row.Kind == IncomeEntryKind.Refund)
            .ToListAsync());
        var second = await verify.PaymentProviderEvents.SingleAsync(
            row => row.ProviderEventId == "evt_dispute_2");
        Assert.Null(second.ProcessedAt);
    }

    private async Task SeedDisputeInboxAsync(Seeded seeded, string providerEventId)
    {
        _context.PaymentProviderEvents.Add(new PaymentProviderEvent
        {
            Id = Guid.CreateVersion7(),
            Provider = "mock",
            ProviderEventId = providerEventId,
            EventType = PaymentWebhookEventType.DisputeOpened,
            ProviderIntentId = seeded.ProviderIntentId,
            AmountMinor = 900000,
            Currency = "LKR",
            OccurredAt = Now.UtcDateTime,
            ReceivedAt = Now.UtcDateTime,
            RawPayload = "{\"type\":\"charge.disputed\"}",
        });
        await _context.SaveChangesAsync();
    }

    private static SettlePaymentCommand DisputeEvent(Seeded seeded, string providerEventId) =>
        new(
            "mock",
            new PaymentWebhookEvent(
                providerEventId,
                PaymentWebhookEventType.DisputeOpened,
                seeded.ProviderIntentId,
                null,
                new Money(900000, "LKR"),
                null,
                Now,
                "{\"type\":\"charge.disputed\"}"));

    private static MeterListener CaptureSettlementOutcomes(List<string> outcomes)
    {
        var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, owner) =>
        {
            if (instrument.Meter.Name == PaymentMetrics.MeterName
                && instrument.Name == "aveline.payment.settlement")
            {
                owner.EnableMeasurementEvents(instrument);
            }
        };

        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            string? outcome = null;
            foreach (var tag in tags)
            {
                if (tag.Key == "outcome")
                {
                    outcome = tag.Value?.ToString();
                }
            }

            if (outcome is not null)
            {
                lock (outcomes)
                {
                    outcomes.Add(outcome);
                }
            }
        });

        listener.Start();
        return listener;
    }
}
