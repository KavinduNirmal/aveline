namespace Aveline.Api.Modules.Commerce.DTOs;

/// <summary>
/// The body of a refund request. The optional <paramref name="Reason"/> is the operator's own words
/// and becomes the boutique ledger entry's reason, so the register can say why money went back
/// rather than only that it did.
/// </summary>
/// <remarks>
/// The body itself is optional: a refund with no note is a legitimate counter action, and the ledger
/// substitutes a stated fallback rather than refusing it. What is **not** optional is the actor —
/// the route is guarded by <c>payments:refund</c>, so the journal always names who issued it.
/// </remarks>
public sealed record RefundPaymentRequest(string? Reason = null);
