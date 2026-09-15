using Aveline.Api.Modules.Billing.DTOs;

namespace Aveline.Api.Modules.Billing.Services;

public interface IBillingStatisticsService
{
    Task<BurnRateResponseDto> GetBurnRateAsync(
        Guid organizationId, string window, CancellationToken cancellationToken = default);

    Task<ActiveCustomersDto> GetActiveCustomersAsync(
        Guid organizationId, CancellationToken cancellationToken = default);

    Task<StaffSeatsDto> GetStaffSeatsAsync(
        Guid organizationId, CancellationToken cancellationToken = default);

    Task<BillingProfitabilityDto> GetProfitabilityAsync(
        DateTime? from, DateTime? to, string? groupBy, CancellationToken cancellationToken = default);

    Task<OrgUsageRankingPageDto> GetOrgUsageRankingAsync(
        DateTime? from, DateTime? to, int page, int pageSize, CancellationToken cancellationToken = default);

    Task<BillingAdjustmentActivityDto> GetAdjustmentsAsync(
        DateTime? from, DateTime? to, Guid? organizationId, string? actorUserId, CancellationToken cancellationToken = default);

    Task<PlanChangeHistoryDto> GetPlanChangesAsync(
        DateTime? from, DateTime? to, Guid? organizationId, CancellationToken cancellationToken = default);

    Task<DowngradeStatisticsDto> GetDowngradesAsync(
        DateTime? from, DateTime? to, Guid? organizationId, CancellationToken cancellationToken = default);
}
