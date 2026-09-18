using Aveline.Api.Modules.Commerce.DTOs;
using Aveline.Api.Modules.Commerce.Models;
using Aveline.Api.Modules.Commerce.Repositories;

namespace Aveline.Api.Modules.Commerce.Services;

public class ApprovalService : IApprovalService
{
    private readonly IApprovalRepository _approvalRepository;
    private readonly IOrderRepository _orderRepository;

    public ApprovalService(IApprovalRepository approvalRepository, IOrderRepository orderRepository)
    {
        _approvalRepository = approvalRepository ?? throw new ArgumentNullException(nameof(approvalRepository));
        _orderRepository = orderRepository ?? throw new ArgumentNullException(nameof(orderRepository));
    }

    public async Task<PagedResult<ApprovalQueueResponseDto>> GetPendingApprovalsAsync(
        Guid organizationId,
        ApprovalQueryParametersDto query,
        CancellationToken ct = default)
    {
        var result = await _approvalRepository.ListAsync(organizationId, query.Status, query.Page, query.PageSize, ct);
        return new PagedResult<ApprovalQueueResponseDto>
        {
            Items = result.Items.Select(MapToDto).ToList(),
            TotalCount = result.TotalCount,
            Page = result.Page,
            PageSize = result.PageSize
        };
    }

    public async Task<ApprovalQueueResponseDto?> GetApprovalByIdAsync(
        Guid id,
        Guid organizationId,
        CancellationToken ct = default)
    {
        var entry = await _approvalRepository.GetByIdAsync(id, organizationId, ct);
        return entry is not null ? MapToDto(entry) : null;
    }

    public async Task<ApprovalQueueResponseDto> ProcessDecisionAsync(
        Guid id,
        Guid organizationId,
        ApprovalDecisionDto dto,
        Guid? decidedBy,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        if (string.IsNullOrWhiteSpace(dto.Decision))
        {
            throw new ArgumentException("Decision is required.", nameof(dto));
        }

        var entry = await _approvalRepository.GetByIdAsync(id, organizationId, ct);
        if (entry is null)
        {
            throw new KeyNotFoundException($"Approval entry '{id}' not found.");
        }

        if (!entry.Status.Equals("pending", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Approval entry '{id}' has already been processed with status '{entry.Status}'.");
        }

        var decision = dto.Decision.Trim().ToLowerInvariant();
        entry.DecisionComment = dto.Reason;
        entry.DecidedBy = decidedBy;
        entry.DecidedAt = DateTime.UtcNow;

        var order = entry.Order ?? await _orderRepository.GetByIdAsync(entry.OrderId, organizationId, ct);

        switch (decision)
        {
            case "approve":
                entry.Status = "approved";
                if (order is not null)
                {
                    order.Status = "confirmed";
                    order.UpdatedAt = DateTime.UtcNow;
                    await _orderRepository.UpdateAsync(order, ct);
                }
                break;

            case "reject":
                entry.Status = "rejected";
                if (order is not null)
                {
                    order.Status = "cancelled";
                    order.UpdatedAt = DateTime.UtcNow;
                    await _orderRepository.UpdateAsync(order, ct);
                }
                break;

            case "revise":
                if (dto.RevisedDiscount.HasValue)
                {
                    if (dto.RevisedDiscount.Value < 0)
                    {
                        throw new ArgumentException("Revised discount cannot be negative.", nameof(dto));
                    }

                    if (order is not null)
                    {
                        order.Discount = Math.Min(order.Subtotal, dto.RevisedDiscount.Value);
                        order.Total = Math.Max(0m, order.Subtotal - order.Discount);
                        var rawMargin = order.Total > 0 ? (order.Total - order.TotalCost) / order.Total : 0.0000m;
                        order.Margin = Math.Clamp(rawMargin, -0.9999m, 0.9999m);
                        order.Status = "revised";
                        order.UpdatedAt = DateTime.UtcNow;
                        await _orderRepository.UpdateAsync(order, ct);
                    }
                }
                entry.Status = "revised";
                break;

            default:
                throw new ArgumentException($"Invalid decision '{dto.Decision}'. Allowed values: 'approve', 'reject', 'revise'.", nameof(dto));
        }

        await _approvalRepository.UpdateAsync(entry, ct);
        return MapToDto(entry);
    }

    private static ApprovalQueueResponseDto MapToDto(ApprovalQueueEntry entry)
    {
        return new ApprovalQueueResponseDto
        {
            Id = entry.Id,
            OrganizationId = entry.OrganizationId,
            OrderId = entry.OrderId,
            ApprovalType = entry.ApprovalType,
            Status = entry.Status,
            ThresholdExceeded = entry.ThresholdExceeded,
            Reason = entry.Reason,
            DecisionComment = entry.DecisionComment,
            DecidedBy = entry.DecidedBy,
            ThreadId = entry.ThreadId,
            ConversationId = entry.ConversationId,
            CreatedAt = entry.CreatedAt,
            DecidedAt = entry.DecidedAt,
            Order = entry.Order is not null ? MapOrderToDto(entry.Order) : null
        };
    }

    private static OrderResponseDto MapOrderToDto(Order order)
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
            Items = order.Items?.Select(i => new OrderItemDto
            {
                Id = i.Id,
                ItemId = i.ItemId,
                ItemName = i.ItemName,
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice,
                WholesaleCost = i.WholesaleCost,
                TotalPrice = i.TotalPrice
            }).ToList() ?? new List<OrderItemDto>()
        };
    }
}
