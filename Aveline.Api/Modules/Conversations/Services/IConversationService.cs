using System.Text.Json;
using Aveline.Api.Modules.Conversations.DTOs;
using Aveline.Api.Modules.Conversations.Models;

namespace Aveline.Api.Modules.Conversations.Services;

/// <summary>
/// An agent content event (from the Redis event bus, ADR-014) that the API persists and
/// broadcasts. The agent service never writes message rows directly; it emits these and the
/// API becomes the system of record.
/// </summary>
public sealed record AgentMessageEvent(
    Guid ConversationId,
    string ThreadId,
    string AgentKey,
    MessageKind Kind,
    JsonElement ContentBlocks,
    Guid? ReplyToMessageId,
    Guid? WorkflowRunId);

public interface IConversationService
{
    Task<ConversationDto> GetOrCreateSalonAsync(
        Guid orgId,
        Guid userId,
        Guid? customerId,
        CancellationToken cancellationToken = default);

    Task<ConversationDto?> GetAsync(
        Guid orgId,
        Guid conversationId,
        CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<ConversationDto> Items, int Total)> ListAsync(
        Guid orgId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<MessageDto> Items, int Total)> ListMessagesAsync(
        Guid orgId,
        Guid conversationId,
        int page,
        int pageSize,
        Guid? around = null,
        CancellationToken cancellationToken = default);

    Task<MessageDto> SendStaffNoteAsync(
        Guid orgId,
        Guid userId,
        Guid conversationId,
        string text,
        CancellationToken cancellationToken = default);

    Task<MessageDto> ApplyAgentMessageAsync(
        AgentMessageEvent evt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Decides a human-in-the-loop <see cref="MessageKind.SignOff"/> message: approves or
    /// rejects it, transitions the message and conversation status, and (in a full system)
    /// resumes the paused LangGraph workflow via the conversation's thread id.
    /// </summary>
    Task<MessageDto> DecideSignOffAsync(
        Guid orgId,
        Guid userId,
        Guid conversationId,
        Guid messageId,
        bool approved,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records an inbound customer message (e.g. WhatsApp) as a <see cref="MessageKind.ClientMessage"/>
    /// in the customer's Salon, creating the Salon by external channel reference when needed.
    /// </summary>
    Task<MessageDto> RecordInboundClientMessageAsync(
        Guid orgId,
        string externalRef,
        string from,
        string text,
        CancellationToken cancellationToken = default);
}
