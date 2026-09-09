using System.Net.Http.Json;
using System.Text.Json;
using Aveline.Api.Infrastructure.Integrations;
using Aveline.Api.Modules.Conversations.DTOs;
using Aveline.Api.Modules.Conversations.Models;
using Aveline.Api.Modules.Conversations.Repositories;
using Microsoft.Extensions.Logging;

namespace Aveline.Api.Modules.Conversations.Services;

public class ConversationService : IConversationService
{
    private readonly IConversationRepository _conversations;
    private readonly IMessageRepository _messages;
    private readonly ISignOffDecisionRepository _signOffDecisions;
    private readonly IAgentServiceClient _agentClient;
    private readonly ILogger<ConversationService> _logger;

    public ConversationService(
        IConversationRepository conversations,
        IMessageRepository messages,
        ISignOffDecisionRepository signOffDecisions,
        IAgentServiceClient agentClient,
        ILogger<ConversationService> logger)
    {
        _conversations = conversations;
        _messages = messages;
        _signOffDecisions = signOffDecisions;
        _agentClient = agentClient;
        _logger = logger;
    }

    public async Task<ConversationDto> GetOrCreateSalonAsync(
        Guid orgId,
        Guid userId,
        Guid? customerId,
        CancellationToken cancellationToken = default)
    {
        var threadId = Guid.NewGuid().ToString("N");
        var (conversation, created) = await _conversations.GetOrCreateSalonAsync(orgId, customerId, threadId, cancellationToken);

        // A brand-new Salon gets a predefined Aveline greeting so the user always has a
        // warm first message (no LLM required).
        if (created)
        {
            await SeedAvelineGreetingAsync(conversation, customerId, cancellationToken);
        }

        return ConversationDto.From(conversation);
    }

    /// <summary>
    /// Inserts Aveline's predefined welcome message into a freshly created Salon.
    /// </summary>
    private async Task SeedAvelineGreetingAsync(
        Conversation conversation,
        Guid? customerId,
        CancellationToken cancellationToken)
    {
        var greeting = customerId is null
            ? "Welcome to your Salon. I'm Aveline, your boutique concierge. Ask me about a customer, a piece in your catalogue, or a price - and I'll bring in Ava, Elle, or Lina when they can help."
            : "Welcome. I'm Aveline, your boutique concierge. I'll help you look after this customer.";
        var message = new Message
        {
            ConversationId = conversation.Id,
            AuthorKind = AuthorKind.Agent,
            AuthorAgentKey = AgentKeys.Aveline,
            Kind = MessageKind.Note,
            ContentBlocksJson = JsonSerializer.Serialize(new[]
            {
                new { type = "text", text = greeting },
            }),
            Status = MessageStatus.Published,
        };
        await _messages.SaveAsync(message, cancellationToken);

        conversation.LastMessageAt = DateTime.UtcNow;
        await _conversations.SaveAsync(conversation, cancellationToken);
    }

    public async Task<ConversationDto?> GetAsync(
        Guid orgId,
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        var conversation = await _conversations.GetAsync(orgId, conversationId, cancellationToken);
        return conversation is null ? null : ConversationDto.From(conversation);
    }

    public async Task<(IReadOnlyList<ConversationDto> Items, int Total)> ListAsync(
        Guid orgId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var (items, total) = await _conversations.ListAsync(orgId, page, pageSize, cancellationToken);
        return (items.Select(ConversationDto.From).ToList(), total);
    }

    public async Task<(IReadOnlyList<MessageDto> Items, int Total)> ListMessagesAsync(
        Guid orgId,
        Guid conversationId,
        int page,
        int pageSize,
        Guid? around = null,
        CancellationToken cancellationToken = default)
    {
        var conversation = await _conversations.GetAsync(orgId, conversationId, cancellationToken);
        if (conversation is null)
        {
            throw new InvalidOperationException("Conversation not found in this organization.");
        }

        var (items, total) = await _messages.ListAsync(conversationId, page, pageSize, around, cancellationToken);
        return (items.Select(MessageDto.From).ToList(), total);
    }

    public async Task<MessageDto> SendStaffNoteAsync(
        Guid orgId,
        Guid userId,
        Guid conversationId,
        string text,
        CancellationToken cancellationToken = default)
    {
        var conversation = await _conversations.GetAsync(orgId, conversationId, cancellationToken)
            ?? throw new InvalidOperationException("Conversation not found in this organization.");

        var message = new Message
        {
            ConversationId = conversationId,
            AuthorKind = AuthorKind.User,
            AuthorUserId = userId,
            Kind = MessageKind.Note,
            ContentBlocksJson = JsonSerializer.Serialize(new[]
            {
                new { type = "text", text },
            }),
            Status = MessageStatus.Published,
        };
        await _messages.SaveAsync(message, cancellationToken);

        conversation.LastMessageAt = DateTime.UtcNow;
        await _conversations.SaveAsync(conversation, cancellationToken);

        await TriggerAgentAsync(conversation, text, cancellationToken);

        return MessageDto.From(message);
    }

    public async Task<MessageDto> ApplyAgentMessageAsync(
        AgentMessageEvent evt,
        CancellationToken cancellationToken = default)
    {
        var conversation = await _conversations.GetByThreadIdAsync(evt.ThreadId, cancellationToken)
            ?? throw new InvalidOperationException($"Conversation for thread {evt.ThreadId} not found.");
        if (conversation is null)
        {
            // The event may arrive before the conversation is known to this instance; the
            // caller decides whether to retry. Surface a clear error rather than silently
            // dropping the message.
            throw new InvalidOperationException($"Conversation {evt.ConversationId} not found.");
        }

        var contentBlocksJson = evt.ContentBlocks.ValueKind == JsonValueKind.Undefined
            ? "[]"
            : evt.ContentBlocks.GetRawText();

        var message = new Message
        {
            // The agent's message.created payload only carries a thread_id, so the
            // conversation is resolved above by thread id. Use its real id here rather
            // than evt.ConversationId (which is Guid.Empty for agent events).
            ConversationId = conversation.Id,
            AuthorKind = AuthorKind.Agent,
            AuthorAgentKey = evt.AgentKey,
            Kind = evt.Kind,
            ContentBlocksJson = contentBlocksJson,
            // Bind a SignOff to the exact payload the human will approve, so a later edit
            // to the message cannot change what was approved.
            ContentHash = evt.Kind == MessageKind.SignOff ? ContentHash.Compute(contentBlocksJson) : null,
            ReplyToMessageId = evt.ReplyToMessageId,
            WorkflowRunId = evt.WorkflowRunId,
            Status = MessageStatus.Published,
        };
        await _messages.SaveAsync(message, cancellationToken);

        conversation.LastMessageAt = DateTime.UtcNow;
        await _conversations.SaveAsync(conversation, cancellationToken);

        return MessageDto.From(message);
    }

    public async Task<MessageDto> DecideSignOffAsync(
        Guid orgId,
        Guid userId,
        Guid conversationId,
        Guid messageId,
        bool approved,
        string contentHash,
        CancellationToken cancellationToken = default)
    {
        var conversation = await _conversations.GetAsync(orgId, conversationId, cancellationToken)
            ?? throw new InvalidOperationException("Conversation not found in this organization.");

        var message = await _messages.GetAsync(conversationId, messageId, cancellationToken)
            ?? throw new InvalidOperationException("Message not found in this conversation.");

        if (message.Kind != MessageKind.SignOff)
        {
            throw new InvalidOperationException("Only a SignOff message can be decided.");
        }
        if (message.Status != MessageStatus.AwaitingSignOff)
        {
            throw new InvalidOperationException("This SignOff is not awaiting a decision.");
        }

        // Bind the decision to the exact payload the human saw. If the message content was
        // rewritten after display, the hash no longer matches and the approval is void.
        var currentHash = ContentHash.Compute(message.ContentBlocksJson);
        if (!string.Equals(currentHash, contentHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "This SignOff has changed since it was displayed. Please review the latest version before deciding.");
        }

        // Record the decision out of band (immutable, separate from the mutable message row)
        // so a later edit to the message cannot retroactively change what was approved.
        await _signOffDecisions.SaveAsync(new SignOffDecision
        {
            OrganizationId = orgId,
            ConversationId = conversationId,
            MessageId = messageId,
            ContentHash = currentHash,
            Approved = approved,
            DecidedBy = userId,
            DecidedAt = DateTime.UtcNow,
        }, cancellationToken);

        message.Status = approved ? MessageStatus.Published : MessageStatus.Cancelled;
        await _messages.SaveAsync(message, cancellationToken);

        conversation.Status = approved ? ConversationStatus.Active : ConversationStatus.Resolved;
        conversation.LastMessageAt = DateTime.UtcNow;
        await _conversations.SaveAsync(conversation, cancellationToken);

        _logger.LogInformation(
            "SignOff {MessageId} {Decision} by user {UserId} in conversation {ConversationId} (hash {ContentHash}).",
            messageId, approved ? "approved" : "rejected", userId, conversationId, currentHash);

        return MessageDto.From(message);
    }

    public async Task<MessageDto> RecordInboundClientMessageAsync(
        Guid orgId,
        string externalRef,
        string from,
        string text,
        CancellationToken cancellationToken = default)
    {
        var threadId = Guid.NewGuid().ToString("N");
        var conversation = await _conversations.GetOrCreateSalonByExternalRefAsync(orgId, externalRef, threadId, cancellationToken);

        var message = new Message
        {
            ConversationId = conversation.Id,
            AuthorKind = AuthorKind.System,
            Kind = MessageKind.ClientMessage,
            ContentBlocksJson = JsonSerializer.Serialize(new[]
            {
                new { type = "client_message", from, text },
            }),
            Status = MessageStatus.Published,
        };
        await _messages.SaveAsync(message, cancellationToken);

        conversation.LastMessageAt = DateTime.UtcNow;
        await _conversations.SaveAsync(conversation, cancellationToken);

        // Best-effort: ask the agent to draft a response to this inbound client message into
        // the Salon (ADR-016). The client's phone is forwarded so the memory agent can attempt
        // to identify the customer and personalize the draft. Replies arrive later as
        // message.created events.
        await TriggerInboundDraftAsync(conversation, from, text, cancellationToken);

        return MessageDto.From(message);
    }

    private async Task TriggerInboundDraftAsync(
        Conversation conversation,
        string from,
        string text,
        CancellationToken cancellationToken)
    {
        try
        {
            var payload = new
            {
                query = text,
                thread_id = conversation.ThreadId,
                org_context = new
                {
                    organization_id = conversation.OrganizationId,
                    phone_number = from,
                    channel = "whatsapp",
                    direction = "inbound",
                },
            };
            using var content = JsonContent.Create(payload);
            // Best-effort: a failure to reach the agent must not fail the webhook. The agent
            // replies arrive later as message.created events.
            var response = await _agentClient.PostAsync("/agents/query", content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Agent inbound draft returned {StatusCode} for conversation {ConversationId}.",
                    (int)response.StatusCode,
                    conversation.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to trigger inbound draft for conversation {ConversationId}.", conversation.Id);
        }
    }

    private async Task TriggerAgentAsync(Conversation conversation, string query, CancellationToken cancellationToken)
    {
        try
        {
            var payload = new
            {
                query,
                thread_id = conversation.ThreadId,
                org_context = new { organization_id = conversation.OrganizationId },
            };
            using var content = JsonContent.Create(payload);
            // Best-effort: a failure to reach the agent must not fail the staff note. The
            // agent replies arrive later as message.created events.
            var response = await _agentClient.PostAsync("/agents/query", content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Agent query returned {StatusCode} for conversation {ConversationId}.",
                    (int)response.StatusCode,
                    conversation.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to trigger agent for conversation {ConversationId}.", conversation.Id);
        }
    }
}
