using Aveline.Api.Modules.Commerce.DTOs;

namespace Aveline.Api.Modules.Commerce.Services;

public interface IOrderService
{
    Task<OrderResponseDto> CreateOrderAsync(
        Guid organizationId,
        CreateOrderDto dto,
        Guid? createdBy = null,
        CancellationToken cancellationToken = default);

    Task<OrderResponseDto?> GetOrderByIdAsync(
        Guid id,
        Guid organizationId,
        CancellationToken cancellationToken = default);

    Task<OrderResponseDto> UpdateOrderAsync(
        Guid id,
        Guid organizationId,
        CreateOrderDto dto,
        CancellationToken cancellationToken = default);

    Task<PagedResult<OrderResponseDto>> GetOrdersAsync(
        Guid organizationId,
        OrderQueryParametersDto query,
        CancellationToken cancellationToken = default);

    Task<OrderResponseDto> TransitionStatusAsync(
        Guid id,
        Guid organizationId,
        UpdateOrderStatusDto dto,
        CancellationToken cancellationToken = default);

    Task<bool> CancelOrderAsync(
        Guid id,
        Guid organizationId,
        string? reason = null,
        CancellationToken cancellationToken = default);

    Task<OrderResponseDto> RecalculateOrderAsync(
        Guid id,
        Guid organizationId,
        CancellationToken cancellationToken = default);
}
