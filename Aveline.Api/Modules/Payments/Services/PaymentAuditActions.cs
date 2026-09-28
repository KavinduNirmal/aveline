namespace Aveline.Api.Modules.Payments.Services;

/// <summary>
/// The audit action names of plan §12.4, in the shape the rest of the repository uses
/// (<c>revenue.ledger.verified</c>, <c>blossom.ledger.topupgrant</c>).
/// </summary>
/// <remarks>
/// Kept beside the payment services rather than appended to <c>Modules.Audit.Models.AuditAction</c>
/// so this module's audit vocabulary lands in one diff; the values are the contract, and
/// <c>AuditAction</c>'s own comment says new actions are added by new modules.
/// </remarks>
internal static class PaymentAuditActions
{
    internal const string IntentCreated = "payment.intent.created";

    internal const string Settled = "payment.settled";

    internal const string Failed = "payment.failed";

    internal const string Refunded = "payment.refunded";

    /// <summary>A provider dispute reversed a settled charge (P10, plan §9.6).</summary>
    internal const string Disputed = "payment.disputed";

    internal const string IntentCancelled = "payment.intent.cancelled";
}
