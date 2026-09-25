using Aveline.Api.Modules.Billing.Models;

namespace Aveline.Api.Modules.Payments.DTOs;

/// <summary>
/// The Blossom top-up checkout request (plan §6.2). The only client input is the SKU: the price and
/// the Blossom quantity are resolved server-side from the price book, so a caller cannot name the
/// amount it is charged.
/// </summary>
/// <remarks>
/// Deliberately carries no card number, CVC, expiry, PAN or token field (constraint C11). There is
/// no field a client could use to send card data to Aveline, which is what makes the omission a
/// testable invariant rather than an intention (<c>PaymentDtosTests</c>, M19).
/// </remarks>
public sealed record CreateTopUpCheckoutRequest(string SkuCode);

/// <summary>
/// The subscription checkout request (plan §6.2). No route maps it in Phase 3 — the plan's Q1
/// decision defers "require settlement" onboarding to a later slice — but the wire shape is part of
/// the abstraction's contract and is defined here so the interface does not change when it lands.
/// </summary>
public sealed record CreateSubscriptionCheckoutRequest(PlanTier PlanTier, string? BillingCycle);

/// <summary>The handoff a checkout returns: where to send the customer, and how to poll for truth.</summary>
public sealed record TopUpCheckoutResponse(
    Guid PaymentIntentId,
    string Provider,
    string Status,
    string SkuCode,
    decimal BlossomQuantity,
    decimal AmountLkr,
    string Currency,
    string? CheckoutUrl,
    DateTime? ExpiresAt);

/// <summary>
/// The client's poll of an intent. The redirect back from a hosted page is not proof of settlement,
/// so the client reads the terminal state from the server; this shape is that read.
/// </summary>
public sealed record PaymentIntentResponse(
    Guid PaymentIntentId,
    string Provider,
    string ProviderIntentId,
    string Purpose,
    string Status,
    decimal AmountLkr,
    string Currency,
    string? CheckoutUrl,
    string? FailureCode,
    string? FailureMessage,
    DateTime CreatedAt,
    DateTime? SettledAt,
    DateTime? ExpiresAt);

/// <summary>
/// The refund request (plan §9.6). No card, CVC, expiry, PAN or token field exists here either
/// (constraint C11): a refund names an amount and a reason, never an instrument.
/// </summary>
/// <param name="AmountLkr">
/// The amount to return, or null for the whole settled charge. A partial refund beyond the settled
/// amount is refused.
/// </param>
public sealed record RefundPaymentIntentRequest(decimal? AmountLkr, string Reason);

/// <summary>
/// The refund outcome (plan §9.6): the provider's refund id and the ledger entry the receipt was
/// written to, so the caller can reconcile both sides of the movement.
/// </summary>
public sealed record PaymentRefundResponse(
    Guid PaymentIntentId,
    string Provider,
    string Status,
    string ProviderRefundId,
    Guid LedgerEntryId,
    decimal AmountLkr,
    string Currency,
    DateTime? RefundedAt);

/// <summary>
/// The top-up catalogue entry of plan §6.2 (<c>{skuCode, blossomQuantity, priceLkr, currency}</c>).
/// </summary>
/// <remarks>
/// The shipped catalogue route (<c>GET .../blossoms/top-up-packs</c>, E-11) serialises the identical
/// shape through <c>Modules.Billing.DTOs.TopUpPackDto</c>; that type is left in place so the shipped
/// contract and its tests do not move under a caller's feet (decision D7's reasoning).
/// </remarks>
public sealed record TopUpPackView(string SkuCode, decimal BlossomQuantity, decimal PriceLkr, string Currency);
