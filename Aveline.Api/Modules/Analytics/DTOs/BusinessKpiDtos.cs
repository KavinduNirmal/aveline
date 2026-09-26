namespace Aveline.Api.Modules.Analytics.DTOs;

/// <summary>
/// A business-KPI response that carries a <see cref="BusinessDataQualityDto"/>. Every series
/// endpoint does, which is what lets the endpoint layer attach cache-backed notes generically.
/// </summary>
public interface IBusinessQualityCarrier
{
    BusinessDataQualityDto DataQuality { get; }
}

/// <summary>The window a series was computed over, echoed so the client never re-derives it.</summary>
public sealed record BusinessWindowDto(
    DateTime From,
    DateTime To,
    string Granularity,
    string TimeZone,
    int BucketCount);

/// <summary>
/// What is wrong with this answer. Mirrors <c>AgentDataQualityDto</c> and the system
/// <c>omitted[]</c> vocabulary: a false flag is named, never silently absorbed.
/// </summary>
public sealed record BusinessDataQualityDto(
    bool UserAttributionAvailable,
    long UnresolvedAttributionCount,
    bool SubscriptionHistoryBackfilled,
    bool LastActivityIsReconstructed,
    bool AgentMetricsUninstrumented,
    IReadOnlyList<string> Notes)
{
    /// <summary>The flags that hold when every source answered and every measure is instrumented.</summary>
    public static BusinessDataQualityDto Clean { get; } = new(
        UserAttributionAvailable: true,
        UnresolvedAttributionCount: 0,
        SubscriptionHistoryBackfilled: false,
        LastActivityIsReconstructed: false,
        AgentMetricsUninstrumented: false,
        Notes: []);
}

public sealed record GrowthPointDto(
    DateTime BucketStart,
    bool IsPartial,
    int NewUsers,
    int NewOrganizations,
    int NewAdminRequests,
    int ApprovedAdminRequests);

public sealed record GrowthTotalsDto(
    int NewUsers,
    int NewOrganizations,
    int NewAdminRequests,
    int ApprovedAdminRequests)
{
    public static GrowthTotalsDto Empty { get; } = new(0, 0, 0, 0);
}

public sealed record BusinessGrowthDto(
    BusinessWindowDto Window,
    DateTime? ObservedFrom,
    IReadOnlyList<GrowthPointDto> Series,
    GrowthTotalsDto Totals,
    GrowthTotalsDto PreviousTotals,
    BusinessDataQualityDto DataQuality) : IBusinessQualityCarrier;

public sealed record ActiveUsersPointDto(
    DateTime BucketStart,
    bool IsPartial,
    int? ActiveUsers,
    int? ActiveOrganizations);

public sealed record RollingActiveUsersDto(int? Dau, int? Wau, int? Mau, double? Stickiness);

public sealed record BusinessActiveUsersDto(
    BusinessWindowDto Window,
    IReadOnlyList<ActiveUsersPointDto> Series,
    RollingActiveUsersDto Rolling,
    BusinessDataQualityDto DataQuality) : IBusinessQualityCarrier;

public sealed record PlanMixItemDto(
    string PlanTier,
    bool IsFree,
    int OrganizationCount,
    int ActiveOrganizationCount,
    int BilledSubscriptionCount,
    int UserCount,
    decimal MonthlyPriceLkr);

public sealed record PlanMixSideDto(
    int OrganizationCount,
    int UserCount,
    decimal MonthlyPriceLkr,
    double ShareOfOrganizations);

public sealed record BusinessPlanMixDto(
    DateTime AsOf,
    IReadOnlyList<PlanMixItemDto> Tiers,
    PlanMixSideDto Free,
    PlanMixSideDto Premium,
    int OrganizationsTotal,
    int OrganizationsWithBillingRow,
    decimal TotalMonthlyPriceLkr,
    BusinessDataQualityDto DataQuality) : IBusinessQualityCarrier;

public sealed record SubscriptionTrendPointDto(
    DateTime BucketStart,
    bool IsPartial,
    int ActiveTotal,
    IReadOnlyDictionary<string, int> ActiveByTier,
    int Started,
    int Cancelled,
    bool IsBackfilled);

public sealed record BusinessSubscriptionTrendDto(
    BusinessWindowDto Window,
    IReadOnlyList<SubscriptionTrendPointDto> Series,
    int OpeningActive,
    int ClosingActive,
    decimal ChurnRate,
    BusinessDataQualityDto DataQuality) : IBusinessQualityCarrier;

public sealed record UsageTrendPointDto(
    DateTime BucketStart,
    bool IsPartial,
    long MessagesSent,
    int AgentRuns,
    long ApiRequests,
    decimal BlossomUnits,
    decimal ActualCostUsd);

public sealed record UsageTotalsDto(
    long MessagesSent,
    int AgentRuns,
    long ApiRequests,
    decimal BlossomUnits,
    decimal ActualCostUsd)
{
    public static UsageTotalsDto Empty { get; } = new(0, 0, 0, 0m, 0m);
}

public sealed record BusinessUsageDto(
    BusinessWindowDto Window,
    Guid? OrganizationId,
    IReadOnlyList<UsageTrendPointDto> Series,
    UsageTotalsDto Totals,
    BusinessDataQualityDto DataQuality) : IBusinessQualityCarrier;

public sealed record OrganizationUsageItemDto(
    int Rank,
    Guid OrganizationId,
    string Name,
    string PlanTier,
    long MessagesSent,
    int AgentRuns,
    long ApiRequests,
    decimal BlossomUnits,
    DateTime? LastActivityAt,
    int? DaysSinceLastActivity);

public sealed record BusinessOrganizationUsageDto(
    string Metric,
    DateTime From,
    DateTime To,
    IReadOnlyList<OrganizationUsageItemDto> Items,
    int TotalCount,
    BusinessDataQualityDto DataQuality) : IBusinessQualityCarrier;
