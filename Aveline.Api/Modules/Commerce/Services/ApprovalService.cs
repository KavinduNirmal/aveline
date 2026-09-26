using System.Net.Http.Json;
using Aveline.Api.Infrastructure.Integrations;
using Aveline.Api.Modules.Commerce.DTOs;
using Aveline.Api.Modules.Commerce.Models;
using Aveline.Api.Modules.Commerce.Repositories;
using Microsoft.Extensions.Logging;

namespace Aveline.Api.Modules.Commerce.Services;

public class ApprovalService : IApprovalService
{
    private readonly IApprovalRepository _approvalRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly IAgentServiceClient? _agentClient;
    private readonly ILogger<ApprovalService>? _logger;

    public ApprovalService(
        IApprovalRepository approvalRepository,
        IOrderRepository orderRepository,
        IAgentServiceClient? agentClient = null,
        ILogger<ApprovalService>? logger = null)
    {
        _approvalRepository = approvalRepository ?? throw new ArgumentNullException(nameof(approvalRepository));
        _orderRepository = orderRepository ?? throw new ArgumentNullException(nameof(orderRepository));
        _agentClient = agentClient;
        _logger = logger;
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

        // The agent's pricing tools speak in *rates* (0.10 = 10% off) while an approval decision
        // speaks in absolute money. The rate is derived from the order the decision just produced, and
        // only for `revise`; every other verb leaves the discount alone.
        decimal? revisedDiscountRate = null;

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

                        revisedDiscountRate = order.Subtotal > 0m
                            ? Math.Round(order.Discount / order.Subtotal, 4)
                            : 0m;
                    }
                }
                entry.Status = "revised";
                break;

            default:
                throw new ArgumentException($"Invalid decision '{dto.Decision}'. Allowed values: 'approve', 'reject', 'revise'.", nameof(dto));
        }

        await _approvalRepository.UpdateAsync(entry, ct);

        // Resume the paused workflow through its checkpoint (ADR-024, Decision 3).
        //
        // A decision is not a new question. This resumes the graph at the node that interrupted, so
        // the supervisor is never re-consulted and nothing before the pause re-executes. The previous
        // implementation re-queried `/agents/query` with the decision as the question text, which
        // re-ran the whole pipeline and - because a decision carries no commerce signal - routed to
        // the memory agent, so the resumed deal was never settled.
        await ResumePausedWorkflowAsync(
            entry, organizationId, decision, dto, order?.CustomerName, revisedDiscountRate, ct);

        return MapToDto(entry);
    }

    /// <summary>
    /// Tell the agent to settle the deal the decision was made about.
    /// </summary>
    /// <remarks>
    /// Best-effort, like every other agent call on this path: the owner's decision is already
    /// persisted and the order transitioned, and a failure to reach the agent must not roll that
    /// back or surface as an error to the dashboard. It is logged loudly because a decision the
    /// agent never heard is a decision the customer never sees.
    /// </remarks>
    private async Task ResumePausedWorkflowAsync(
        ApprovalQueueEntry entry,
        Guid organizationId,
        string decision,
        ApprovalDecisionDto dto,
        string? customerName,
        decimal? revisedDiscountRate,
        CancellationToken ct)
    {
        if (_agentClient is null || string.IsNullOrWhiteSpace(entry.ThreadId))
        {
            return;
        }

        // An approval with no conversation has no agent run behind it - it was raised from the
        // dashboard, and its thread id is generated rather than naming a checkpoint. Resuming it
        // would 404 at best; skipping is the honest reading of a row that never involved the agent.
        if (entry.ConversationId is null)
        {
            _logger?.LogInformation(
                "Approval {ApprovalId} has no conversation; the decision is complete without an agent resume.",
                entry.Id);
            return;
        }

        // The API's HTTP verbs and the graph's vocabulary are two different sets; this is the single
        // place the translation happens.
        var agentDecision = ApprovalDecisions.ToAgentDecision(decision);
        if (agentDecision is null)
        {
            return;
        }

        try
        {
            var payload = new
            {
                thread_id = entry.ThreadId,
                decision = agentDecision,
                comment = dto.Reason,
                organization_id = organizationId,
                order_id = entry.OrderId,
                // The settlement quotes the customer by name. The API knows it - it wrote the order -
                // while the checkpoint's conversation context often does not.
                customer_name = customerName,
                // A rate, not an amount: the agent's pricing tools read `proposed_discount` as
                // 0.10 == 10% off. See `revisedDiscountRate` above.
                revised_discount = revisedDiscountRate,
                conversation_id = entry.ConversationId,
            };
            using var content = JsonContent.Create(payload);
            var response = await _agentClient.PostAsync("/agents/resume", content, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger?.LogWarning(
                    "Agent resume for thread {ThreadId} returned {StatusCode}; the decision was recorded but the workflow was not settled.",
                    entry.ThreadId,
                    (int)response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(
                ex,
                "Failed to resume the paused workflow for thread {ThreadId}.",
                entry.ThreadId);
        }
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
