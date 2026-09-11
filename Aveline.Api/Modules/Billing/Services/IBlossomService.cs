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
    string? IdempotencyScope);

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

/// <summary>Administrative and self-service Blossom account operations (FR-2.2..FR-2.9).</summary>
public interface IBlossomService
{
    Task<BlossomLedgerEntry> CreditAsync(
        CreditBlossomsCommand command, CancellationToken cancellationToken = default);

    Task<BlossomLedgerEntry> DebitAsync(
        DebitBlossomsCommand command, CancellationToken cancellationToken = default);

    Task<BlossomLedgerEntry> RevokeAsync(
        RevokeBlossomsCommand command, CancellationToken cancellationToken = default);

    Task<BlossomBalance> GetBalanceAsync(
        Guid organizationId, CancellationToken cancellationToken = default);
}
