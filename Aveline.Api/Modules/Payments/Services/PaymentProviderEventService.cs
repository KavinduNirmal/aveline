using Aveline.Api.Modules.Payments.Domain;
using Aveline.Api.Modules.Payments.Models;
using Aveline.Api.Modules.Payments.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aveline.Api.Modules.Payments.Services;

/// <summary>
/// The webhook inbox's writer and the settlement dispatcher (plan §6.4 steps 6-7).
/// </summary>
/// <remarks>
/// The inbox is not an optimisation. A provider retries webhooks, and the settlement path is
/// idempotent regardless; the inbox is what makes a *duplicate* distinguishable from a *retry of a
/// failed dispatch*, which an idempotency-only design cannot tell apart (plan §6.3). A retry of an
/// unprocessed dispatch is deliberately not re-driven here — the inbound delivery is answered
/// "already seen" and the Phase 7 sweep owns the backlog.
/// </remarks>
public sealed class PaymentProviderEventService(
    IPaymentProviderFactory providers,
    IPaymentProviderEventRepository repository,
    IPaymentSettlementService settlement,
    PaymentMetrics metrics,
    TimeProvider clock,
    ILogger<PaymentProviderEventService> logger) : IPaymentProviderEventService
{
    public async Task<WebhookOutcome> IngestAsync(
        string providerKey, PaymentWebhookRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var provider = providers.Resolve(providerKey);

        // Verification happens before anything is persisted, and the raw body never reaches a JSON
        // binder before this point (the Clerk webhook precedent).
        var parsed = provider.VerifyAndParseWebhook(request);

        metrics.RecordWebhookReceived(providerKey, parsed.Type.ToString());

        var existing = await repository.FindByProviderEventIdAsync(
            providerKey, parsed.ProviderEventId, cancellationToken);

        if (existing is not null)
        {
            logger.LogInformation(
                "Duplicate provider event ignored. provider={Provider} eventId={EventId} "
                + "processed={Processed}",
                providerKey, parsed.ProviderEventId, existing.ProcessedAt is not null);
            return new WebhookOutcome(
                IsNew: false,
                Processed: existing.ProcessedAt is not null,
                InboxEventId: existing.Id,
                Detail: "already-seen");
        }

        var inbox = new PaymentProviderEvent
        {
            Provider = providerKey,
            ProviderEventId = parsed.ProviderEventId,
            EventType = parsed.Type,
            ProviderIntentId = parsed.ProviderIntentId,
            AmountMinor = parsed.Amount?.AmountMinor,
            Currency = parsed.Amount?.Currency,
            OccurredAt = parsed.OccurredAt.UtcDateTime,
            ReceivedAt = request.ReceivedAt.UtcDateTime,
            // Kept verbatim for forensics, matching IdempotencyRecords.ResponseBodyJson.
            RawPayload = parsed.RawPayload,
        };

        try
        {
            await repository.AddAsync(inbox, cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            // Two deliveries raced; the unique index picked a winner, and this one is a duplicate.
            logger.LogWarning(
                exception,
                "Provider event {EventId} from {Provider} was stored concurrently.",
                parsed.ProviderEventId, providerKey);

            var raced = await repository.FindByProviderEventIdAsync(
                providerKey, parsed.ProviderEventId, cancellationToken);
            return new WebhookOutcome(
                IsNew: false,
                Processed: raced?.ProcessedAt is not null,
                InboxEventId: raced?.Id,
                Detail: "already-seen");
        }

        logger.LogInformation(
            "Provider event received. provider={Provider} eventId={EventId} type={Type} "
            + "providerIntentId={ProviderIntentId}",
            providerKey, parsed.ProviderEventId, parsed.Type, parsed.ProviderIntentId);

        var outcome = await settlement.SettleAsync(
            new SettlePaymentCommand(providerKey, parsed), cancellationToken);

        return new WebhookOutcome(
            IsNew: true,
            Processed: outcome.Kind is not SettlementOutcomeKind.Ignored,
            InboxEventId: inbox.Id,
            Detail: outcome.Kind.ToString());
    }

    /// <summary>
    /// The instant the event was received. Exposed so the endpoint and the inbox row share one clock
    /// reading rather than each taking their own (<c>TimeProvider</c> determinism, plan §7.3).
    /// </summary>
    internal DateTimeOffset Now() => clock.GetUtcNow();
}
