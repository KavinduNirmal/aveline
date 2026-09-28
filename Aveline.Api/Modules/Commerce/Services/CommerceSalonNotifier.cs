using System.Text.Json;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Commerce.Models;
using Aveline.Api.Modules.Commerce.Repositories;
using Aveline.Api.Modules.Conversations.DTOs;
using Aveline.Api.Modules.Conversations.Models;
using Aveline.Api.Modules.Conversations.Repositories;
using Aveline.Api.Modules.Conversations.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aveline.Api.Modules.Commerce.Services;

/// <summary>
/// Dispatches real-time Lina notifications into the customer Salon thread when order lifecycle events occur.
/// </summary>
public class CommerceSalonNotifier : ICommerceSalonNotifier
{
    private readonly IOrderRepository _orderRepository;
    private readonly IApprovalRepository _approvalRepository;
    private readonly IPaymentRepository _paymentRepository;
    private readonly IConversationRepository _conversations;
    private readonly IMessageRepository _messages;
    private readonly IMessageBroadcaster _broadcaster;
    private readonly AppDbContext _db;
    private readonly ILogger<CommerceSalonNotifier> _logger;

    public CommerceSalonNotifier(
        IOrderRepository orderRepository,
        IApprovalRepository approvalRepository,
        IPaymentRepository paymentRepository,
        IConversationRepository conversations,
        IMessageRepository messages,
        IMessageBroadcaster broadcaster,
        AppDbContext db,
        ILogger<CommerceSalonNotifier> logger)
    {
        _orderRepository = orderRepository ?? throw new ArgumentNullException(nameof(orderRepository));
        _approvalRepository = approvalRepository ?? throw new ArgumentNullException(nameof(approvalRepository));
        _paymentRepository = paymentRepository ?? throw new ArgumentNullException(nameof(paymentRepository));
        _conversations = conversations ?? throw new ArgumentNullException(nameof(conversations));
        _messages = messages ?? throw new ArgumentNullException(nameof(messages));
        _broadcaster = broadcaster ?? throw new ArgumentNullException(nameof(broadcaster));
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task NotifyOrderApprovedAsync(
        Guid organizationId,
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, organizationId, cancellationToken);
        if (order is null)
        {
            _logger.LogWarning("Cannot notify approval: Order {OrderId} not found.", orderId);
            return;
        }

        var conversation = await ResolveConversationAsync(organizationId, order, cancellationToken);
        if (conversation is null)
        {
            _logger.LogInformation("No salon conversation found for Order {OrderId} customer {CustomerId}.", orderId, order.CustomerId);
            return;
        }

        var shortId = order.Id.ToString("N")[..8];
        var text = $"Order #{shortId} for {order.CustomerName} (LKR {order.Total:N0}) has been approved by the boutique owner. The order is now confirmed and ready for payment or fulfillment.";
        var blocks = new object[]
        {
            new { type = "text", text = text },
            new { type = "suggestion", text = "Would you like me to request payment from the client or schedule delivery?" }
        };

        await PublishAgentMessageAsync(conversation, blocks, cancellationToken);
    }

    public async Task NotifyPaymentRequestedAsync(
        Guid organizationId,
        Guid orderId,
        string? checkoutUrl = null,
        decimal? amount = null,
        CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, organizationId, cancellationToken);
        if (order is null)
        {
            _logger.LogWarning("Cannot notify payment requested: Order {OrderId} not found.", orderId);
            return;
        }

        var conversation = await ResolveConversationAsync(organizationId, order, cancellationToken);
        if (conversation is null)
        {
            _logger.LogInformation("No salon conversation found for Order {OrderId} customer {CustomerId}.", orderId, order.CustomerId);
            return;
        }

        if (string.IsNullOrWhiteSpace(checkoutUrl) || amount is null)
        {
            var payment = await _paymentRepository.GetByOrderIdAsync(orderId, organizationId, cancellationToken);
            if (payment is not null)
            {
                checkoutUrl ??= payment.PaymentLink;
                amount ??= payment.Amount;
            }
        }

        var effectiveAmount = amount ?? order.Total;
        var shortId = order.Id.ToString("N")[..8];
        var text = $"Payment of LKR {effectiveAmount:N0} has been requested for Order #{shortId} ({order.CustomerName}).";

        var blocks = new List<object>
        {
            new { type = "text", text = text },
            new
            {
                type = "payment",
                amount = effectiveAmount,
                status = "requested",
                url = checkoutUrl
            }
        };

        await PublishAgentMessageAsync(conversation, blocks, cancellationToken);
    }

    public async Task NotifyOrderRejectedAsync(
        Guid organizationId,
        Guid orderId,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, organizationId, cancellationToken);
        if (order is null)
        {
            _logger.LogWarning("Cannot notify rejection: Order {OrderId} not found.", orderId);
            return;
        }

        var conversation = await ResolveConversationAsync(organizationId, order, cancellationToken);
        if (conversation is null)
        {
            _logger.LogInformation("No salon conversation found for Order {OrderId} customer {CustomerId}.", orderId, order.CustomerId);
            return;
        }

        var shortId = order.Id.ToString("N")[..8];
        var reasonSuffix = string.IsNullOrWhiteSpace(reason) ? "." : $": {reason}.";
        var text = $"Order #{shortId} for {order.CustomerName} was rejected{reasonSuffix}";

        var blocks = new object[]
        {
            new { type = "text", text = text }
        };

        await PublishAgentMessageAsync(conversation, blocks, cancellationToken);
    }

    private async Task<Conversation?> ResolveConversationAsync(
        Guid organizationId,
        Order order,
        CancellationToken cancellationToken)
    {
        // 1. Try finding conversation via approval queue entry
        var approval = await _approvalRepository.GetByOrderIdAsync(order.Id, organizationId, cancellationToken);
        if (approval?.ConversationId is Guid convId && convId != Guid.Empty)
        {
            var matched = await _conversations.GetAsync(organizationId, convId, cancellationToken);
            if (matched is not null)
            {
                return matched;
            }
        }

        // 2. Fall back to finding Salon bound to this customer
        return await _db.Conversations.FirstOrDefaultAsync(
            c => c.OrganizationId == organizationId
                 && c.CustomerId == order.CustomerId
                 && c.Kind == ConversationKind.Salon,
            cancellationToken);
    }

    private async Task PublishAgentMessageAsync(
        Conversation conversation,
        object blocks,
        CancellationToken cancellationToken)
    {
        var message = new Message
        {
            ConversationId = conversation.Id,
            AuthorKind = AuthorKind.Agent,
            AuthorAgentKey = AgentKeys.Lina,
            Kind = MessageKind.Note,
            ContentBlocksJson = JsonSerializer.Serialize(blocks),
            Status = MessageStatus.Published
        };

        await _messages.SaveAsync(message, cancellationToken);

        conversation.LastMessageAt = DateTime.UtcNow;
        await _conversations.SaveAsync(conversation, cancellationToken);

        // Broadcast to SignalR salon group
        var messageDto = MessageDto.From(message);
        await _broadcaster.BroadcastMessageAsync(messageDto, cancellationToken);

        // Update inbox tile
        var row = await _conversations.GetRowAsync(conversation.Id, cancellationToken);
        if (row is not null)
        {
            var tile = new ConversationTile(
                ConversationTileMapper.ToDto(row),
                row.Conversation.OrganizationId,
                row.Conversation.OwnerUserId);
            await _broadcaster.BroadcastConversationChangedAsync(tile, cancellationToken);
        }

        _logger.LogInformation(
            "Broadcasted Lina salon update for conversation {ConversationId}, message {MessageId}.",
            conversation.Id,
            message.Id);
    }
}
