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
    private readonly IAgentServiceClient _agentClient;
    private readonly ILogger<ConversationService> _logger;

    public ConversationService(
        IConversationRepository conversations,
        IMessageRepository messages,
        IAgentServiceClient agentClient,
        ILogger<ConversationService> logger)
    {
        _conversations = conversations;
        _messages = messages;
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
        var conversation = await _conversations.GetOrCreateSalonAsync(orgId, customerId, threadId, cancellationToken);
        return ConversationDto.From(conversation);
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
        Guid orgId,
        AgentMessageEvent evt,
        CancellationToken cancellationToken = default)
    {
        var conversation = await _conversations.GetAsync(orgId, evt.ConversationId, cancellationToken);
        if (conversation is null)
        {
            // The event may arrive before the conversation is known to this instance; the
            // caller decides whether to retry. Surface a clear error rather than silently
            // dropping the message.
            throw new InvalidOperationException($"Conversation {evt.ConversationId} not found.");
        }

        var message = new Message
        {
            ConversationId = evt.ConversationId,
            AuthorKind = AuthorKind.Agent,
            AuthorAgentKey = evt.AgentKey,
            Kind = evt.Kind,
            ContentBlocksJson = evt.ContentBlocks.ValueKind == JsonValueKind.Undefined
                ? "[]"
                : evt.ContentBlocks.GetRawText(),
            ReplyToMessageId = evt.ReplyToMessageId,
            WorkflowRunId = evt.WorkflowRunId,
            Status = MessageStatus.Published,
        };
        await _messages.SaveAsync(message, cancellationToken);

        conversation.LastMessageAt = DateTime.UtcNow;
        await _conversations.SaveAsync(conversation, cancellationToken);

        return MessageDto.From(message);
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
