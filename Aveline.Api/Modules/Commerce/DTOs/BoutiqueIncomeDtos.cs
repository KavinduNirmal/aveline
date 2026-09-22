using Aveline.Api.Modules.Commerce.Models;

namespace Aveline.Api.Modules.Commerce.DTOs;

/// <summary>The window a boutique income read was computed over, echoed so the client never re-derives it.</summary>
public sealed record BoutiqueIncomeWindowDto(
    DateTime From,
    DateTime To,
    DateTime GeneratedAt);

/// <summary>
/// The tenant income surface's own data-quality vocabulary — the sixth in the API, and named field
/// by field rather than reused from the agent, home-feed or revenue families.
/// </summary>
/// <remarks>
/// A field is named only where it means something specific to **this** surface. The two that matter
/// most:
///
/// - <see cref="PaymentRowsPresent"/> is `false` when the shop has no `Payments` rows at all, which
///   is the single most likely reason a real shop shows zero cash.
/// - <see cref="IncomeLedgerBackfilled"/> is `true` while the register contains only rows the
///   reconciliation job repaired, so a reader knows the ledger begins at a date rather than
///   claiming full history.
/// </remarks>
public sealed record BoutiqueIncomeDataQualityDto(
    bool PaymentRowsPresent,
    bool OrderCostsComplete,
    bool IncomeLedgerBackfilled,
    bool RefundAndOutstandingExcludedFromCollected,
    DateTime CheckedAt,
    IReadOnlyList<string> Notes)
{
    /// <summary>Every source answered and every measure is instrumented.</summary>
    public static BoutiqueIncomeDataQualityDto Clean(DateTime checkedAt) => new(
        PaymentRowsPresent: true,
        OrderCostsComplete: true,
        IncomeLedgerBackfilled: false,
        // Stated rather than implied: the collected figure is money taken, and it is never netted
        // against a refund or an outstanding request without a label saying so.
        RefundAndOutstandingExcludedFromCollected: true,
        CheckedAt: checkedAt,
        Notes: []);

    /// <summary>Copies this block with extra notes appended.</summary>
    public BoutiqueIncomeDataQualityDto WithNotes(IEnumerable<string> notes) =>
        this with { Notes = [.. Notes, .. notes] };
}

/// <summary>
/// Window totals computed over the **whole window**, not the page, so the figures describe the
/// period the caller asked about rather than the slice on screen. Each kind is stated separately.
/// </summary>
public sealed record BoutiqueIncomeTotalsDto(
    decimal SaleTotal,
    decimal PaymentTotal,
    decimal RefundTotal,
    decimal AdjustmentTotal);

/// <summary>
/// The two-basis reconciliation.
/// </summary>
/// <remarks>
/// **`Derived` and `Verified` are never summed into one unlabelled figure.** A derived total is
/// billed value with no evidence of collection; a verified total is money taken.
/// <see cref="UnverifiedGap"/> is the distance between them, and it is the most useful number on
/// the surface rather than an error to be netted away.
/// </remarks>
public sealed record BoutiqueIncomeReconciliationDto(
    decimal VerifiedTotal,
    decimal DerivedTotal,
    decimal RefundTotal,
    decimal NetVerified,
    decimal UnverifiedGap,
    bool IsReconciled);

/// <summary>One entry in the register, as the client reads it.</summary>
public sealed record BoutiqueIncomeEntryDto(
    Guid Id,
    string Kind,
    string SourceKind,
    string? SourceRef,
    string ChargeBasis,
    string Status,
    string Currency,
    decimal Amount,
    string Reason,
    DateTime OccurredAt,
    DateTime RecordedAt,
    Guid? OrderId,
    Guid? CustomerId,
    Guid? PaymentId,
    Guid? RecordedByUserId);

/// <summary>One page of the register, with the window's totals and the reconciliation block on it.</summary>
public sealed record BoutiqueIncomeLedgerPageDto(
    BoutiqueIncomeWindowDto Window,
    IReadOnlyList<BoutiqueIncomeEntryDto> Items,
    int Total,
    int Page,
    int PageSize,
    bool WindowCapped,
    BoutiqueIncomeTotalsDto Totals,
    BoutiqueIncomeReconciliationDto Reconciliation,
    string Currency,
    BoutiqueIncomeDataQualityDto DataQuality)
{
    /// <summary>Set when the request was refused, so the endpoint can map it to a 400.</summary>
    public string? InvalidReason { get; init; }

    public static BoutiqueIncomeLedgerPageDto Invalid(string reason) =>
        new(
            new BoutiqueIncomeWindowDto(default, default, DateTime.UtcNow),
            [], 0, 1, 0, false,
            new BoutiqueIncomeTotalsDto(0m, 0m, 0m, 0m),
            new BoutiqueIncomeReconciliationDto(0m, 0m, 0m, 0m, 0m, true),
            "LKR",
            BoutiqueIncomeDataQualityDto.Clean(DateTime.UtcNow))
        {
            InvalidReason = reason,
        };
}

/// <summary>One kind's contribution to the window.</summary>
public sealed record BoutiqueIncomeKindTotalDto(
    string Kind,
    decimal Total,
    int Count);

/// <summary>One payment method's contribution to the window's cash.</summary>
public sealed record BoutiqueIncomePaymentMethodTotalDto(
    string PaymentMethod,
    decimal Total,
    int Count);

/// <summary>
/// The window broken down by kind, and — for cash entries — by payment method.
/// </summary>
/// <remarks>
/// The payment-method split is read from the **payment rows**, not inferred: a ledger entry carries
/// no method of its own, and inventing one would be exactly the fabrication this surface avoids.
/// </remarks>
public sealed record BoutiqueIncomeAccountsDto(
    BoutiqueIncomeWindowDto Window,
    IReadOnlyList<BoutiqueIncomeKindTotalDto> Items,
    IReadOnlyList<BoutiqueIncomePaymentMethodTotalDto> ByPaymentMethod,
    BoutiqueIncomeReconciliationDto Reconciliation,
    string Currency,
    BoutiqueIncomeDataQualityDto DataQuality)
{
    /// <summary>Set when the request was refused, so the endpoint can map it to a 400.</summary>
    public string? InvalidReason { get; init; }

    public static BoutiqueIncomeAccountsDto Invalid(string reason) =>
        new(
            new BoutiqueIncomeWindowDto(default, default, DateTime.UtcNow),
            [], [],
            new BoutiqueIncomeReconciliationDto(0m, 0m, 0m, 0m, 0m, true),
            "LKR",
            BoutiqueIncomeDataQualityDto.Clean(DateTime.UtcNow))
        {
            InvalidReason = reason,
        };
}

/// <summary>The register query: a window, the filters, and the page.</summary>
public sealed record BoutiqueIncomeLedgerQuery(
    DateTime? From = null,
    DateTime? To = null,
    string? Kind = null,
    string? Basis = null,
    string? Query = null,
    int Page = 1,
    int PageSize = 50);

/// <summary>The per-kind breakdown query: a window only.</summary>
public sealed record BoutiqueIncomeAccountsQuery(
    DateTime? From = null,
    DateTime? To = null);
