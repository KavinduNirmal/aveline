using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Billing.Services;

namespace Aveline.Api.Modules.Billing.DTOs;

public sealed record BlossomLedgerEntryDto(
    Guid Id,
    string EntryType,
    decimal BlossomDelta,
    decimal BlossomBalanceAfter,
    string Reason,
    string? SourceKind,
    string? SourceRef,
    DateTime? ExpiresAt,
    Guid? CreatedByUserId,
    DateTime CreatedAt)
{
    public static BlossomLedgerEntryDto From(BlossomLedgerEntry entry) => new(
        entry.Id,
        entry.EntryType.ToString(),
        entry.BlossomDelta,
        entry.BlossomBalanceAfter,
        entry.Reason,
        entry.SourceKind?.ToString(),
        entry.SourceRef,
        entry.ExpiresAt,
        entry.CreatedByUserId,
        entry.CreatedAt);
}

public sealed record BlossomBalanceDto(
    Guid OrganizationId,
    DateTime PeriodStart,
    DateTime PeriodEnd,
    bool PeriodIsClosed,
    string? PlanTier,
    decimal MonthlyBlossomLimit,
    decimal BlossomGranted,
    decimal BlossomAdjusted,
    decimal BlossomUsed,
    decimal BlossomRemaining,
    decimal PercentUsed,
    decimal LowBalanceThresholdPercent,
    DateTime AsOf)
{
    public static BlossomBalanceDto From(BlossomBalance balance, decimal lowBalanceThresholdPercent)
    {
        var percentUsed = balance.MonthlyBlossomLimit <= 0
            ? 0m
            : Math.Round(balance.BlossomUsed / balance.MonthlyBlossomLimit * 100m, 2);

        return new BlossomBalanceDto(
            balance.OrganizationId,
            balance.PeriodStart,
            balance.PeriodEnd,
            balance.PeriodIsClosed,
            balance.PlanTier?.ToString(),
            balance.MonthlyBlossomLimit,
            balance.BlossomGranted,
            balance.BlossomAdjusted,
            balance.BlossomUsed,
            balance.BlossomRemaining,
            percentUsed,
            lowBalanceThresholdPercent,
            balance.AsOf);
    }
}

public sealed record CreditBlossomsRequest(
    decimal Amount,
    string Reason,
    DateTime? ExpiresAt,
    BlossomSourceKind? SourceKind,
    string? SourceRef);

public sealed record DebitBlossomsRequest(decimal Amount, string Reason, bool AllowNegative);

public sealed record RevokeBlossomsRequest(Guid LedgerEntryId, string Reason);

public sealed record TopUpRequest(string SkuCode, decimal? BlossomQuantity, string? PaymentReference);
