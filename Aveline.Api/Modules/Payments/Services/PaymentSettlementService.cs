using System.Diagnostics;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Infrastructure.Eventing;
using Aveline.Api.Modules.Audit.Models;
using Aveline.Api.Modules.Audit.Services;
using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Billing.Services;
using Aveline.Api.Modules.Payments.Domain;
using Aveline.Api.Modules.Payments.Models;
using Aveline.Api.Modules.Payments.Repositories;
using Aveline.Api.Modules.Revenue.Models;
using Aveline.Api.Modules.Revenue.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace Aveline.Api.Modules.Payments.Services;

/// <summary>
/// The reference settlement flow of plan §6.4 steps 8-10 for <c>BlossomTopUp</c>: one database
/// transaction across the Blossom grant, the verified income receipt, the intent's terminal state and
/// the inbox row's <c>ProcessedAt</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why one transaction.</b> §6.6 forbids a partial settlement by construction. Both writers are
/// idempotent as well, so a replayed webhook which somehow reached this method twice would still not
/// double-grant: the Blossom ledger dedups on
/// <c>(OrganizationId, "payments.topup", ProviderIntentId)</c> and the income ledger on
/// <c>(BlossomTopUp, providerIntentId)</c>.
/// </para>
/// <para>
/// <b>Order of the writes.</b> The Blossom grant precedes the income receipt, as §6.4 step 9 says.
/// On a relational provider the transaction makes the order immaterial; the atomicity case runs
/// against PostgreSQL (<c>PaymentSettlementAtomicityPostgresTests</c>) because the in-memory provider
/// has no transactions to roll back.
/// </para>
/// </remarks>
public sealed class PaymentSettlementService(
    AppDbContext db,
    IPaymentIntentRepository intents,
    IPaymentProviderEventRepository providerEvents,
    IBlossomService blossoms,
    IIncomeLedgerService incomeLedger,
    IEventBus eventBus,
    IAuditService auditService,
    PaymentMetrics metrics,
    TimeProvider clock,
    ILogger<PaymentSettlementService> logger,
    // Optional so the unit-level cases that only exercise top-up settlement can construct this
    // without the subscription side; the composition root registers it (plan §9.4 F4).
    ISubscriptionRenewalService? subscriptionRenewals = null) : IPaymentSettlementService
{
    /// <summary>Matches the inbox column's <c>varchar(500)</c>.</summary>
    private const int MaxProcessingErrorLength = 500;

    /// <summary>The Blossom ledger's idempotency scope for provider-settled top-ups (plan C9).</summary>
    internal const string TopUpIdempotencyScope = "payments.topup";

    /// <summary>The income ledger's idempotency scope for a provider-settled proration charge.</summary>
    internal const string ProrationIdempotencyScope = "payments.proration";

    public async Task<SettlementOutcome> SettleAsync(
        SettlePaymentCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var providerEvent = command.Event;
        var provider = command.Provider;

        if (string.IsNullOrWhiteSpace(providerEvent.ProviderIntentId))
        {
            await RecordProcessingErrorAsync(
                command, "The provider event does not name an intent.", cancellationToken);
            throw new PaymentIntentStateException(
                "A settlement event must name the provider intent it applies to.");
        }

        var intent = await intents.FindByProviderIntentIdAsync(
            provider, providerEvent.ProviderIntentId, cancellationToken);

        if (intent is null)
        {
            await RecordProcessingErrorAsync(
                command,
                $"No payment intent matches {provider} charge '{providerEvent.ProviderIntentId}'.",
                cancellationToken);
            throw new PaymentIntentNotFoundException(
                $"No payment intent matches {provider} charge '{providerEvent.ProviderIntentId}'.");
        }

        if (command.ExpectedOrganizationId is { } expected && intent.OrganizationId != expected)
        {
            // Fail closed without disclosing whether the id exists for another tenant.
            await RecordProcessingErrorAsync(
                command, "The event's organisation does not own the intent.", cancellationToken);
            throw new PaymentIntentNotFoundException(intent.Id);
        }

        return providerEvent.Type switch
        {
            PaymentWebhookEventType.IntentSucceeded =>
                await SettleSucceededAsync(command, intent, cancellationToken),
            PaymentWebhookEventType.IntentFailed =>
                await MarkTerminalAsync(command, intent, PaymentProviderStatus.Failed, cancellationToken),
            PaymentWebhookEventType.IntentCancelled =>
                await MarkTerminalAsync(command, intent, PaymentProviderStatus.Cancelled, cancellationToken),
            PaymentWebhookEventType.IntentExpired =>
                await MarkTerminalAsync(command, intent, PaymentProviderStatus.Expired, cancellationToken),
            PaymentWebhookEventType.DisputeOpened =>
                await ReverseDisputeAsync(command, intent, cancellationToken),
            _ => await IgnoreAsync(command, intent, cancellationToken),
        };
    }

    private async Task<SettlementOutcome> SettleSucceededAsync(
        SettlePaymentCommand command, PaymentIntent intent, CancellationToken cancellationToken)
    {
        var providerEvent = command.Event;
        var purpose = intent.Purpose.ToString();
        var now = clock.GetUtcNow().UtcDateTime;

        if (intent.Status == PaymentProviderStatus.Succeeded)
        {
            // Belt-and-braces on top of the inbox and the ledger tuples. The new inbox row is still
            // marked processed: it is a distinct event, and leaving it queued would grow the backlog
            // forever.
            await MarkProcessedAsync(command, now, cancellationToken);
            metrics.RecordSettlement(intent.Provider, purpose, "succeeded");
            logger.LogInformation(
                "Settlement replayed against an already-settled intent; no further effect. "
                + "intentId={IntentId} provider={Provider} eventId={EventId}",
                intent.Id, intent.Provider, providerEvent.ProviderEventId);
            return new SettlementOutcome(SettlementOutcomeKind.AlreadySettled, intent.Id, "AlreadySettled");
        }

        if (intent.Status is not (PaymentProviderStatus.RequiresAction or PaymentProviderStatus.Processing))
        {
            await RecordProcessingErrorAsync(
                command,
                $"A {intent.Status} intent cannot be settled.",
                cancellationToken);
            throw new PaymentIntentStateException(
                $"A {intent.Status} payment intent cannot be settled.");
        }

        if (intent.ExpiresAt is { } expiresAt && expiresAt <= now)
        {
            await RecordProcessingErrorAsync(command, "The intent expired before settlement.", cancellationToken);
            throw new PaymentIntentStateException(
                "The payment intent expired before its settlement arrived.");
        }

        if (providerEvent.Amount is { } amount)
        {
            if (amount.AmountMinor != intent.AmountMinor)
            {
                var detail =
                    $"Event amount {amount.AmountMinor} does not match the intent's {intent.AmountMinor}.";
                await RecordProcessingErrorAsync(command, detail, cancellationToken);
                metrics.RecordSettlement(intent.Provider, purpose, "mismatch");
                throw new PaymentIntentMismatchException(detail);
            }

            if (!string.Equals(amount.Currency, intent.Currency, StringComparison.OrdinalIgnoreCase))
            {
                var detail =
                    $"Event currency {amount.Currency} does not match the intent's {intent.Currency}.";
                await RecordProcessingErrorAsync(command, detail, cancellationToken);
                metrics.RecordSettlement(intent.Provider, purpose, "mismatch");
                throw new PaymentIntentMismatchException(detail);
            }
        }

        if (intent.Purpose == PaymentPurpose.SubscriptionRenewal)
        {
            return await SettleSubscriptionRenewalAsync(command, intent, now, cancellationToken);
        }

        if (intent.Purpose == PaymentPurpose.SubscriptionProration)
        {
            return await SettleProrationAsync(command, intent, now, cancellationToken);
        }

        if (intent.Purpose != PaymentPurpose.BlossomTopUp)
        {
            // Deliberately not silently ignored: the event stays unprocessed so the remaining
            // purposes (SubscriptionInitial, and CommerceOrder until Phase 9) are visible to
            // reconciliation rather than the charge vanishing between two phases.
            await RecordProcessingErrorAsync(
                command,
                $"Settlement for purpose {intent.Purpose} is not implemented in this phase.",
                cancellationToken);
            metrics.RecordSettlement(intent.Provider, purpose, "failed");
            logger.LogWarning(
                "Received a settlement for a purpose this phase does not apply. "
                + "intentId={IntentId} purpose={Purpose}",
                intent.Id, intent.Purpose);
            return new SettlementOutcome(SettlementOutcomeKind.Ignored, intent.Id, "UnsupportedPurpose");
        }

        var started = Stopwatch.GetTimestamp();
        var sourceRef = intent.ExternalRef ?? intent.ProviderIntentId!;
        // The cap was resolved before payment and persisted on the intent (deliverable 6), so a
        // settlement after a period boundary cannot extend or truncate the grant. Null means the
        // purchase was deliberately uncapped.
        var grantExpiresAt = intent.BillingPeriodEnd;

        BlossomLedgerEntry grant;
        IncomeLedgerEntry receipt;

        var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        try
        {
            grant = await blossoms.CreditAsync(new CreditBlossomsCommand(
                intent.OrganizationId,
                intent.BlossomQuantity!.Value,
                $"Top-up purchase {intent.SkuCode} ({intent.BlossomQuantity:0.####} Blossoms).",
                grantExpiresAt,
                BlossomSourceKind.PaymentProvider,
                sourceRef,
                intent.CreatedByUserId,
                // C9: both halves, or the ledger's dedup is silently disabled.
                intent.ProviderIntentId,
                TopUpIdempotencyScope,
                BlossomLedgerEntryType.TopUpGrant), cancellationToken);

            // D4: a provider confirmed it, so this is a receipt and not an expectation. It goes
            // through the shared verify path (plan §9.9 item 1) so the webhook settlement and the
            // admin route cannot diverge: if a `Derived` expectation ever holds the same identity,
            // it is superseded rather than rejected as a duplicate (risk R3).
            receipt = await incomeLedger.VerifyAsync(new VerifyIncomeCommand(
                intent.OrganizationId,
                intent.PriceLkr,
                $"Top-up purchase {intent.SkuCode} ({intent.BlossomQuantity:0.####} Blossoms).",
                IncomeEntryKind.TopUpPurchase,
                ReceiptSourceKind(intent),
                sourceRef,
                now,
                AttributedActor(intent),
                PeriodStart: intent.BillingPeriodStart,
                PeriodEnd: intent.BillingPeriodEnd), cancellationToken);

            intent.Status = PaymentProviderStatus.Succeeded;
            intent.SettledAt = now;
            intent.ExternalRef = sourceRef;
            intent.UpdatedAt = now;
            await intents.UpdateAsync(intent, cancellationToken);

            await MarkProcessedAsync(command, now, cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }
        catch
        {
            metrics.RecordSettlement(intent.Provider, purpose, "failed");
            throw;
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }

        metrics.RecordSettlement(intent.Provider, purpose, "succeeded");
        metrics.RecordSettlementLatency(
            intent.Provider, purpose, Stopwatch.GetElapsedTime(started).TotalMilliseconds);

        await eventBus.PublishAsync(
            "payment.settled",
            intent.OrganizationId,
            new
            {
                intentId = intent.Id,
                purpose = intent.Purpose.ToString(),
                provider = intent.Provider,
                amountLkr = intent.PriceLkr,
                blossomDelta = intent.BlossomQuantity,
                ledgerEntryId = grant.Id,
                incomeEntryId = receipt.Id,
            },
            cancellationToken: cancellationToken);

        await auditService.RecordAsync(new AuditEntryRequest(
            Action: PaymentAuditActions.Settled,
            EntityType: nameof(PaymentIntent),
            EntityId: intent.Id.ToString(),
            OrganizationId: intent.OrganizationId,
            ActorKind: AuditActorKind.System,
            ActorUserId: intent.CreatedByUserId,
            Before: new { Status = nameof(PaymentProviderStatus.RequiresAction) },
            After: new
            {
                Status = nameof(PaymentProviderStatus.Succeeded),
                intent.SettledAt,
                BlossomGrant = intent.BlossomQuantity,
                AmountLkr = intent.PriceLkr,
            },
            Reason: $"Provider {intent.Provider} confirmed event {providerEvent.ProviderEventId}."),
            cancellationToken);

        logger.LogInformation(
            "Payment settled. orgId={OrganizationId} intentId={IntentId} provider={Provider} "
            + "purpose={Purpose} amountLkr={Amount}",
            intent.OrganizationId, intent.Id, intent.Provider, intent.Purpose, intent.PriceLkr);

        return new SettlementOutcome(SettlementOutcomeKind.Settled, intent.Id, "Settled");
    }

    /// <summary>
    /// The subscription analogue of the top-up settlement. The <c>Verified</c> receipt, the period
    /// advance and the dunning state belong to <see cref="ISubscriptionRenewalService"/>, so this
    /// path only moves the intent and the inbox row and then publishes and audits the outcome.
    /// </summary>
    /// <remarks>
    /// A renewal whose effect cannot be applied stays unprocessed (a null <c>ProcessedAt</c> and a
    /// <c>ProcessingError</c>) rather than being swallowed, so reconciliation can see it.
    /// </remarks>
    private async Task<SettlementOutcome> SettleSubscriptionRenewalAsync(
        SettlePaymentCommand command, PaymentIntent intent, DateTime now, CancellationToken cancellationToken)
    {
        var purpose = intent.Purpose.ToString();

        if (subscriptionRenewals is null)
        {
            await RecordProcessingErrorAsync(
                command,
                "Subscription renewal settlement is not wired in this deployment.",
                cancellationToken);
            metrics.RecordSettlement(intent.Provider, purpose, "failed");
            return new SettlementOutcome(SettlementOutcomeKind.Ignored, intent.Id, "NoRenewalHandler");
        }

        try
        {
            await subscriptionRenewals.ApplySettlementAsync(intent, now, cancellationToken);

            intent.Status = PaymentProviderStatus.Succeeded;
            intent.SettledAt = now;
            intent.UpdatedAt = now;
            await intents.UpdateAsync(intent, cancellationToken);

            await MarkProcessedAsync(command, now, cancellationToken);
        }
        catch (Exception exception)
        {
            await RecordProcessingErrorAsync(
                command,
                Truncate(exception.Message, MaxProcessingErrorLength)
                    ?? "The renewal settlement could not be applied.",
                cancellationToken);
            metrics.RecordSettlement(intent.Provider, purpose, "failed");
            throw;
        }

        metrics.RecordSettlement(intent.Provider, purpose, "succeeded");

        await eventBus.PublishAsync(
            "payment.settled",
            intent.OrganizationId,
            new
            {
                intentId = intent.Id,
                purpose = intent.Purpose.ToString(),
                provider = intent.Provider,
                amountLkr = intent.PriceLkr,
            },
            cancellationToken: cancellationToken);

        logger.LogInformation(
            "Subscription renewal settled. orgId={OrganizationId} intentId={IntentId} "
            + "provider={Provider} amountLkr={Amount}",
            intent.OrganizationId, intent.Id, intent.Provider, intent.PriceLkr);

        return new SettlementOutcome(SettlementOutcomeKind.Settled, intent.Id, "Settled");
    }

    /// <summary>
    /// The proration settlement P5 left open: a settled <c>SubscriptionProration</c> charge is money
    /// the journal must record, so it writes one <c>Verified</c> receipt through the shared
    /// <see cref="IIncomeLedgerService.VerifyAsync"/> path rather than being left outstanding with an
    /// unprocessed inbox row (plan §9.3 F3, deliverable 4).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A proration charge has no Blossom effect — it is not a purchase — so the effect is the
    /// receipt alone. P5 raises the charge without first writing a <c>Derived</c> expectation, so
    /// there is usually nothing to supersede; routing through <c>VerifyAsync</c> anyway means the
    /// one shared path still applies if a derived row ever exists for the same identity.
    /// </para>
    /// <para>
    /// The receipt is a <see cref="IncomeSourceKind.System"/> write because the settlement is
    /// provider-driven rather than an operator's confirmation; the plan-change actor is still
    /// recorded when there is one.
    /// </para>
    /// </remarks>
    private async Task<SettlementOutcome> SettleProrationAsync(
        SettlePaymentCommand command, PaymentIntent intent, DateTime now, CancellationToken cancellationToken)
    {
        var purpose = intent.Purpose.ToString();
        var sourceRef = intent.ExternalRef ?? intent.ProviderIntentId!;
        var started = Stopwatch.GetTimestamp();

        var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        IncomeLedgerEntry receipt;
        try
        {
            receipt = await incomeLedger.VerifyAsync(
                new VerifyIncomeCommand(
                    intent.OrganizationId,
                    intent.PriceLkr,
                    $"Prorated plan-change charge for {intent.PlanTier} settled by {intent.Provider}.",
                    IncomeEntryKind.SubscriptionCharge,
                    IncomeSourceKind.System,
                    sourceRef,
                    now,
                    AttributedActor(intent),
                    PeriodStart: intent.BillingPeriodStart,
                    PeriodEnd: intent.BillingPeriodEnd,
                    IdempotencyKey: intent.ProviderIntentId,
                    IdempotencyScope: ProrationIdempotencyScope),
                cancellationToken);

            intent.Status = PaymentProviderStatus.Succeeded;
            intent.SettledAt = now;
            intent.ExternalRef = sourceRef;
            intent.UpdatedAt = now;
            await intents.UpdateAsync(intent, cancellationToken);

            await MarkProcessedAsync(command, now, cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }
        catch
        {
            metrics.RecordSettlement(intent.Provider, purpose, "failed");
            throw;
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }

        metrics.RecordSettlement(intent.Provider, purpose, "succeeded");
        metrics.RecordSettlementLatency(
            intent.Provider, purpose, Stopwatch.GetElapsedTime(started).TotalMilliseconds);

        await eventBus.PublishAsync(
            "payment.settled",
            intent.OrganizationId,
            new
            {
                intentId = intent.Id,
                purpose = intent.Purpose.ToString(),
                provider = intent.Provider,
                amountLkr = intent.PriceLkr,
                incomeEntryId = receipt.Id,
            },
            cancellationToken: cancellationToken);

        await auditService.RecordAsync(new AuditEntryRequest(
            Action: PaymentAuditActions.Settled,
            EntityType: nameof(PaymentIntent),
            EntityId: intent.Id.ToString(),
            OrganizationId: intent.OrganizationId,
            ActorKind: AuditActorKind.System,
            ActorUserId: intent.CreatedByUserId,
            Before: new { Status = nameof(PaymentProviderStatus.RequiresAction) },
            After: new { Status = nameof(PaymentProviderStatus.Succeeded), intent.SettledAt, intent.PriceLkr },
            Reason: $"Provider {intent.Provider} settled the prorated plan-change charge "
            + $"{command.Event.ProviderEventId}."),
            cancellationToken);

        logger.LogInformation(
            "Proration settled. orgId={OrganizationId} intentId={IntentId} provider={Provider} "
            + "amountLkr={Amount}",
            intent.OrganizationId, intent.Id, intent.Provider, intent.PriceLkr);

        return new SettlementOutcome(SettlementOutcomeKind.Settled, intent.Id, "Settled");
    }

    /// <summary>
    /// A failed, cancelled or expired provider charge still moves the intent, and still marks the
    /// inbox row processed: nothing will ever be granted for it, so queuing it would only grow the
    /// backlog.
    /// </summary>
    private async Task<SettlementOutcome> MarkTerminalAsync(
        SettlePaymentCommand command,
        PaymentIntent intent,
        PaymentProviderStatus terminal,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var purpose = intent.Purpose.ToString();

        if (intent.Status is PaymentProviderStatus.Succeeded or PaymentProviderStatus.Failed
            or PaymentProviderStatus.Cancelled or PaymentProviderStatus.Expired)
        {
            await MarkProcessedAsync(command, now, cancellationToken);
            return new SettlementOutcome(SettlementOutcomeKind.AlreadySettled, intent.Id, "Terminal");
        }

        intent.Status = terminal;
        intent.FailureCode = terminal == PaymentProviderStatus.Failed
            ? Truncate(command.Event.FailureCode, 64)
            : intent.FailureCode;
        intent.UpdatedAt = now;
        await intents.UpdateAsync(intent, cancellationToken);
        await MarkProcessedAsync(command, now, cancellationToken);

        metrics.RecordSettlement(intent.Provider, purpose, "failed");

        await eventBus.PublishAsync(
            "payment.failed",
            intent.OrganizationId,
            new
            {
                intentId = intent.Id,
                purpose = intent.Purpose.ToString(),
                provider = intent.Provider,
                failureCode = intent.FailureCode,
            },
            cancellationToken: cancellationToken);

        await auditService.RecordAsync(new AuditEntryRequest(
            Action: PaymentAuditActions.Failed,
            EntityType: nameof(PaymentIntent),
            EntityId: intent.Id.ToString(),
            OrganizationId: intent.OrganizationId,
            ActorKind: AuditActorKind.System,
            ActorUserId: intent.CreatedByUserId,
            After: new { Status = terminal.ToString(), intent.FailureCode },
            Reason: $"Provider {intent.Provider} reported {command.Event.Type}."),
            cancellationToken);

        logger.LogInformation(
            "Payment intent moved to {Status}. orgId={OrganizationId} intentId={IntentId} "
            + "provider={Provider} failureCode={FailureCode}",
            terminal, intent.OrganizationId, intent.Id, intent.Provider, intent.FailureCode);

        return new SettlementOutcome(SettlementOutcomeKind.Ignored, intent.Id, terminal.ToString());
    }

    /// <summary>
    /// A provider dispute (a chargeback) reverses money the provider had already collected. The
    /// effect is an **append-only** <see cref="IncomeEntryKind.Refund"/> row through
    /// <see cref="IIncomeLedgerService"/>: the settled charge, its Blossom grant and its verified
    /// receipt are never mutated, and the intent stays <see cref="PaymentProviderStatus.Succeeded"/>
    /// because the charge genuinely did succeed — a dispute is a separate movement of money.
    /// </summary>
    /// <remarks>
    /// The ledger's <c>(SourceKind, SourceRef)</c> identity is the dedup: a second reversal for the
    /// same charge is refused, and the event is only marked processed after the write succeeded, so
    /// a refused or failed reversal stays visible in the reconciliation backlog rather than becoming
    /// a processed no-op (plan §9.6, P10).
    /// </remarks>
    private async Task<SettlementOutcome> ReverseDisputeAsync(
        SettlePaymentCommand command, PaymentIntent intent, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var purpose = intent.Purpose.ToString();
        // The provider's reported disputed amount when it gives one; otherwise the whole charge.
        var disputedMinor = command.Event.Amount?.AmountMinor ?? intent.AmountMinor;
        var amountLkr = disputedMinor / 100m;

        var reversal = await incomeLedger.RecordAsync(
            new RecordIncomeCommand(
                intent.OrganizationId,
                amountLkr,
                $"Provider {intent.Provider} opened a dispute against charge "
                + $"{intent.ProviderIntentId} ({amountLkr:0.00} LKR); the amount is reversed.",
                IncomeEntryKind.Refund,
                // The bank asserted it, so it is a receipt and not an expectation - it simply moves
                // money the other way, which `Kind = Refund` signs.
                IncomeChargeBasis.Verified,
                // A dispute is the provider's act and belongs to no person, and `System` is the one
                // source the ledger permits an unattributed row on.
                IncomeSourceKind.System,
                $"payment-dispute:{intent.ProviderIntentId}",
                PeriodStart: null,
                PeriodEnd: null,
                command.Event.OccurredAt.UtcDateTime,
                RecordedByUserId: null),
            cancellationToken);

        await MarkProcessedAsync(command, now, cancellationToken);

        metrics.RecordSettlement(intent.Provider, purpose, "reversed");

        await eventBus.PublishAsync(
            "payment.disputed",
            intent.OrganizationId,
            new
            {
                intentId = intent.Id,
                purpose,
                provider = intent.Provider,
                amountLkr,
                reversalEntryId = reversal.Id,
            },
            cancellationToken: cancellationToken);

        await auditService.RecordAsync(new AuditEntryRequest(
            Action: PaymentAuditActions.Disputed,
            EntityType: nameof(PaymentIntent),
            EntityId: intent.Id.ToString(),
            OrganizationId: intent.OrganizationId,
            ActorKind: AuditActorKind.System,
            ActorUserId: null,
            Before: new { Status = intent.Status.ToString() },
            After: new { ReversalEntryId = reversal.Id, AmountLkr = amountLkr, intent.ProviderIntentId },
            Reason: $"Provider {intent.Provider} opened dispute {command.Event.ProviderEventId}."),
            cancellationToken);

        logger.LogWarning(
            "Provider dispute reversed a settled charge. orgId={OrganizationId} intentId={IntentId} "
            + "provider={Provider} eventId={EventId} amountLkr={Amount}",
            intent.OrganizationId, intent.Id, intent.Provider, command.Event.ProviderEventId, amountLkr);

        return new SettlementOutcome(SettlementOutcomeKind.Reversed, intent.Id, "Reversed");
    }

    /// <summary>
    /// An event type that carries no ledger effect is recorded as processed so it leaves the
    /// backlog, and logged. Refund confirmations are Phase 6: refunds here are operator-initiated and
    /// the provider call is made before the ledger write, so there is no async confirmation to apply.
    /// </summary>
    /// <remarks>
    /// <see cref="PaymentWebhookEventType.Unknown"/> is the deliberate exception (plan §9.6,
    /// deliverable 5). At P10 a dispute is no longer one of these — it has its own type and its
    /// append-only reversal — but a type that is still without a handler is stored **unprocessed**
    /// with a <c>ProcessingError</c> so reconciliation surfaces it, and logged; marking it processed
    /// would forget it.
    /// </remarks>
    private async Task<SettlementOutcome> IgnoreAsync(
        SettlePaymentCommand command, PaymentIntent intent, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;

        if (command.Event.Type == PaymentWebhookEventType.Unknown)
        {
            await RecordProcessingErrorAsync(
                command,
                $"Provider event type '{command.Event.Type}' has no settlement effect in this phase.",
                cancellationToken);

            logger.LogWarning(
                "Unrecognised provider event stored unprocessed. intentId={IntentId} "
                + "provider={Provider} eventId={EventId}",
                intent.Id, intent.Provider, command.Event.ProviderEventId);

            return new SettlementOutcome(
                SettlementOutcomeKind.Ignored, intent.Id, nameof(PaymentWebhookEventType.Unknown));
        }

        await MarkProcessedAsync(command, now, cancellationToken);
        logger.LogInformation(
            "Provider event carries no settlement effect. intentId={IntentId} provider={Provider} "
            + "eventId={EventId} type={Type}",
            intent.Id, intent.Provider, command.Event.ProviderEventId, command.Event.Type);
        return new SettlementOutcome(SettlementOutcomeKind.Ignored, intent.Id, command.Event.Type.ToString());
    }

    /// <summary>
    /// The income ledger refuses an unattributable money entry unless the source kind is
    /// <see cref="IncomeSourceKind.System"/>. The checkout route always has an authenticated
    /// initiator, so the plan's <see cref="IncomeSourceKind.BlossomTopUp"/> is what a real settlement
    /// writes; a system-created intent (a renewal in a later phase) falls back to
    /// <see cref="IncomeSourceKind.System"/> rather than inventing an actor.
    /// </summary>
    private static IncomeSourceKind ReceiptSourceKind(PaymentIntent intent) =>
        AttributedActor(intent) is null ? IncomeSourceKind.System : IncomeSourceKind.BlossomTopUp;

    private static Guid? AttributedActor(PaymentIntent intent) =>
        intent.CreatedByUserId is { } actor && actor != Guid.Empty ? actor : null;

    private async Task MarkProcessedAsync(
        SettlePaymentCommand command, DateTime now, CancellationToken cancellationToken)
    {
        var inbox = await providerEvents.FindByProviderEventIdAsync(
            command.Provider, command.Event.ProviderEventId, cancellationToken);

        if (inbox is null)
        {
            // A direct service call (tests, a future reconciliation job) may settle without an inbox
            // row; the ledger tuples still make the effect idempotent.
            logger.LogDebug(
                "Settled an event with no inbox row. provider={Provider} eventId={EventId}",
                command.Provider, command.Event.ProviderEventId);
            return;
        }

        inbox.ProcessedAt = now;
        inbox.ProcessingError = null;
        await providerEvents.UpdateAsync(inbox, cancellationToken);
    }

    /// <summary>
    /// Stores the event with a null <c>ProcessedAt</c> and a <c>ProcessingError</c>, which is what
    /// makes a mismatch (or a retryable failure) visible rather than silently reconciled.
    /// </summary>
    private async Task RecordProcessingErrorAsync(
        SettlePaymentCommand command, string error, CancellationToken cancellationToken)
    {
        var inbox = await providerEvents.FindByProviderEventIdAsync(
            command.Provider, command.Event.ProviderEventId, cancellationToken);

        if (inbox is null)
        {
            return;
        }

        inbox.ProcessedAt = null;
        inbox.ProcessingError = Truncate(error, MaxProcessingErrorLength);
        await providerEvents.UpdateAsync(inbox, cancellationToken);
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
