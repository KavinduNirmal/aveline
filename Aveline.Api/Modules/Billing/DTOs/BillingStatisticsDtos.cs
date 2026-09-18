using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Statistics.DTOs;

namespace Aveline.Api.Modules.Billing.DTOs;

/// <summary>S-4 · Burn rate and projected balance exhaustion.</summary>
public sealed record BurnRateResponseDto(
    string Window,
    decimal BurnRatePerDay,
    DateTime? ProjectedExhaustionAt,
    decimal CurrentBalance,
    decimal AverageDailyUsage,
    AgentDataQualityDto DataQuality);

/// <summary>S-11 · Active customer count based on the 90-day activity window.</summary>
public sealed record ActiveCustomersDto(
    Guid OrganizationId,
    int ActiveCount,
    int TotalCount,
    DateTime AsOf,
    int InactivityThresholdDays = 90);

/// <summary>S-12 · Staff seat utilization and role distribution.</summary>
public sealed record StaffSeatsDto(
    Guid OrganizationId,
    int ActiveSeats,
    int AllowedSeats,
    IReadOnlyDictionary<string, int> RoleBreakdown,
    DateTime AsOf);

/// <summary>S-5 · Unit economics and profitability breakdown.</summary>
public sealed record ProfitabilityItemDto(
    string Dimension,
    int RequestCount,
    decimal BlossomRevenue,
    decimal ActualCostUsd,
    decimal? MarginLkr);

public sealed record BillingProfitabilityDto(
    string GroupBy,
    IReadOnlyList<ProfitabilityItemDto> Series,
    decimal TotalBlossomRevenue,
    decimal TotalCostUsd,
    decimal? TotalMarginLkr,
    AgentDataQualityDto DataQuality);

/// <summary>S-6 · Top consuming organizations ranked by Blossom units.</summary>
public sealed record OrgUsageRankItemDto(
    int Rank,
    Guid OrganizationId,
    string OrganizationName,
    PlanTier PlanTier,
    decimal BlossomUnits,
    int RequestCount);

public sealed record OrgUsageRankingPageDto(
    IReadOnlyList<OrgUsageRankItemDto> Items,
    int Page,
    int PageSize,
    int TotalCount);

/// <summary>S-7 · Administrative Blossom adjustments and manual grants.</summary>
public sealed record AdjustmentItemDto(
    Guid OrganizationId,
    string EntryType,
    decimal BlossomDelta,
    string? ActorUserId,
    string Reason,
    DateTime CreatedAt);

public sealed record BillingAdjustmentActivityDto(
    IReadOnlyList<AdjustmentItemDto> Items,
    decimal TotalCredits,
    decimal TotalDebits,
    IReadOnlyDictionary<string, decimal> ByActor);

/// <summary>S-8 · Plan change events with proration adjustments.</summary>
public sealed record PlanChangeItemDto(
    Guid OrganizationId,
    string FromTier,
    string ToTier,
    decimal ProrationDelta,
    DateTime ChangedAt);

public sealed record PlanChangeHistoryDto(
    IReadOnlyList<PlanChangeItemDto> Items,
    int UpgradeCount,
    int DowngradeCount,
    decimal TotalProrationDelta);

/// <summary>S-9 · Blocked downgrade attempts due to entitlement limits.</summary>
public sealed record DowngradeStatisticsDto(
    int Attempted,
    int Blocked,
    double BlockedRate,
    IReadOnlyDictionary<string, int> ByViolatedKey);
