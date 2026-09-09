using Aveline.Api.Modules.CustomerConcierge.DTOs;

namespace Aveline.Api.Modules.CustomerConcierge.Services;

/// <summary>
/// Loyalty/tier progression for customers (Issue #169). Customers carry a lifecycle status
/// (new/returning/vip/dormant) plus <c>TotalSpent</c>/<c>VisitCount</c>/<c>LastVisitAt</c> which
/// are populated by purchase activity (Commerce / Slice 3). This service derives the status from
/// that data and supports an owner override.
/// </summary>
public interface ICustomerLoyaltyService
{
    /// <summary>
    /// Recomputes (and persists) a customer's status from their spend/visits/last-visit, or applies
    /// an owner override when <paramref name="overrideStatus"/> is supplied. Returns the resolved
    /// status, or <c>null</c> when the customer does not exist in the org.
    /// </summary>
    Task<CustomerStatusDto?> RecomputeAsync(
        Guid orgId,
        Guid customerId,
        string? overrideStatus = null,
        CancellationToken cancellationToken = default);
}
