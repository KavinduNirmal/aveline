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
        Guid orgId,
        AgentMessageEvent evt,
        CancellationToken cancellationToken = default);
}
