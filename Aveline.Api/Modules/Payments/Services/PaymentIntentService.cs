using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Infrastructure.Eventing;
using Aveline.Api.Modules.Audit.Models;
using Aveline.Api.Modules.Audit.Services;
using Aveline.Api.Modules.Payments.Domain;
using Aveline.Api.Modules.Payments.Models;
using Aveline.Api.Modules.Payments.Repositories;
using Aveline.Api.Modules.Revenue.Models;
using Aveline.Api.Modules.Revenue.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aveline.Api.Modules.Payments.Services;

/// <summary>
/// The reference create/read/cancel/refund path of plan §6.4 (P2-B2).
/// </summary>
/// <remarks>
/// <para>
/// <b>On the create ordering.</b> §6.4 step 3 inserts the row and then calls the provider, and §6.6
/// requires that a transport failure commits nothing. Both hold here because the row is added to the
/// change tracker but **not saved** until the provider has answered: a failure leaves the insert
/// unsaved, and the transaction (relational providers) wraps the single save that follows.
/// </para>
/// <para>
/// <b>On the checkout URL.</b> §6.4 step 3d names a persisted <c>CheckoutUrl</c>, but the intent
/// model has no such column and this phase must not add a migration. The URL is the provider's own
/// state, so it is read back from the adapter that issued it rather than duplicated onto the row.
/// </para>
/// </remarks>
public sealed class PaymentIntentService(
    AppDbContext db,
    IPaymentIntentRepository repository,
    IPaymentProviderFactory providers,
    IIncomeLedgerService incomeLedger,
    IEventBus eventBus,
    IAuditService auditService,
    PaymentMetrics metrics,
    TimeProvider clock,
    ILogger<PaymentIntentService> logger,
    // Bound from `Payments:*`. Optional so the unit cases that never touch the refund window keep
    // their positional construction (plan §9.6).
    IOptions<PaymentsOptions>? options = null) : IPaymentIntentService
{
    /// <summary>Matches the intent column's <c>varchar(200)</c>.</summary>
    private const int MaxDescriptionLength = 200;

    /// <summary>Matches the failure-message column's <c>varchar(500)</c>.</summary>
    private const int MaxFailureMessageLength = 500;

    public async Task<PaymentIntentView> CreateAsync(
        CreatePaymentIntentCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var existing = await FindReplayAsync(command, cancellationToken);
        if (existing is not null)
        {
            return await ToViewAsync(existing, cancellationToken);
        }

        var provider = providers.Active;
        var now = clock.GetUtcNow().UtcDateTime;

        var intent = new PaymentIntent
        {
            OrganizationId = command.OrganizationId,
            Provider = provider.Key,
            Purpose = command.Purpose,
            Status = PaymentProviderStatus.RequiresAction,
            AmountMinor = command.Amount.AmountMinor,
            Currency = command.Amount.Currency,
            PriceLkr = command.Amount.ToMajorUnits(),
            SkuCode = command.SkuCode,
            BlossomQuantity = command.BlossomQuantity,
            PlanTier = command.PlanTier,
            IdempotencyKey = command.IdempotencyKey,
            Description = Truncate(command.Description, MaxDescriptionLength) ?? string.Empty,
            CreatedByUserId = command.CreatedByUserId,
            CreatedAt = now,
            UpdatedAt = now,
            BillingPeriodStart = command.BillingPeriodStart,
            BillingPeriodEnd = command.BillingPeriodEnd,
            ExpiresAt = command.ExpiresAt,
        };

        var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        ProviderPaymentIntent providerIntent;
        try
        {
            db.PaymentIntents.Add(intent);

            // The provider call carries Aveline's id as its idempotency key, so a retried create
            // returns the original charge rather than making a second one (plan §6.4 step 3c).
            providerIntent = await provider.CreatePaymentIntentAsync(
                new CreateProviderIntentRequest(
                    intent.Id,
                    new Money(intent.AmountMinor, intent.Currency),
                    intent.Purpose,
                    intent.Description,
                    intent.OrganizationId.ToString(),
                    intent.Id.ToString(),
                    ReturnUrl: null,
                    ExpiresAt: command.ExpiresAt),
                cancellationToken);

            intent.ProviderIntentId = providerIntent.ProviderIntentId;
            // ExternalRef is the SourceRef both ledgers will dedup on (plan §6.3).
            intent.ExternalRef = providerIntent.ProviderIntentId;
            intent.Status = providerIntent.Status;
            intent.FailureCode = Truncate(providerIntent.FailureCode, 64);
            intent.FailureMessage = Truncate(providerIntent.FailureMessage, MaxFailureMessageLength);
            if (providerIntent.ExpiresAt is { } providerExpiry)
            {
                intent.ExpiresAt = providerExpiry;
            }

            if (intent.Status == PaymentProviderStatus.Succeeded)
            {
                // The provider settled in place. The intent row may not claim Succeeded without a
                // settlement timestamp (CK_PaymentIntents_Settled), so stamp it; the grant itself is
                // dispatched by the settlement path, exactly as for a webhook-settled charge.
                intent.SettledAt = now;
                logger.LogWarning(
                    "Provider settled the charge in place at creation; the settlement effect is not "
                    + "dispatched by this path. intentId={IntentId} provider={Provider}",
                    intent.Id, intent.Provider);
            }

            intent.UpdatedAt = now;
            await repository.AddAsync(intent, cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }
        catch (PaymentProviderTransportException)
        {
            // §6.6: an unknown outcome is not a failure, and an intent with no provider intent behind
            // it is unactionable. Nothing was saved, so nothing has to be undone — but the entity is
            // still tracked as `Added`, and a later SaveChanges in the same scope (an audit write, for
            // example) would commit exactly the row §6.6 forbids. Detach it rather than trust that no
            // one will ever save again.
            db.Entry(intent).State = EntityState.Detached;
            throw;
        }
        catch (DbUpdateException exception)
        {
            db.Entry(intent).State = EntityState.Detached;
            throw new PaymentIdempotencyKeyReuseException(
                command.IdempotencyKey ?? string.Empty, exception);
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }

        metrics.RecordIntentCreated(intent.Provider, intent.Purpose.ToString());

        await eventBus.PublishAsync(
            "payment.intent.created",
            intent.OrganizationId,
            new
            {
                intentId = intent.Id,
                purpose = intent.Purpose.ToString(),
                provider = intent.Provider,
                amountLkr = intent.PriceLkr,
                expiresAt = intent.ExpiresAt,
            },
            cancellationToken: cancellationToken);

        await auditService.RecordAsync(new AuditEntryRequest(
            Action: PaymentAuditActions.IntentCreated,
            EntityType: nameof(PaymentIntent),
            EntityId: intent.Id.ToString(),
            OrganizationId: intent.OrganizationId,
            ActorKind: intent.CreatedByUserId is null ? AuditActorKind.System : AuditActorKind.User,
            ActorUserId: intent.CreatedByUserId,
            After: new
            {
                intent.Provider,
                intent.Purpose,
                intent.Status,
                intent.AmountMinor,
                intent.SkuCode,
            },
            Reason: intent.Description), cancellationToken);

        return PaymentIntentView.From(intent, providerIntent.CheckoutUrl?.ToString(), now);
    }

    public async Task<PaymentIntentView> GetAsync(
        Guid organizationId, Guid intentId, CancellationToken cancellationToken = default)
    {
        var intent = await RequireAsync(organizationId, intentId, cancellationToken);
        return await ToViewAsync(intent, cancellationToken);
    }

    public async Task<PaymentIntentView> CancelAsync(
        Guid organizationId, Guid intentId, string reason, CancellationToken cancellationToken = default)
    {
        var intent = await RequireAsync(organizationId, intentId, cancellationToken);

        if (intent.Status is not (PaymentProviderStatus.RequiresAction or PaymentProviderStatus.Processing))
        {
            throw new PaymentIntentStateException(
                $"A {intent.Status} payment intent cannot be cancelled; only an unsettled intent can.");
        }

        var provider = providers.Resolve(intent.Provider);
        var now = clock.GetUtcNow().UtcDateTime;

        if (!string.IsNullOrWhiteSpace(intent.ProviderIntentId))
        {
            var cancelled = await provider.CancelPaymentIntentAsync(
                intent.ProviderIntentId, Truncate(reason, 200) ?? "Payment intent cancelled.", cancellationToken);

            intent.Status = cancelled.Status;
            intent.FailureCode = Truncate(cancelled.FailureCode, 64);
            intent.FailureMessage = Truncate(cancelled.FailureMessage, MaxFailureMessageLength);
        }
        else
        {
            intent.Status = PaymentProviderStatus.Cancelled;
        }

        intent.UpdatedAt = now;
        await repository.UpdateAsync(intent, cancellationToken);

        await auditService.RecordAsync(new AuditEntryRequest(
            Action: PaymentAuditActions.IntentCancelled,
            EntityType: nameof(PaymentIntent),
            EntityId: intent.Id.ToString(),
            OrganizationId: intent.OrganizationId,
            ActorKind: AuditActorKind.User,
            After: new { intent.Status },
            Reason: reason), cancellationToken);

        logger.LogInformation(
            "Payment intent cancelled. orgId={OrganizationId} intentId={IntentId} provider={Provider}",
            intent.OrganizationId, intent.Id, intent.Provider);

        return PaymentIntentView.From(intent, null, now);
    }

    public async Task<PaymentRefundResult> RequestRefundAsync(
        Guid organizationId, Guid intentId, decimal? amountLkr, string reason,
        CancellationToken cancellationToken = default)
    {
        var intent = await RequireAsync(organizationId, intentId, cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;

        if (intent.Status != PaymentProviderStatus.Succeeded)
        {
            throw new PaymentIntentStateException(
                $"Only a Succeeded payment intent can be refunded; this one is {intent.Status}.");
        }

        if (intent.RefundedAt is not null)
        {
            throw new PaymentIntentStateException("This payment intent has already been fully refunded.");
        }

        var amount = amountLkr ?? intent.PriceLkr;
        if (amount <= 0m)
        {
            throw new PaymentIntentStateException("A refund amount must be greater than zero.");
        }

        if (amount > intent.PriceLkr)
        {
            throw new PaymentIntentStateException(
                $"The refund of {amount:0.00} is larger than the {intent.PriceLkr:0.00} settled charge.");
        }

        // The shipped revenue rule is kept (plan §9.6, decision D8): a refund returns money the
        // journal recorded collecting, so a live `Verified` receipt must exist for the charge's own
        // `(SourceKind, SourceRef)` identity. The check precedes the provider call so a charge the
        // ledger never recorded is refused without contacting anyone.
        var receiptRef = intent.ExternalRef ?? intent.ProviderIntentId!;
        var receiptSourceKind = ReceiptSourceKind(intent);
        var collected = await db.IncomeLedgerEntries.AnyAsync(
            entry => entry.OrganizationId == intent.OrganizationId
                && entry.SourceKind == receiptSourceKind
                && entry.SourceRef == receiptRef
                && entry.ChargeBasis == IncomeChargeBasis.Verified
                && entry.Status == IncomeEntryStatus.Recorded,
            cancellationToken);

        if (!collected)
        {
            throw new RevenueRefundNotAllowedException(
                $"no verified receipt exists for {receiptSourceKind} reference '{receiptRef}'.");
        }

        // Plan §9.6: the refund policy is unresolved in code, so the window is configuration and
        // absent (the default) means no automatic window at all — the operator decides. A configured
        // window refuses a refund struck outside it, before the provider is asked.
        if (options?.Value.RefundWindowDays is { } windowDays and > 0
            && intent.SettledAt is { } settledAt
            && now > settledAt.AddDays(windowDays))
        {
            throw new PaymentIntentStateException(
                $"The {windowDays}-day refund window for this charge closed on "
                + $"{settledAt.AddDays(windowDays):yyyy-MM-dd}; a refund outside it is an operator "
                + "decision, not an automatic one.");
        }

        var provider = providers.Resolve(intent.Provider);

        // Decision D8: the provider is asked first, so the ledger never records a refund a provider
        // refused.
        var refund = await provider.RefundAsync(
            new ProviderRefundRequest(
                intent.ProviderIntentId!,
                Money.Lkr(amount),
                $"refund:{intent.Id:N}",
                Truncate(reason, 200) ?? "Customer refund."),
            cancellationToken);

        var isFullRefund = amount >= intent.PriceLkr;
        var actorUserId = intent.CreatedByUserId is { } actor && actor != Guid.Empty ? actor : (Guid?)null;

        var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        IncomeLedgerEntry income;
        try
        {
            income = await incomeLedger.RecordAsync(new RecordIncomeCommand(
                intent.OrganizationId,
                amount,
                RefundReason(intent),
                IncomeEntryKind.Refund,
                IncomeChargeBasis.Verified,
                receiptSourceKind,
                // The provider's own refund id is the dedup identity: pointing at the charge would
                // collide with the very receipt being refunded.
                refund.ProviderRefundId,
                intent.BillingPeriodStart,
                intent.BillingPeriodEnd,
                now,
                actorUserId), cancellationToken);

            if (isFullRefund)
            {
                // `PaymentProviderStatus` has no Refunded member and this phase adds no migration, so
                // the refunded state is derived from RefundedAt rather than invented as an enum value
                // (see PaymentIntentView.From).
                intent.RefundedAt = now;
            }

            intent.UpdatedAt = now;
            await repository.UpdateAsync(intent, cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }

        metrics.RecordRefund(intent.Provider, "succeeded");

        await eventBus.PublishAsync(
            "payment.refunded",
            intent.OrganizationId,
            new
            {
                intentId = intent.Id,
                provider = intent.Provider,
                amountLkr = amount,
                ledgerEntryId = income.Id,
            },
            cancellationToken: cancellationToken);

        await auditService.RecordAsync(new AuditEntryRequest(
            Action: PaymentAuditActions.Refunded,
            EntityType: nameof(PaymentIntent),
            EntityId: intent.Id.ToString(),
            OrganizationId: intent.OrganizationId,
            ActorKind: actorUserId is null ? AuditActorKind.System : AuditActorKind.User,
            ActorUserId: actorUserId,
            Before: new { Status = nameof(PaymentProviderStatus.Succeeded) },
            After: new { intent.Status, intent.RefundedAt, RefundAmount = amount },
            Reason: reason), cancellationToken);

        logger.LogInformation(
            "Payment refunded. orgId={OrganizationId} intentId={IntentId} provider={Provider} "
            + "amountLkr={Amount} full={Full} providerRefundId={ProviderRefundId} ledgerEntryId={LedgerEntryId}",
            intent.OrganizationId, intent.Id, intent.Provider, amount, isFullRefund,
            refund.ProviderRefundId, income.Id);

        return new PaymentRefundResult(
            PaymentIntentView.From(intent, null, now), refund.ProviderRefundId, income.Id, amount);
    }

    private async Task<PaymentIntent?> FindReplayAsync(
        CreatePaymentIntentCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            return null;
        }

        var existing = await repository.FindByIdempotencyKeyAsync(
            command.OrganizationId, command.Purpose, command.IdempotencyKey, cancellationToken);

        if (existing is null)
        {
            return null;
        }

        if (existing.AmountMinor == command.Amount.AmountMinor
            && string.Equals(existing.SkuCode, command.SkuCode, StringComparison.Ordinal)
            && existing.BlossomQuantity == command.BlossomQuantity
            && existing.PlanTier == command.PlanTier)
        {
            return existing;
        }

        throw new PaymentIdempotencyKeyReuseException(command.IdempotencyKey);
    }

    private async Task<PaymentIntent> RequireAsync(
        Guid organizationId, Guid intentId, CancellationToken cancellationToken) =>
        await repository.GetAsync(organizationId, intentId, cancellationToken)
        ?? throw new PaymentIntentNotFoundException(intentId);

    /// <summary>
    /// Reads the provider's current checkout URL for the poll. A provider that cannot be reached must
    /// not fail the read — the persisted state is still the truth this endpoint serves.
    /// </summary>
    private async Task<PaymentIntentView> ToViewAsync(
        PaymentIntent intent, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        string? checkoutUrl = null;

        if (!string.IsNullOrWhiteSpace(intent.ProviderIntentId))
        {
            try
            {
                var providerIntent = await providers
                    .Resolve(intent.Provider)
                    .GetPaymentIntentAsync(intent.ProviderIntentId, cancellationToken);
                checkoutUrl = providerIntent?.CheckoutUrl?.ToString();
            }
            catch (PaymentDomainException exception)
            {
                logger.LogDebug(
                    exception,
                    "Could not read the provider's checkout URL for intent {IntentId}; serving the "
                    + "persisted state.",
                    intent.Id);
            }
        }

        return PaymentIntentView.From(intent, checkoutUrl, now);
    }

    /// <summary>
    /// The ledger reason a refund is booked under. Deliberately descriptive rather than the
    /// operator's free text: the journal's reason column is a statement of what the movement was,
    /// while the caller's own words reach the provider and the audit entry.
    /// </summary>
    private static string RefundReason(PaymentIntent intent) => intent.Purpose switch
    {
        PaymentPurpose.SubscriptionProration =>
            $"Refund of the prorated plan-change charge for {intent.PlanTier}.",
        PaymentPurpose.SubscriptionRenewal =>
            $"Refund of the {intent.BillingPeriodStart:yyyy-MM} plan renewal charge.",
        PaymentPurpose.SubscriptionInitial => "Refund of the initial subscription charge.",
        _ => $"Refund for top-up purchase {intent.SkuCode} "
             + $"({intent.BlossomQuantity:0.####} Blossoms).",
    };

    /// <summary>
    /// The income source kind a receipt for this intent is written under, and therefore the one a
    /// refund looks for. A top-up with an attributed actor is the top-up namespace; a
    /// system-created top-up and every subscription charge are the system namespace.
    /// </summary>
    private static IncomeSourceKind ReceiptSourceKind(PaymentIntent intent) =>
        intent.Purpose == PaymentPurpose.BlossomTopUp && AttributedActor(intent) is not null
            ? IncomeSourceKind.BlossomTopUp
            : IncomeSourceKind.System;

    private static Guid? AttributedActor(PaymentIntent intent) =>
        intent.CreatedByUserId is { } actor && actor != Guid.Empty ? actor : null;

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
