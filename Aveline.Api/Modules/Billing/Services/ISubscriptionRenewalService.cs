using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Payments.Models;

namespace Aveline.Api.Modules.Billing.Services;

/// <summary>How one renewal charge attempt ended.</summary>
public enum RenewalAttemptKind
{
    /// <summary>The provider settled the charge and the receipt was written.</summary>
    Settled,

    /// <summary>The charge did not settle; the subscription is (or stays) <c>PastDue</c>.</summary>
    Failed,

    /// <summary>The subscription is winding down, so no charge was attempted.</summary>
    Skipped,
}

/// <summary>The result of one renewal charge attempt.</summary>
public sealed record RenewalAttempt(RenewalAttemptKind Kind, Guid? PaymentIntentId, string? Detail);

/// <summary>
/// The subscription side of plan §9.4 F4: at the period rollover, create a
/// <c>PaymentPurpose.SubscriptionRenewal</c> charge through the payment module and
/// apply the result, then run the approved dunning schedule (decision Q3) for subscriptions that
/// did not settle.
/// </summary>
/// <remarks>
/// It depends on <c>IPaymentIntentService</c> and never on <c>IPaymentSettlementService</c>, so
/// the settlement service can depend on this one to apply an asynchronous renewal webhook without
/// a cycle.
/// </remarks>
public interface ISubscriptionRenewalService
{
    /// <summary>
    /// Renews every subscription whose period has ended, and advances the dunning window for every
    /// subscription already <c>PastDue</c>. Returns how many subscriptions it acted on.
    /// </summary>
    Task<int> RunAsync(DateTime now, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies the effect of a <c>Succeeded</c> <c>SubscriptionRenewal</c> intent: the
    /// <c>Verified</c> receipt that supersedes the <c>Derived</c> charge for the same period, and
    /// the period advance. Idempotent, so a replayed webhook cannot double-charge or skip a period.
    /// </summary>
    /// <returns><c>true</c> when this call wrote the receipt; <c>false</c> when it was a replay.</returns>
    Task<bool> ApplySettlementAsync(
        PaymentIntent intent, DateTime now, CancellationToken cancellationToken = default);
}
