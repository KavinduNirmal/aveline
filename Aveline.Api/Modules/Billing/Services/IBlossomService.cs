using Aveline.Api.Modules.Billing.Models;

namespace Aveline.Api.Modules.Billing.Services;

public sealed record CreditBlossomsCommand(
    Guid OrganizationId,
    decimal Amount,
    string Reason,
    DateTime? ExpiresAt,
    BlossomSourceKind SourceKind,
    string? SourceRef,
    Guid? ActorUserId,
    string? IdempotencyKey,
    string? IdempotencyScope,
    BlossomLedgerEntryType EntryType = BlossomLedgerEntryType.AdminCredit);

public sealed record DebitBlossomsCommand(
    Guid OrganizationId,
    decimal Amount,
    string Reason,
    bool AllowNegative,
    Guid? ActorUserId,
    string? IdempotencyKey,
    string? IdempotencyScope);

public sealed record RevokeBlossomsCommand(
    Guid OrganizationId,
    Guid LedgerEntryId,
    string Reason,
    Guid? ActorUserId,
    string? IdempotencyKey,
    string? IdempotencyScope);

public sealed record ApplyPlanChangeCommand(
    Guid OrganizationId,
    decimal BlossomDelta,
    BlossomLedgerEntryType EntryType,
    string Reason,
    Guid? ActorUserId,
    string? IdempotencyKey = null,
    string? IdempotencyScope = null);

/// <summary>The authoritative balance projection for one period.</summary>
public sealed record BlossomBalance(
    Guid OrganizationId,
    DateTime PeriodStart,
    DateTime PeriodEnd,
    bool PeriodIsClosed,
    PlanTier? PlanTier,
    decimal MonthlyBlossomLimit,
    decimal BlossomGranted,
    decimal BlossomAdjusted,
    decimal BlossomUsed,
    decimal BlossomRemaining,
    DateTime AsOf);

/// <summary>One line of the statement of account.</summary>
public sealed record BlossomStatementItem(
    Guid Id,
    DateTime OccurredAt,
    string Kind,
    BlossomLedgerEntryType? EntryType,
    decimal BlossomDelta,
    decimal BalanceAfter,
    string Reason,
    BlossomSourceKind? SourceKind,
    string? SourceRef,
    DateTime? ExpiresAt,
    Guid? CreatedByUserId)
{
    /// <summary>
    /// How much of this grant is still revocable, or <c>null</c> when the row is not a revocable
    /// grant at all.
    /// </summary>
    /// <remarks>
    /// Revenue Ledger R4. The delivered page required an operator to hand-type a ledger-entry GUID
    /// and discovered non-revocability only by submitting and receiving `409 grant-not-revocable`
    /// with `availableToRevoke` — a fact the server knew at read time. Reporting it here lets the
    /// redesign offer an action instead of a failure. The 409 remains the authoritative answer.
    /// </remarks>
    public decimal? AvailableToRevoke { get; init; }

    /// <summary>The provider that produced a consumption row. <c>null</c> for an entitlement row.</summary>
    public string? Provider { get; init; }

    /// <summary>The model a consumption row was billed against. <c>null</c> for an entitlement row.</summary>
    public string? Model { get; init; }

    /// <summary>
    /// Raw normalized units behind a consumption row, so a Blossom charge can be explained rather
    /// than taken on trust. <c>null</c> for an entitlement row.
    /// </summary>
    public long? NormalizedUnits { get; init; }

    /// <summary>The provider cost behind a consumption row. <c>null</c> for an entitlement row.</summary>
    public decimal? ActualCostUsd { get; init; }
}

/// <summary>
/// The statement's data-quality block (S-3).
/// </summary>
/// <remarks>
/// Two flags exist because two things a reader would otherwise assume are not true:
///
/// - <see cref="OpeningBalanceFromProjection"/> is always `true` today. The opening balance is
///   derived **backwards** from the cached `BlossomRemaining` projection rather than accumulated
///   forward from the ledger, so it inherits that row's state. Stating it is the honest alternative
///   to implying the ledger produced it.
/// - <see cref="ReconciliationChecked"/> is `false` when the reconciliation could not be evaluated.
///   A `false` here renders *"reconciliation status unknown"*, and must never be rendered as
///   "consistent".
/// </remarks>
public sealed record BlossomStatementDataQuality(
    bool ReconciliationChecked,
    bool OpeningBalanceFromProjection,
    bool WindowCapped,
    int MaxWindowDays,
    IReadOnlyList<string> Notes);

public sealed record BlossomStatementReconciliation(
    decimal ProjectedBalance, decimal LedgerDerivedBalance, decimal Drift, bool IsConsistent);

public sealed record BlossomStatement(
    Guid OrganizationId,
    DateTime PeriodStart,
    DateTime PeriodEnd,
    decimal OpeningBalance,
    IReadOnlyList<BlossomStatementItem> Items,
    int Total,
    int Page,
    int PageSize,
    decimal ClosingBalance,
    BlossomStatementReconciliation Reconciliation,
    DateTime GeneratedAt,
    /// <summary>The effective window cap, so a client can state it rather than guess (S-3).</summary>
    int MaxWindowDays = 400,
    /// <summary>Present on every response; states what the numbers above do and do not rest on.</summary>
    BlossomStatementDataQuality? DataQuality = null);

public sealed record BlossomUsagePoint(string Key, decimal Blossoms, long NormalizedUnits, int WorkflowCount);

public sealed record BlossomUsage(
    DateTime From,
    DateTime To,
    decimal TotalBlossoms,
    long TotalNormalizedUnits,
    IReadOnlyList<BlossomUsagePoint> Series,
    DateTime GeneratedAt);

/// <summary>Administrative and self-service Blossom account operations (FR-2.2..FR-2.9).</summary>
public interface IBlossomService
{
    Task<BlossomLedgerEntry> CreditAsync(
        CreditBlossomsCommand command, CancellationToken cancellationToken = default);

    Task<BlossomLedgerEntry> DebitAsync(
        DebitBlossomsCommand command, CancellationToken cancellationToken = default);

    Task<BlossomLedgerEntry> RevokeAsync(
        RevokeBlossomsCommand command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes the mid-period allowance delta for a plan change without changing the
    /// period's base allocation (BR-2.10). Returns <c>null</c> when the delta is zero.
    /// </summary>
    Task<BlossomLedgerEntry?> ApplyPlanChangeAsync(
        ApplyPlanChangeCommand command, CancellationToken cancellationToken = default);

    Task<BlossomBalance> GetBalanceAsync(
        Guid organizationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Merged statement of entitlement entries and consumption (FR-2.9), paged and filtered
    /// **server-side**.
    /// </summary>
    /// <param name="kind"><c>Entitlement</c>, <c>Consumption</c>, <c>all</c> or null.</param>
    /// <param name="entryType">Restricts entitlement rows to one ledger entry type.</param>
    /// <param name="sourceKind">Restricts entitlement rows to one source (Revenue Ledger R4).</param>
    /// <param name="query">Substring match over the reason or the source reference.</param>
    /// <param name="minAmount">Inclusive lower bound on the absolute Blossom movement.</param>
    /// <param name="maxAmount">Inclusive upper bound on the absolute Blossom movement.</param>
    /// <remarks>
    /// Paging happens across the merged source rather than in memory: the delivered version read the
    /// whole window with `pageSize = int.MaxValue` and then skipped in memory, so a busy account
    /// materialised every row per request.
    /// </remarks>
    Task<BlossomStatement> GetStatementAsync(
        Guid organizationId,
        DateTime from,
        DateTime to,
        string? kind,
        BlossomLedgerEntryType? entryType,
        BlossomSourceKind? sourceKind,
        string? query,
        decimal? minAmount,
        decimal? maxAmount,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>Consumption series grouped by day, provider, model or workflow (FR-2.1).</summary>
    Task<BlossomUsage> GetUsageAsync(
        Guid organizationId,
        DateTime from,
        DateTime to,
        string groupBy,
        CancellationToken cancellationToken = default);
}
