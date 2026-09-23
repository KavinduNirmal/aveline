using Aveline.Api.Modules.Commerce.DTOs;
using Aveline.Api.Modules.Commerce.Repositories;
using Aveline.Api.Modules.CustomerConcierge.Services;
using Microsoft.Extensions.Logging;

namespace Aveline.Api.Modules.Commerce.Services;

/// <summary>
/// The order an agent pause was turned into, and the customer it belongs to.
/// </summary>
/// <param name="OrderId">The order the approval queue entry references.</param>
/// <param name="CustomerId">The customer the order is filed against, so the caller can bind the
/// conversation to a real record instead of leaving it unknown.</param>
/// <param name="Created">
/// False when an order for this thread already existed. That is the idempotent path, not an error.
/// </param>
/// <param name="RequiresApproval">
/// Whether the order actually landed in <c>pending_approval</c>. It can only be false when the
/// business rules changed between the agent's evaluation and this write, which is a genuine
/// inconsistency worth surfacing.
/// </param>
public sealed record PausedOrderOutcome(
    Guid OrderId,
    Guid CustomerId,
    bool Created,
    bool RequiresApproval);

/// <summary>
/// Turns an agent's <c>pending_approval</c> verdict into an order the owner can actually act on
/// (ADR-024, Decision 2).
/// </summary>
public interface IConversationOrderBridge
{
    /// <summary>
    /// Create the order a paused run needs, or return <c>null</c> when there is nothing to create.
    /// </summary>
    Task<PausedOrderOutcome?> CreateForPausedRunAsync(
        Guid organizationId,
        string? threadId,
        Guid? conversationId,
        Guid? customerId,
        string? phoneNumber,
        string? customerName,
        IReadOnlyList<OrderContextItem> items,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The bridge between a conversation pause and the order lifecycle.
/// </summary>
/// <remarks>
/// <para>
/// The whole reason this type exists is a schema fact: <c>ApprovalQueueEntry.OrderId</c> is required
/// and references an <c>Order</c>, and before ADR-024 only the staff orders API ever created one. An
/// agent pause therefore had nowhere to land, and the Approval Queue stayed empty however loudly the
/// agent asked for sign-off.
/// </para>
/// <para>
/// It delegates to <see cref="IOrderService.CreateOrderAsync"/> rather than writing an Order itself,
/// so the pause path inherits the same rules evaluation, the same status transitions and the same
/// approval-queue write as the staff path. The agent still writes nothing (invariant A5).
/// </para>
/// </remarks>
public sealed class ConversationOrderBridge : IConversationOrderBridge
{
    private readonly IOrderService _orders;
    private readonly IApprovalRepository _approvals;
    private readonly ICustomerService _customers;
    private readonly ILogger<ConversationOrderBridge> _logger;

    public ConversationOrderBridge(
        IOrderService orders,
        IApprovalRepository approvals,
        ICustomerService customers,
        ILogger<ConversationOrderBridge> logger)
    {
        _orders = orders ?? throw new ArgumentNullException(nameof(orders));
        _approvals = approvals ?? throw new ArgumentNullException(nameof(approvals));
        _customers = customers ?? throw new ArgumentNullException(nameof(customers));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<PausedOrderOutcome?> CreateForPausedRunAsync(
        Guid organizationId,
        string? threadId,
        Guid? conversationId,
        Guid? customerId,
        string? phoneNumber,
        string? customerName,
        IReadOnlyList<OrderContextItem> items,
        CancellationToken cancellationToken = default)
    {
        // Invariant A6: a pause nobody can reach is not an approval. Without a thread there is no
        // checkpoint to resume and no way to link the decision back to the run, so the pause is left
        // as telemetry only.
        if (string.IsNullOrWhiteSpace(threadId))
        {
            _logger.LogWarning(
                "Agent paused for organization {OrganizationId} with no thread id; no order was created.",
                organizationId);
            return null;
        }

        if (items.Count == 0)
        {
            // The agent can only have paused on a deal it evaluated, which requires items. Reaching
            // here means the API and the reply disagree, and inventing an order would hide that.
            _logger.LogWarning(
                "Agent paused for thread {ThreadId} but the conversation produced no line items; no order was created.",
                threadId);
            return null;
        }

        var pending = await _approvals.GetPendingByThreadIdAsync(organizationId, threadId, cancellationToken);
        if (pending is not null)
        {
            // Invariant A4. The run that pauses is a run that may be retried - a redelivered webhook,
            // a client timeout, an operator replaying a message - and a second order for one pause
            // would double every figure derived from it.
            _logger.LogInformation(
                "Thread {ThreadId} already has a pending approval {ApprovalId}; reusing order {OrderId}.",
                threadId,
                pending.Id,
                pending.OrderId);
            var existingCustomer = pending.Order?.CustomerId ?? Guid.Empty;
            return new PausedOrderOutcome(pending.OrderId, existingCustomer, Created: false, RequiresApproval: true);
        }

        var resolvedCustomerId = customerId is { } bound && bound != Guid.Empty
            ? bound
            : await ResolveCustomerAsync(organizationId, phoneNumber, customerName, cancellationToken);

        if (resolvedCustomerId is null)
        {
            // An order must reference a customer. Creating a nameless placeholder here would be the
            // duplicate-customer bug all over again, so the pause is recorded and the order is not.
            _logger.LogWarning(
                "Agent paused for thread {ThreadId} but no customer could be resolved; no order was created.",
                threadId);
            return null;
        }

        var dto = new CreateOrderDto
        {
            CustomerId = resolvedCustomerId.Value,
            CustomerName = string.IsNullOrWhiteSpace(customerName) ? "Customer" : customerName.Trim(),
            OrderType = "whatsapp",
            Items = items.Select(item => new OrderItemDto
            {
                ItemId = item.ItemId,
                ItemName = item.ItemName,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                WholesaleCost = item.WholesaleCost,
                TotalPrice = item.TotalPrice
            }).ToList(),
            ThreadId = threadId,
            ConversationId = conversationId
        };

        var order = await _orders.CreateOrderAsync(organizationId, dto, createdBy: null, cancellationToken);
        var pendingApproval = string.Equals(order.Status, "pending_approval", StringComparison.OrdinalIgnoreCase);

        if (!pendingApproval)
        {
            _logger.LogWarning(
                "Agent paused for thread {ThreadId} but order {OrderId} was created as {Status}; "
                + "the business rules and the agent disagree and the pause has no approval row.",
                threadId,
                order.Id,
                order.Status);
        }

        return new PausedOrderOutcome(order.Id, order.CustomerId, Created: true, RequiresApproval: pendingApproval);
    }

    /// <summary>
    /// Find (or create) the customer behind an inbound conversation's phone number.
    /// </summary>
    /// <remarks>
    /// Reuses <see cref="ICustomerService.IdentifyOrCreateAsync"/>, the same call the agent's own
    /// identify tool lands on, so a first-contact order and a later staff lookup agree about who the
    /// person is.
    /// </remarks>
    private async Task<Guid?> ResolveCustomerAsync(
        Guid organizationId,
        string? phoneNumber,
        string? customerName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            return null;
        }

        var profile = await _customers.IdentifyOrCreateAsync(
            organizationId,
            phoneNumber,
            string.IsNullOrWhiteSpace(customerName) ? null : customerName,
            cancellationToken);

        return profile.CustomerId == Guid.Empty ? null : profile.CustomerId;
    }
}
