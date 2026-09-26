using Aveline.Api.Modules.Commerce.DTOs;
using Aveline.Api.Modules.Commerce.Models;

namespace Aveline.Api.Modules.Commerce.Repositories;

public interface IDeliveryRepository
{
    Task<DeliveryPlan?> GetByIdAsync(Guid id, Guid organizationId, CancellationToken ct = default);
    Task<DeliveryPlan?> GetByOrderIdAsync(Guid orderId, Guid organizationId, CancellationToken ct = default);
    Task<PagedResult<DeliveryPlan>> ListAsync(Guid organizationId, DeliveryQueryParametersDto query, CancellationToken ct = default);
    Task<DeliveryPlan> AddAsync(DeliveryPlan plan, CancellationToken ct = default);
    Task<DeliveryPlan> UpdateAsync(DeliveryPlan plan, CancellationToken ct = default);
}
