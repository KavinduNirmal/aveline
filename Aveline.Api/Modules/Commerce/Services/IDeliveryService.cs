using Aveline.Api.Modules.Commerce.DTOs;

namespace Aveline.Api.Modules.Commerce.Services;

public interface IDeliveryService
{
    Task<DeliveryPlanResponseDto> CreateDeliveryPlanAsync(Guid organizationId, CreateDeliveryDto dto, CancellationToken ct = default);
    Task<DeliveryPlanResponseDto> UpdateDeliveryStatusAsync(Guid organizationId, Guid planId, UpdateDeliveryStatusDto dto, CancellationToken ct = default);
    Task<DeliveryPlanResponseDto?> GetDeliveryPlanByIdAsync(Guid id, Guid organizationId, CancellationToken ct = default);
    Task<PagedResult<DeliveryPlanResponseDto>> ListDeliveryPlansAsync(Guid organizationId, DeliveryQueryParametersDto query, CancellationToken ct = default);
}
