using System.Text.Json;
using Aveline.Api.Modules.Conversations.DTOs;
using Aveline.Api.Modules.Conversations.Models;
using Aveline.Api.Modules.Conversations.Repositories;
using Aveline.Api.Modules.CustomerConcierge.Repositories;
using Aveline.Api.Modules.Integrations.Models;
using Aveline.Api.Modules.Integrations.Services;
using Aveline.Api.Modules.Integrations.Services.Providers;
using Microsoft.Extensions.Logging;

namespace Aveline.Api.Modules.Conversations.Services;

/// <summary>
/// The outbound customer delivery path: resolve the thread's customer, find a channel the
/// boutique can actually send on, hand the words to the provider, and record what went out.
/// </summary>
/// <remarks>
/// <para>
/// The record is written **after** the provider accepts, never before. A row saying <c>Sent</c>
/// is a claim that a customer was messaged, and the console has no way to unsay it; writing the
/// row first would make a refused send look delivered in the very transcript an associate reads
/// to check. A provider success whose row then fails to write is logged and still reported as
/// delivered, because the customer does have the message.
/// </para>
/// <para>
/// The channel is resolved from what the tenant has connected rather than from a field on the
/// conversation, because conversations do not carry a channel today: a thread that arrived on a
/// channel keeps its handle in <c>ExternalRef</c>, and a thread opened in the console keeps it on
/// the customer's phone number. Both are phone-shaped, so both go out over WhatsApp. Instagram
/// has credentials in this platform but no provider and no webhook, so an Instagram-only boutique
/// is told exactly that instead of being handed a send that never happened.
/// </para>
/// </remarks>
public sealed class CustomerDeliveryService : ICustomerDeliveryService
{
    /// <summary>The channel's own name, as the client prints it.</summary>
    private const string WhatsAppChannel = "WhatsApp";

    private readonly IConversationRepository _conversations;
    private readonly IMessageRepository _messages;
    private readonly ICustomerRepository _customers;
    private readonly IIntegrationService _integrations;
    private readonly IWhatsAppService _whatsApp;
    private readonly IMessageBroadcaster _broadcaster;
    private readonly ILogger<CustomerDeliveryService> _logger;

    public CustomerDeliveryService(
        IConversationRepository conversations,
        IMessageRepository messages,
        ICustomerRepository customers,
        IIntegrationService integrations,
        IWhatsAppService whatsApp,
        IMessageBroadcaster broadcaster,
        ILogger<CustomerDeliveryService> logger)
    {
        _conversations = conversations;
        _messages = messages;
        _customers = customers;
        _integrations = integrations;
        _whatsApp = whatsApp;
        _broadcaster = broadcaster;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<DeliveryOutcome> DeliverAsync(
        Guid organizationId,
        Guid userId,
        Guid conversationId,
        string text,
        Guid? clientMessageId = null,
        CancellationToken cancellationToken = default)
    {
        // A blank body is a caller error, not a delivery state: the endpoint refuses it with a
        // `400` before reaching here, so this guard exists to keep the invariant explicit rather
        // than to invent a refusal code that no HTTP surface could produce.
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var body = text.Trim();

        var conversation = await _conversations.GetVisibleToUserAsync(
            organizationId, conversationId, userId, cancellationToken);
        if (conversation is null)
        {
            return DeliveryOutcome.Refused(
                DeliveryRefusal.ConversationNotFound, "Conversation not found.");
        }

        // A replay of a delivery that already went out is answered from the row it wrote. Its
        // status is the evidence: a `Sent` row is one the provider accepted, so re-sending would
        // put the same words in front of the customer twice.
        if (clientMessageId is not null)
        {
            var existing = await _messages.GetByClientMessageIdAsync(
                conversationId, clientMessageId.Value, cancellationToken);
            if (existing is not null)
            {
                return existing.Status is MessageStatus.Sent or MessageStatus.Delivered or MessageStatus.Read
                    ? DeliveryOutcome.Sent(WhatsAppChannel, null, MessageDto.From(existing))
                    : DeliveryOutcome.Refused(
                        DeliveryRefusal.ProviderRefused,
                        "That delivery was already attempted and did not go out.");
            }
        }

        if (conversation.CustomerId is null)
        {
            // An inbound thread whose number is not on file lands here: there is a handle, but no
            // customer to attach a delivery to, and inventing one would file the message against
            // the wrong record.
            return DeliveryOutcome.Refused(
                DeliveryRefusal.NoCustomer,
                "This thread isn't linked to a client yet.");
        }

        var customer = await _customers.GetAsync(
            organizationId, conversation.CustomerId.Value, cancellationToken);
        var handle = ResolveHandle(conversation.ExternalRef, customer?.PhoneNumber);
        if (handle is null)
        {
            return DeliveryOutcome.Refused(
                DeliveryRefusal.NoChannelHandle,
                "That client has no WhatsApp number on file.");
        }

        var channel = await ResolveChannelAsync(organizationId, cancellationToken);
        if (channel.Refusal is not null)
        {
            return DeliveryOutcome.Refused(channel.Refusal.Value, channel.Detail!);
        }

        var sent = await _whatsApp.SendMessageAsync(
            channel.AccessToken!, channel.PhoneNumberId!, handle, body, cancellationToken);
        if (!sent.IsSuccess)
        {
            // The provider's error is logged, never returned: it can echo the request, and the
            // associate only needs to know that nothing was sent.
            _logger.LogWarning(
                "WhatsApp delivery refused. organizationId={OrganizationId} conversationId={ConversationId} error={Error}",
                organizationId, conversationId, sent.Error);
            return DeliveryOutcome.Refused(
                DeliveryRefusal.ProviderRefused,
                "WhatsApp refused the message. Nothing was sent.");
        }

        var message = await RecordAsync(
            conversation, userId, body, clientMessageId, cancellationToken);

        _logger.LogInformation(
            "Delivered a block to a customer over WhatsApp. organizationId={OrganizationId} conversationId={ConversationId}",
            organizationId, conversationId);

        return DeliveryOutcome.Sent(WhatsAppChannel, sent.MessageId, message);
    }

    /// <summary>
    /// The handle a delivery can reach, preferring the thread's own channel reference.
    /// </summary>
    /// <remarks>
    /// The thread's reference is what the inbound webhook keyed the conversation by, so it is the
    /// address the customer actually answered from; a number edited on the customer record since
    /// then must not silently redirect the reply to a different handset.
    /// </remarks>
    private static string? ResolveHandle(string? externalRef, string? phoneNumber)
    {
        if (!string.IsNullOrWhiteSpace(externalRef)) return externalRef.Trim();
        if (!string.IsNullOrWhiteSpace(phoneNumber)) return phoneNumber.Trim();
        return null;
    }

    /// <summary>Which channel this tenant can currently send on, or the reason it cannot.</summary>
    private async Task<ChannelChoice> ResolveChannelAsync(
        Guid organizationId, CancellationToken cancellationToken)
    {
        try
        {
            var credentials = await _integrations.GetCredentialsAsync(
                organizationId, IntegrationType.WhatsApp, cancellationToken);
            credentials.TryGetValue("accessToken", out var accessToken);
            credentials.TryGetValue("phoneNumberId", out var phoneNumberId);
            if (!string.IsNullOrWhiteSpace(accessToken) && !string.IsNullOrWhiteSpace(phoneNumberId))
            {
                return new ChannelChoice(null, null, accessToken, phoneNumberId);
            }
        }
        catch (IntegrationNotConfiguredException)
        {
            // WhatsApp is absent; the Instagram check below decides which sentence the associate
            // gets, so this is not itself a failure.
        }

        if (await IsConfiguredAsync(organizationId, IntegrationType.Instagram, cancellationToken))
        {
            return new ChannelChoice(
                DeliveryRefusal.ChannelUnsupported,
                "Instagram messaging is not connected yet.",
                null, null);
        }

        return new ChannelChoice(
            DeliveryRefusal.ChannelNotConnected,
            "This boutique has not connected WhatsApp yet.",
            null, null);
    }

    /// <summary>Whether a tenant has usable credentials for a type, without revealing them.</summary>
    private async Task<bool> IsConfiguredAsync(
        Guid organizationId, IntegrationType type, CancellationToken cancellationToken)
    {
        try
        {
            await _integrations.GetCredentialsAsync(organizationId, type, cancellationToken);
            return true;
        }
        catch (IntegrationNotConfiguredException)
        {
            return false;
        }
    }

    /// <summary>
    /// Writes the delivery into the thread as a staff message the customer has received, or
    /// <c>null</c> when the write failed.
    /// </summary>
    /// <remarks>
    /// The status is <see cref="MessageStatus.Sent"/>, not <see cref="MessageStatus.Published"/>:
    /// that is the difference the transcript exists to show, and it is what tells both clients to
    /// draw the bubble as delivered rather than as a note that never left the shop.
    ///
    /// Every failure here is swallowed and logged, and the delivery is still reported as delivered,
    /// because the customer does have the message by this point. What is *not* done is inventing a
    /// row: the DTO is returned only once it is stored, so a caller can never be handed an id for
    /// something the transcript cannot show.
    /// </remarks>
    private async Task<MessageDto?> RecordAsync(
        Conversation conversation,
        Guid userId,
        string body,
        Guid? clientMessageId,
        CancellationToken cancellationToken)
    {
        var blocks = JsonSerializer.Serialize(new[] { new { type = "text", text = body } });

        var message = new Message
        {
            ConversationId = conversation.Id,
            AuthorKind = AuthorKind.User,
            AuthorUserId = userId,
            Kind = MessageKind.Note,
            ContentBlocksJson = blocks,
            ClientMessageId = clientMessageId,
            Status = MessageStatus.Sent,
        };

        try
        {
            await _messages.SaveAsync(message, cancellationToken);
            conversation.LastMessageAt = DateTime.UtcNow;
            await _conversations.SaveAsync(conversation, cancellationToken);
        }
        catch (Exception ex)
        {
            // The customer has the message either way; the record is what failed. Logged loudly
            // because the transcript and the customer's phone now disagree, and support has the
            // provider id to reconcile them.
            _logger.LogError(
                ex,
                "Delivered a customer message but could not record it. conversationId={ConversationId}",
                conversation.Id);
            return null;
        }

        var dto = MessageDto.From(message);
        await _broadcaster.BroadcastMessageAsync(dto, cancellationToken);
        return dto;
    }

    /// <summary>The channel decision: either a provider to send with, or the sentence explaining why not.</summary>
    private sealed record ChannelChoice(
        DeliveryRefusal? Refusal,
        string? Detail,
        string? AccessToken,
        string? PhoneNumberId);
}
