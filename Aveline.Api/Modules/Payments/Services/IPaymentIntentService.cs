using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Payments.Domain;
using Aveline.Api.Modules.Payments.Models;

namespace Aveline.Api.Modules.Payments.Services;

/// <summary>
/// A request to create a provider-neutral payment intent (plan §6.4 step 3, §6.2's
/// <c>IPaymentIntentService.CreateAsync</c>).
/// </summary>
/// <param name="OrganizationId">The tenant the charge belongs to.</param>
/// <param name="Purpose">Drives the settlement effect, never the provider call.</param>
/// <param name="Amount">The resolved list price, in the configured currency.</param>
/// <param name="Description">Sent to the provider; also the grant/receipt reason stem.</param>
/// <param name="SkuCode">Set for a top-up; the pack's identity.</param>
/// <param name="BlossomQuantity">Set for a top-up; exactly what settlement will grant.</param>
/// <param name="IdempotencyKey">The client's <c>Idempotency-Key</c>; unique per organisation and purpose.</param>
/// <param name="CreatedByUserId">The initiator, or null for a system-created intent.</param>
/// <param name="BillingPeriodStart">
/// The billing period the grant belongs to, resolved **before** payment. Null when cross-period
/// top-ups are allowed (deliverable 6, BR-2.6).
/// </param>
/// <param name="BillingPeriodEnd">
/// The resolved period cap, persisted on the intent so a settlement arriving after a period boundary
/// cannot silently extend or truncate the grant. Null means "no cap" and is what the intent stores
/// when <c>Billing:AllowCrossPeriodTopUps</c> allowed an uncapped purchase.
/// </param>
/// <param name="PlanTier">Set for subscription purposes.</param>
/// <param name="BillingCycle">Set for subscription purposes.</param>
/// <param name="ExpiresAt">The requested expiry, if the caller has one.</param>
public sealed record CreatePaymentIntentCommand(
    Guid OrganizationId,
    PaymentPurpose Purpose,
    Money Amount,
    string Description,
    string? SkuCode,
    decimal? BlossomQuantity,
    string? IdempotencyKey,
    Guid? CreatedByUserId,
    DateTime? BillingPeriodStart = null,
    DateTime? BillingPeriodEnd = null,
    PlanTier? PlanTier = null,
    string? BillingCycle = null,
    DateTime? ExpiresAt = null);

/// <summary>
/// The application-layer view of an intent. The wire shape is
/// <c>Modules.Payments.DTOs.PaymentIntentResponse</c>; this type exists so the service does not
/// depend on the DTOs, and it carries the organisation id the DTO deliberately omits.
/// </summary>
public sealed record PaymentIntentView(
    Guid PaymentIntentId,
    Guid OrganizationId,
    string Provider,
    string? ProviderIntentId,
    PaymentPurpose Purpose,
    string Status,
    decimal AmountLkr,
    string Currency,
    string? CheckoutUrl,
    string? FailureCode,
    string? FailureMessage,
    DateTime CreatedAt,
    DateTime? SettledAt,
    DateTime? RefundedAt,
    DateTime? ExpiresAt,
    string? SkuCode,
    decimal? BlossomQuantity)
{
    /// <summary>The intent's status, with the derivable <c>Expired</c> and <c>Refunded</c> states applied.</summary>
    /// <remarks>
    /// Plan §6.6: a sweep is deliberately not part of this phase, and the state is derivable, so the
    /// read reports <c>Expired</c> when an unsettled intent is past its expiry rather than leaving
    /// the client to infer it from a timestamp. <c>Refunded</c> is derived the same way, because
    /// <see cref="PaymentProviderStatus"/> describes the *provider's* charge state and has no
    /// Refunded member; adding one would either need a migration or misreport the provider's view.
    /// </remarks>
    public static string DeriveStatus(
        string status, PaymentProviderStatus parsed, DateTime? expiresAt, DateTime? refundedAt, DateTime now)
    {
        if (refundedAt is not null)
        {
            return "Refunded";
        }

        return parsed is PaymentProviderStatus.RequiresAction or PaymentProviderStatus.Processing
            && expiresAt is { } expiry
            && expiry <= now
                ? nameof(PaymentProviderStatus.Expired)
                : status;
    }

    /// <summary>
    /// Projects a persisted intent. <paramref name="checkoutUrl"/> is passed in rather than stored
    /// because the intent row has no such column: the URL is the provider's, and the adapter is the
    /// one place that knows it (the same reasoning as D5's resolve-by-stored-key).
    /// </summary>
    public static PaymentIntentView From(
        PaymentIntent intent, string? checkoutUrl, DateTime now) =>
        new(
            intent.Id,
            intent.OrganizationId,
            intent.Provider,
            intent.ProviderIntentId,
            intent.Purpose,
            DeriveStatus(intent.Status.ToString(), intent.Status, intent.ExpiresAt, intent.RefundedAt, now),
            intent.AmountMinor / 100m,
            intent.Currency,
            checkoutUrl,
            intent.FailureCode,
            intent.FailureMessage,
            intent.CreatedAt,
            intent.SettledAt,
            intent.RefundedAt,
            intent.ExpiresAt,
            intent.SkuCode,
            intent.BlossomQuantity);
}

/// <summary>
/// The outcome of a refund (plan §9.6). The provider's refund id and the ledger entry the receipt
/// was written to are reported because the response must name both: the provider reference is what
/// the reconciliation matches against, and the ledger id is what an operator looks up.
/// </summary>
public sealed record PaymentRefundResult(
    PaymentIntentView Intent,
    string ProviderRefundId,
    Guid LedgerEntryId,
    decimal AmountLkr);

/// <summary>
/// The application rules for a payment intent: creation, reads, cancellation and refunds
/// (plan §6.2). It owns the business rules; the provider owns the charge.
/// </summary>
public interface IPaymentIntentService
{
    /// <summary>
    /// Creates the intent row and the provider charge it points at. The provider call carries
    /// <c>IdempotencyKey = intentId</c>, and a provider transport failure leaves **no** intent row
    /// committed (plan §6.6).
    /// </summary>
    /// <exception cref="PaymentProviderTransportException">The provider could not be reached.</exception>
    /// <exception cref="PaymentIdempotencyKeyReuseException">The client key already names a different request.</exception>
    Task<PaymentIntentView> CreateAsync(
        CreatePaymentIntentCommand command, CancellationToken cancellationToken = default);

    /// <summary>The intent, scoped to its organisation. An unknown id is a 404 for that tenant.</summary>
    /// <exception cref="PaymentIntentNotFoundException">No such intent for this organisation.</exception>
    Task<PaymentIntentView> GetAsync(
        Guid organizationId, Guid intentId, CancellationToken cancellationToken = default);

    /// <summary>Voids an unsettled intent.</summary>
    /// <exception cref="PaymentIntentStateException">The intent is not in a cancellable state.</exception>
    Task<PaymentIntentView> CancelAsync(
        Guid organizationId, Guid intentId, string reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refunds a settled charge, wholly or partly, by asking the provider **first** and writing the
    /// ledger only after it accepts (decision D8). The provider is never asked for a charge the
    /// journal did not record collecting: a refund requires a live <c>Verified</c> receipt for the
    /// same <c>(SourceKind, SourceRef)</c>, exactly as the admin route requires (plan §9.6).
    /// </summary>
    /// <exception cref="PaymentIntentStateException">
    /// The intent is not settled, is already refunded, or the refund falls outside a configured
    /// <c>Payments:RefundWindowDays</c>.
    /// </exception>
    /// <exception cref="Modules.Revenue.Models.RevenueRefundNotAllowedException">
    /// No verified receipt exists for the charge, so there is nothing to return.
    /// </exception>
    /// <exception cref="PaymentProviderTransportException">The provider refused or was unreachable.</exception>
    Task<PaymentRefundResult> RequestRefundAsync(
        Guid organizationId, Guid intentId, decimal? amountLkr, string reason,
        CancellationToken cancellationToken = default);
}
