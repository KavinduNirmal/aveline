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
        { "pending_approval", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "approved", "confirmed", "rejected", "cancelled", "revised" } },
        { "approved", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "payment_requested", "cancelled" } },
        { "confirmed", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "payment_requested", "cancelled" } },
        { "revised", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "payment_requested", "cancelled" } },
        { "payment_requested", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "payment_confirmed", "payment_expired", "cancelled" } },
        { "payment_expired", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "payment_requested", "cancelled" } },
        { "payment_confirmed", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "delivery_scheduled", "completed", "cancelled" } },
        { "delivery_scheduled", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "delivered", "cancelled" } },
        { "delivered", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "completed" } }
    };

    private readonly IApprovalRepository? _approvalRepository;
    private readonly IBoutiqueSaleLedgerService? _ledger;
    private readonly IPaymentRepository? _paymentRepository;

    /// <summary>
    /// Every status the product can write, derived from the transition map rather than transcribed
    /// from the model's comment — which names eleven and forgets `confirmed` and `revised`, both of
    /// which the approval path writes.
    /// </summary>
    /// <remarks>
    /// Exposed so a KPI that classifies orders can be checked against the real map: adding a status
    /// to <see cref="ValidTransitions"/> without classifying it fails that test rather than silently
    /// changing every figure in the tenant dashboard.
    /// </remarks>
    public static IReadOnlyCollection<string> KnownOrderStatuses { get; } =
        ValidTransitions
            .SelectMany(pair => new[] { pair.Key }.Concat(pair.Value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public OrderService(
        IOrderRepository orderRepository,
        ILogger<OrderService> logger,
        IBusinessRulesService? businessRulesService = null,
        IApprovalRepository? approvalRepository = null,
        IBoutiqueSaleLedgerService? ledger = null,
        IPaymentRepository? paymentRepository = null)
    {
        _orderRepository = orderRepository ?? throw new ArgumentNullException(nameof(orderRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _businessRulesService = businessRulesService;
        _approvalRepository = approvalRepository;
        _ledger = ledger;
        _paymentRepository = paymentRepository;
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
        string approvalReason = "Requires approval based on business rules evaluation";
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
                    if (evaluation.TriggeredRules != null && evaluation.TriggeredRules.Count > 0)
                    {
                        approvalReason = string.Join(", ", evaluation.TriggeredRules);
                    }
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

        if (initialStatus == "pending_approval" && _approvalRepository != null)
        {
            try
            {
                // `ThreadId` is required (ADR-024, Decision 4). An order raised from the dashboard
                // rather than from a conversation has no checkpoint to resume, so it gets a generated
                // value; a null `ConversationId` is what tells the resume path there is no agent run
                // behind the row rather than silently re-querying with a thread that names nothing.
                var threadId = string.IsNullOrWhiteSpace(dto.ThreadId)
                    ? Guid.NewGuid().ToString("N")
                    : dto.ThreadId.Trim();

                var approvalEntry = new ApprovalQueueEntry
                {
                    Id = Guid.NewGuid(),
                    OrganizationId = organizationId,
                    OrderId = createdOrder.Id,
                    ApprovalType = "order_approval",
                    Status = "pending",
                    ThresholdExceeded = true,
                    Reason = approvalReason,
                    ThreadId = threadId,
                    ConversationId = dto.ConversationId,
                    CreatedAt = DateTime.UtcNow
                };
                await _approvalRepository.AddAsync(approvalEntry, cancellationToken);
                _logger.LogInformation("Enqueued order {OrderId} for approval with ThreadId {ThreadId}", createdOrder.Id, threadId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to enqueue approval for order {OrderId}", createdOrder.Id);
            }
        }

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

    public async Task<OrderResponseDto> UpdateOrderAsync(
        Guid id,
        Guid organizationId,
        CreateOrderDto dto,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        var order = await _orderRepository.GetByIdAsync(id, organizationId, cancellationToken)
            ?? throw new KeyNotFoundException($"Order '{id}' not found.");

        decimal subtotal = 0m;
        decimal totalCost = 0m;
        var orderItems = new List<OrderItem>();

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
                OrderId = order.Id,
                ItemId = itemDto.ItemId,
                ItemName = itemDto.ItemName,
                Quantity = itemDto.Quantity,
                UnitPrice = itemDto.UnitPrice,
                WholesaleCost = itemDto.WholesaleCost,
                TotalPrice = itemTotal,
                CreatedAt = DateTime.UtcNow
            });
        }

        decimal discount = Math.Min(subtotal, Math.Max(0m, dto.Discount ?? 0m));
        decimal total = Math.Max(0m, subtotal - discount);
        decimal rawMargin = total > 0 ? (total - totalCost) / total : 0.0000m;

        order.CustomerName = dto.CustomerName;
        order.OrderType = string.IsNullOrWhiteSpace(dto.OrderType) ? "whatsapp" : dto.OrderType.Trim().ToLowerInvariant();
        order.Subtotal = subtotal;
        order.Discount = discount;
        order.Total = total;
        order.TotalCost = totalCost;
        order.Margin = Math.Clamp(rawMargin, -0.9999m, 0.9999m);
        order.Items = orderItems;
        order.UpdatedAt = DateTime.UtcNow;

        var updated = await _orderRepository.UpdateAsync(order, cancellationToken);
        return MapToDto(updated);
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
        // Kept separately: `previousStatus` names the status the order was in *before* this
        // transition, which the settled-sale rule below needs in order to tell a paid order from an
        // unpaid one.
        var previousStatus = currentStatus;

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

        if (_ledger is not null && SettledStatuses.Contains(targetStatus))
        {
            // The fourth writer: an order that reached a paid terminal status posts its **billed
            // value** as a `Derived` entry — "this is what the order says was sold", with no
            // evidence that money moved.
            //
            // It is skipped when the order passed through `payment_confirmed`, because
            // `PaymentService.ConfirmPaymentAsync` has already written a `Verified` entry for the
            // same money. Posting both would count one sale twice, which is precisely the mistake
            // the two-basis design exists to prevent.
            //
            // The check asks the payment table, not the status history: a confirmed payment row is
            // direct evidence that the money was collected, whereas an order can reach `delivered`
            // from several places. `Order.PaymentId` has no foreign key, and
            // `IPaymentRepository.GetByOrderIdAsync` returns the order's single payment, so reading
            // it is the same source the payment surface shows.
            var alreadyCollected = string.Equals(
                previousStatus, "payment_confirmed", StringComparison.OrdinalIgnoreCase);
            if (!alreadyCollected && _paymentRepository is not null)
            {
                var payment = await _paymentRepository.GetByOrderIdAsync(id, organizationId, cancellationToken);
                alreadyCollected = payment is not null
                    && payment.Status.Equals("confirmed", StringComparison.OrdinalIgnoreCase);
            }
            if (!alreadyCollected)
            {
                await _ledger.RecordAsync(new RecordBoutiqueSaleCommand(
                    OrganizationId: organizationId,
                    Amount: order.Total,
                    Reason: $"Order {id} reached '{targetStatus}'; billed value awaiting confirmation.",
                    Kind: BoutiqueSaleEntryKind.Sale,
                    ChargeBasis: BoutiqueSaleChargeBasis.Derived,
                    SourceKind: BoutiqueSaleSourceKind.OrderSettlement,
                    SourceRef: $"order:{id}",
                    OccurredAt: order.UpdatedAt ?? DateTime.UtcNow,
                    RecordedByUserId: null,
                    OrderId: id,
                    CustomerId: order.CustomerId), cancellationToken);
            }
        }

        return MapToDto(updatedOrder);
    }

    /// <summary>
    /// The statuses at which an order's value becomes a settled sale. Terminal-negative states
    /// (`cancelled`, `rejected`) are absent on purpose, and so are the transient ones.
    /// </summary>
    private static readonly HashSet<string> SettledStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "completed", "delivered",
    };

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
