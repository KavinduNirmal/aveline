using Aveline.Api.Modules.Revenue.Models;

namespace Aveline.Api.Modules.Revenue.Endpoints;

/// <summary>
/// The audit action names the revenue journal writes, alongside the existing
/// <c>blossom.ledger.*</c> family.
/// </summary>
/// <remarks>
/// One place decides the name, so the three administrative verbs and the system-written derived
/// charge cannot drift apart, and the Audit Explorer reads one vocabulary for both money journals.
///
/// A derived charge is deliberately **not** one of the three administrative verbs: it is written by
/// the period rollover with no human actor, and calling it "verified", "refunded" or "adjusted"
/// would attribute an operator action to a job.
/// </remarks>
public static class RevenueAuditActions
{
    public const string Verified = "revenue.ledger.verified";
    public const string Refunded = "revenue.ledger.refunded";
    public const string Adjusted = "revenue.ledger.adjusted";
    public const string Derived = "revenue.ledger.derived";

    /// <summary>The action name for an entry, derived from what it is rather than who wrote it.</summary>
    public static string For(IncomeLedgerEntry entry) => entry switch
    {
        { Kind: IncomeEntryKind.Refund } => Refunded,
        { Kind: IncomeEntryKind.Adjustment } => Adjusted,
        { ChargeBasis: IncomeChargeBasis.Verified } => Verified,
        _ => Derived,
    };
}
