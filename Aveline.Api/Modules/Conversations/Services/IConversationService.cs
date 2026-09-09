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

/// <summary>
/// An agent event revising an existing message (status and/or content blocks). Either
/// <see cref="Status"/> or <see cref="ContentBlocks"/> may be present; absent fields are left
/// unchanged on the persisted message.
/// </summary>
public sealed record AgentMessageUpdateEvent(
    Guid ConversationId,
    Guid MessageId,
    MessageStatus? Status,
    JsonElement ContentBlocks);

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

    /// <summary>
    /// Binds a Salon to a customer chosen from a resolution <c>choice</c> block (Issue #161)
    /// and re-triggers the agent with that customer in context. Returns <c>null</c> when the
    /// conversation does not exist in the org.
    /// </summary>
    Task<ConversationDto?> SelectCustomerAsync(
        Guid orgId,
        Guid conversationId,
        Guid customerId,
        string? query,
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
    /// Applies an agent revision to an existing message (status and/or content blocks) and
    /// returns the updated DTO, or <c>null</c> when the message does not exist.
    /// </summary>
    Task<MessageDto?> ApplyAgentMessageUpdateAsync(
        AgentMessageUpdateEvent evt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Decides a human-in-the-loop <see cref="MessageKind.SignOff"/> message: approves or
    /// rejects it, records the decision out of band bound to the exact content hash, and (in a
    /// full system) resumes the paused LangGraph workflow via the conversation's thread id.
    /// </summary>
    Task<MessageDto> DecideSignOffAsync(
        Guid orgId,
        Guid userId,
        Guid conversationId,
        Guid messageId,
        bool approved,
        string contentHash,
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
