using Aveline.Api.Modules.Commerce.DTOs;
using Aveline.Api.Modules.Commerce.Models;
using Aveline.Api.Modules.Commerce.Repositories;
using Microsoft.Extensions.Logging;

namespace Aveline.Api.Modules.Commerce.Services;

public class OrderService : IOrderService
{
    private readonly IOrderRepository _orderRepository;
    private readonly IBusinessRulesService? _businessRulesService;
    private readonly ILogger<OrderService> _logger;

    private static readonly Dictionary<string, HashSet<string>> ValidTransitions = new(StringComparer.OrdinalIgnoreCase)
    {
        { "pending_hold", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "pending_approval", "payment_requested", "cancelled" } },
        { "pending_approval", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "approved", "rejected", "cancelled" } },
        { "approved", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "payment_requested", "cancelled" } },
        { "payment_requested", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "payment_confirmed", "payment_expired", "cancelled" } },
        { "payment_expired", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "payment_requested", "cancelled" } },
        { "payment_confirmed", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "delivery_scheduled", "completed", "cancelled" } },
        { "delivery_scheduled", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "delivered", "cancelled" } },
        { "delivered", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "completed" } }
    };

    public OrderService(
        IOrderRepository orderRepository,
        ILogger<OrderService> logger,
        IBusinessRulesService? businessRulesService = null)
    {
        _orderRepository = orderRepository ?? throw new ArgumentNullException(nameof(orderRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _businessRulesService = businessRulesService;
    }

    public async Task<OrderResponseDto> CreateOrderAsync(
        Guid organizationId,
        CreateOrderDto dto,
        Guid? createdBy = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        if (dto.Items == null || dto.Items.Count == 0)
        {
            throw new ArgumentException("Order must contain at least one line item.", nameof(dto));
        }

        var orderId = Guid.NewGuid();
        var orderItems = new List<OrderItem>();
        decimal subtotal = 0m;
        decimal totalCost = 0m;

        foreach (var itemDto in dto.Items)
        {
            var itemTotal = Math.Round(itemDto.UnitPrice * itemDto.Quantity, 2);
            var itemCost = Math.Round(itemDto.WholesaleCost * itemDto.Quantity, 2);

            subtotal += itemTotal;
            totalCost += itemCost;

            orderItems.Add(new OrderItem
            {
                Id = itemDto.Id ?? Guid.NewGuid(),
                OrganizationId = organizationId,
                OrderId = orderId,
                ItemId = itemDto.ItemId,
                ItemName = itemDto.ItemName,
                Quantity = itemDto.Quantity,
                UnitPrice = itemDto.UnitPrice,
                WholesaleCost = itemDto.WholesaleCost,
                TotalPrice = itemTotal,
                CreatedAt = DateTime.UtcNow
            });
        }

        // Apply discount: explicit discount takes precedence, otherwise tier discount
        decimal discount = 0m;
        if (dto.Discount.HasValue && dto.Discount.Value > 0)
        {
            discount = Math.Min(subtotal, Math.Round(dto.Discount.Value, 2));
        }
        else if (!string.IsNullOrWhiteSpace(dto.CustomerTier))
        {
            discount = CalculateTierDiscount(subtotal, dto.CustomerTier);
        }

        var total = Math.Max(0m, Math.Round(subtotal - discount, 2));
        var margin = total > 0 ? Math.Round((total - totalCost) / total, 4) : 0m;

        // Determine initial status based on business rules evaluation
        var initialStatus = "payment_requested";
        if (_businessRulesService != null)
        {
            try
            {
                var discountRate = subtotal > 0 ? Math.Round(discount / subtotal, 4) : 0m;
                var evalRequest = new EvaluateOrderRulesRequestDto(total, margin, discountRate, dto.CustomerTier);
                var evaluation = await _businessRulesService.EvaluateOrderRulesAsync(
                    organizationId,
                    evalRequest,
                    cancellationToken);

                if (evaluation.RequiresApproval)
                {
                    initialStatus = "pending_approval";
                    _logger.LogInformation(
                        "Order {OrderId} requires approval due to rules: {Rules}",
                        orderId,
                        string.Join(", ", evaluation.TriggeredRules));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to evaluate business rules for order {OrderId}. Defaulting to pending_hold.", orderId);
                initialStatus = "pending_hold";
            }
        }

        var order = new Order
        {
            Id = orderId,
            OrganizationId = organizationId,
            CustomerId = dto.CustomerId,
            CustomerName = dto.CustomerName,
            OrderType = string.IsNullOrWhiteSpace(dto.OrderType) ? "whatsapp" : dto.OrderType.Trim().ToLowerInvariant(),
            Status = initialStatus,
            Subtotal = subtotal,
            Discount = discount,
            Total = total,
            TotalCost = totalCost,
            Margin = margin,
            CreatedBy = createdBy,
            CreatedAt = DateTime.UtcNow,
            Items = orderItems
        };

        var createdOrder = await _orderRepository.CreateAsync(order, cancellationToken);
        _logger.LogInformation("Created order {OrderId} for customer {CustomerName} with initial status {Status}", createdOrder.Id, createdOrder.CustomerName, createdOrder.Status);

        return MapToDto(createdOrder);
    }

    public async Task<OrderResponseDto?> GetOrderByIdAsync(
        Guid id,
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(id, organizationId, cancellationToken);
        return order == null ? null : MapToDto(order);
    }

    public async Task<PagedResult<OrderResponseDto>> GetOrdersAsync(
        Guid organizationId,
        OrderQueryParametersDto query,
        CancellationToken cancellationToken = default)
    {
        var (items, totalCount) = await _orderRepository.GetPagedAsync(organizationId, query, cancellationToken);

        return new PagedResult<OrderResponseDto>
        {
            Items = items.Select(MapToDto).ToList(),
            TotalCount = totalCount,
            Page = query.Page,
            PageSize = query.PageSize
        };
    }

    public async Task<OrderResponseDto> TransitionStatusAsync(
        Guid id,
        Guid organizationId,
        UpdateOrderStatusDto dto,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        var order = await _orderRepository.GetByIdAsync(id, organizationId, cancellationToken)
            ?? throw new KeyNotFoundException($"Order {id} was not found.");

        var targetStatus = dto.Status.Trim().ToLowerInvariant();
        var currentStatus = order.Status.Trim().ToLowerInvariant();

        if (string.Equals(currentStatus, targetStatus, StringComparison.OrdinalIgnoreCase))
        {
            return MapToDto(order);
        }

        if (!ValidTransitions.TryGetValue(currentStatus, out var allowedTransitions) || !allowedTransitions.Contains(targetStatus))
        {
            throw new InvalidOperationException($"Invalid status transition from '{currentStatus}' to '{targetStatus}'.");
        }

        order.Status = targetStatus;
        order.UpdatedAt = DateTime.UtcNow;

        var updatedOrder = await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Order {OrderId} transitioned from {OldStatus} to {NewStatus}", id, currentStatus, targetStatus);

        return MapToDto(updatedOrder);
    }

    public async Task<bool> CancelOrderAsync(
        Guid id,
        Guid organizationId,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(id, organizationId, cancellationToken);
        if (order == null)
        {
            return false;
        }

        if (order.Status.Equals("completed", StringComparison.OrdinalIgnoreCase) ||
            order.Status.Equals("cancelled", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Cannot cancel order {id} because it is already '{order.Status}'.");
        }

        order.Status = "cancelled";
        order.UpdatedAt = DateTime.UtcNow;

        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Order {OrderId} cancelled. Reason: {Reason}", id, reason ?? "No reason provided");

        return true;
    }

    public async Task<OrderResponseDto> RecalculateOrderAsync(
        Guid id,
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(id, organizationId, cancellationToken)
            ?? throw new KeyNotFoundException($"Order {id} was not found.");

        decimal subtotal = 0m;
        decimal totalCost = 0m;

        foreach (var item in order.Items)
        {
            item.TotalPrice = Math.Round(item.UnitPrice * item.Quantity, 2);
            subtotal += item.TotalPrice;
            totalCost += Math.Round(item.WholesaleCost * item.Quantity, 2);
        }

        order.Subtotal = subtotal;
        order.TotalCost = totalCost;
        order.Total = Math.Max(0m, Math.Round(subtotal - order.Discount, 2));
        order.Margin = order.Total > 0 ? Math.Round((order.Total - totalCost) / order.Total, 4) : 0m;
        order.UpdatedAt = DateTime.UtcNow;

        var updatedOrder = await _orderRepository.UpdateAsync(order, cancellationToken);
        return MapToDto(updatedOrder);
    }

    public static decimal CalculateTierDiscount(decimal subtotal, string tier)
    {
        var percentage = tier.Trim().ToLowerInvariant() switch
        {
            "vip" => 0.05m,
            "bronze" => 0.03m,
            "silver" => 0.07m,
            "gold" => 0.10m,
            "platinum" => 0.15m,
            _ => 0.00m
        };

        return Math.Round(subtotal * percentage, 2);
    }

    private static OrderResponseDto MapToDto(Order order)
    {
        return new OrderResponseDto
        {
            Id = order.Id,
            OrganizationId = order.OrganizationId,
            CustomerId = order.CustomerId,
            CustomerName = order.CustomerName,
            OrderType = order.OrderType,
            Status = order.Status,
            Subtotal = order.Subtotal,
            Discount = order.Discount,
            Total = order.Total,
            TotalCost = order.TotalCost,
            Margin = order.Margin,
            CreatedBy = order.CreatedBy,
            CreatedAt = order.CreatedAt,
            UpdatedAt = order.UpdatedAt,
            Items = order.Items.Select(i => new OrderItemDto
            {
                Id = i.Id,
                ItemId = i.ItemId,
                ItemName = i.ItemName,
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice,
                WholesaleCost = i.WholesaleCost,
                TotalPrice = i.TotalPrice
            }).ToList()
        };
    }
}
