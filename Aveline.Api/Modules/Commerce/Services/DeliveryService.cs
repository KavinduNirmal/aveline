using Aveline.Api.Modules.Commerce.DTOs;
using Aveline.Api.Modules.Commerce.Models;
using Aveline.Api.Modules.Commerce.Repositories;

namespace Aveline.Api.Modules.Commerce.Services;

public class DeliveryService : IDeliveryService
{
    private readonly IDeliveryRepository _deliveryRepository;
    private readonly IOrderRepository _orderRepository;

    public DeliveryService(IDeliveryRepository deliveryRepository, IOrderRepository orderRepository)
    {
        _deliveryRepository = deliveryRepository ?? throw new ArgumentNullException(nameof(deliveryRepository));
        _orderRepository = orderRepository ?? throw new ArgumentNullException(nameof(orderRepository));
    }

    public async Task<DeliveryPlanResponseDto> CreateDeliveryPlanAsync(
        Guid organizationId,
        CreateDeliveryDto dto,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        if (string.IsNullOrWhiteSpace(dto.DeliveryAddress))
        {
            throw new ArgumentException("Delivery address is required.", nameof(dto));
        }

        var order = await _orderRepository.GetByIdAsync(dto.OrderId, organizationId, ct);
        if (order is null)
        {
            throw new KeyNotFoundException($"Order '{dto.OrderId}' not found.");
        }

        var planId = Guid.NewGuid();
        var carrier = string.IsNullOrWhiteSpace(dto.CourierService) ? "PickMe" : dto.CourierService.Trim();
        var shortRef = planId.ToString("N")[..6].ToUpperInvariant();
        var carrierCode = carrier.Length >= 2 ? carrier[..2].ToUpperInvariant() : "DL";
        var trackingNumber = $"TRK-{carrierCode}-{shortRef}";

        decimal estimatedCost;
        if (dto.EstimatedCost.HasValue && dto.EstimatedCost.Value > 0)
        {
            estimatedCost = Math.Round(dto.EstimatedCost.Value, 2);
        }
        else
        {
            estimatedCost = dto.DeliveryAddress.ToLowerInvariant().Contains("colombo") ? 650.00m : 850.00m;
        }

        var plan = new DeliveryPlan
        {
            Id = planId,
            OrganizationId = organizationId,
            OrderId = dto.OrderId,
            CourierService = carrier,
            TrackingNumber = trackingNumber,
            DeliveryAddress = dto.DeliveryAddress.Trim(),
            PreferredDeliveryTime = dto.PreferredDeliveryTime,
            EstimatedCost = estimatedCost,
            Status = "planned",
            CreatedAt = DateTime.UtcNow
        };

        order.Status = "delivery_scheduled";
        order.UpdatedAt = DateTime.UtcNow;

        await _deliveryRepository.AddAsync(plan, ct);
        await _orderRepository.UpdateAsync(order, ct);

        return MapToDto(plan);
    }

    public async Task<DeliveryPlanResponseDto> UpdateDeliveryStatusAsync(
        Guid organizationId,
        Guid planId,
        UpdateDeliveryStatusDto dto,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        if (string.IsNullOrWhiteSpace(dto.Status))
        {
            throw new ArgumentException("Status is required.", nameof(dto));
        }

        var plan = await _deliveryRepository.GetByIdAsync(planId, organizationId, ct);
        if (plan is null)
        {
            throw new KeyNotFoundException($"Delivery plan '{planId}' not found.");
        }

        plan.Status = dto.Status.Trim().ToLowerInvariant();
        plan.UpdatedAt = DateTime.UtcNow;

        if (!string.IsNullOrWhiteSpace(dto.TrackingNumber))
        {
            plan.TrackingNumber = dto.TrackingNumber.Trim();
        }

        if (!string.IsNullOrWhiteSpace(dto.CourierService))
        {
            plan.CourierService = dto.CourierService.Trim();
        }

        if (plan.Status == "delivered")
        {
            var order = plan.Order ?? await _orderRepository.GetByIdAsync(plan.OrderId, organizationId, ct);
            if (order is not null)
            {
                order.Status = "delivered";
                order.UpdatedAt = DateTime.UtcNow;
                await _orderRepository.UpdateAsync(order, ct);
            }
        }

        await _deliveryRepository.UpdateAsync(plan, ct);
        return MapToDto(plan);
    }

    public async Task<DeliveryPlanResponseDto?> GetDeliveryPlanByIdAsync(
        Guid id,
        Guid organizationId,
        CancellationToken ct = default)
    {
        var plan = await _deliveryRepository.GetByIdAsync(id, organizationId, ct);
        return plan is not null ? MapToDto(plan) : null;
    }

    public async Task<PagedResult<DeliveryPlanResponseDto>> ListDeliveryPlansAsync(
        Guid organizationId,
        DeliveryQueryParametersDto query,
        CancellationToken ct = default)
    {
        var result = await _deliveryRepository.ListAsync(organizationId, query, ct);
        return new PagedResult<DeliveryPlanResponseDto>
        {
            Items = result.Items.Select(MapToDto).ToList(),
            TotalCount = result.TotalCount,
            Page = result.Page,
            PageSize = result.PageSize
        };
    }

    private static DeliveryPlanResponseDto MapToDto(DeliveryPlan plan)
    {
        return new DeliveryPlanResponseDto
        {
            Id = plan.Id,
            OrganizationId = plan.OrganizationId,
            OrderId = plan.OrderId,
            CourierService = plan.CourierService,
            TrackingNumber = plan.TrackingNumber,
            DeliveryAddress = plan.DeliveryAddress,
            PreferredDeliveryTime = plan.PreferredDeliveryTime,
            RouteOptimized = plan.RouteOptimized,
            EstimatedCost = plan.EstimatedCost,
            EstimatedEta = plan.EstimatedEta,
            Status = plan.Status,
            CreatedAt = plan.CreatedAt,
            UpdatedAt = plan.UpdatedAt
        };
    }
}
