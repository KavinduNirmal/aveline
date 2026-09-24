using Aveline.Api.Modules.Payments.Domain;

namespace Aveline.Api.Modules.Payments.Services;

/// <summary>What applying one provider event did.</summary>
public enum SettlementOutcomeKind
{
    /// <summary>The grant and the receipt were written, and the event was marked processed.</summary>
    Settled,

    /// <summary>The intent was already settled; this delivery changed nothing.</summary>
    AlreadySettled,

    /// <summary>
    /// A settled charge was reversed by the provider, and the reversal was appended to the income
    /// ledger. Distinct from <see cref="Settled"/> because no grant or receipt was written: money the
    /// provider already collected moved the other way (a dispute/chargeback, P10).
    /// </summary>
    Reversed,

    /// <summary>A provider failure, or an event type this phase does not act on.</summary>
    Ignored,
}

/// <summary>The result of one settlement attempt.</summary>
public sealed record SettlementOutcome(
    SettlementOutcomeKind Kind, Guid? PaymentIntentId, string? Detail);

/// <summary>
/// One inbound provider event, ready to be applied. The inbox row was persisted by
/// <see cref="IPaymentProviderEventService"/> before this is called, which is what makes the
/// mismatch and state failures recordable against it.
/// </summary>
/// <param name="Provider">The adapter key the event arrived under.</param>
/// <param name="Event">The verified, provider-neutral event.</param>
/// <param name="ExpectedOrganizationId">
/// Set when the caller knows which tenant the settlement belongs to (a client-initiated confirm, or a
/// provider whose payload carries the organisation). Null for an anonymous webhook, where the intent
/// row is the only organisation identity available.
/// </param>
public sealed record SettlePaymentCommand(
    string Provider,
    PaymentWebhookEvent Event,
    Guid? ExpectedOrganizationId = null);

/// <summary>
/// Applies the effect of a settled intent exactly once (plan §6.2, §6.4 steps 8-10).
/// </summary>
/// <remarks>
/// The implementation is safe to call from a retried webhook: the intent state, the inbox row and the
/// ledgers' own idempotency tuples each make a second call a no-op.
/// </remarks>
public interface IPaymentSettlementService
{
    /// <summary>
    /// Applies one verified provider event.
    /// </summary>
    /// <exception cref="PaymentIntentMismatchException">
    /// The event's amount or currency does not match the intent; the event is stored unprocessed.
    /// </exception>
    /// <exception cref="PaymentIntentStateException">
    /// The intent is in a state that cannot settle the event (expired, cancelled, failed).
    /// </exception>
    /// <exception cref="PaymentIntentNotFoundException">
    /// No intent for this organisation matches the event.
    /// </exception>
    Task<SettlementOutcome> SettleAsync(
        SettlePaymentCommand command, CancellationToken cancellationToken = default);
}
