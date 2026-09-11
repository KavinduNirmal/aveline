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
    Guid? CreatedByUserId);

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
    DateTime GeneratedAt);

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

    /// <summary>Merged statement of entitlement entries and consumption (FR-2.9).</summary>
    Task<BlossomStatement> GetStatementAsync(
        Guid organizationId,
        DateTime from,
        DateTime to,
        string? kind,
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
