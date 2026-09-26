using Aveline.Api.Modules.Analytics.DTOs;

namespace Aveline.Api.Modules.Analytics.Services;

/// <summary>
/// The six business-KPI reads (S-44…S-49). Every method returns both the data and the
/// <see cref="BusinessDataQualityDto"/> that names what is approximate, sampled, backfilled or
/// unavailable, so a silent <c>0</c> can never stand in for an unmeasurable measure.
/// </summary>
public interface IBusinessKpiService
{
    /// <summary>S-44 · new users, new organizations and administrator access requests per bucket.</summary>
    Task<BusinessGrowthDto> GetGrowthAsync(
        BusinessWindow window,
        string? cacheKey,
        CancellationToken cancellationToken = default);

    /// <summary>S-45 · active users per bucket plus the current DAU/WAU/MAU reading.</summary>
    Task<BusinessActiveUsersDto> GetActiveUsersAsync(
        BusinessWindow window,
        string? cacheKey,
        CancellationToken cancellationToken = default);

    /// <summary>S-46 · the current distribution of organizations, subscriptions and users across tiers.</summary>
    Task<BusinessPlanMixDto> GetPlanMixAsync(
        string? cacheKey,
        CancellationToken cancellationToken = default);

    /// <summary>S-47 · active subscriptions over time, split by tier, with starts and cancellations.</summary>
    Task<BusinessSubscriptionTrendDto> GetSubscriptionTrendAsync(
        BusinessWindow window,
        string? cacheKey,
        CancellationToken cancellationToken = default);

    /// <summary>S-48 · messages, agent runs, API calls, Blossom units and actual AI cost per bucket.</summary>
    Task<BusinessUsageDto> GetUsageAsync(
        BusinessWindow window,
        Guid? organizationId,
        string? cacheKey,
        CancellationToken cancellationToken = default);

    /// <summary>S-49 · organizations ranked by a chosen usage measure, with last-activity recency.</summary>
    Task<BusinessOrganizationUsageDto> GetOrganizationUsageAsync(
        BusinessWindow window,
        BusinessRankingMetric metric,
        int limit,
        string? cacheKey,
        CancellationToken cancellationToken = default);
}
