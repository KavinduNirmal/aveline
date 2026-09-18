using System.Text.Json;
using Aveline.Api.Modules.Conversations.Models;

namespace Aveline.Api.Modules.Conversations.DTOs;

/// <summary>A message as returned to clients. <see cref="ContentBlocks"/> is the parsed
/// JSON block array; <see cref="ContentHash"/> binds a SignOff decision to the exact payload.</summary>
public sealed record MessageDto(
    Guid Id,
    Guid ConversationId,
    string AuthorKind,
    string? AgentKey,
    Guid? AuthorUserId,
    MessageKind Kind,
    JsonElement ContentBlocks,
    string? ContentHash,
    Guid? ReplyToMessageId,
    MessageStatus Status,
    DateTime CreatedAt)
{
    public static MessageDto From(Message message)
    {
        JsonElement blocks = default;
        if (!string.IsNullOrWhiteSpace(message.ContentBlocksJson))
        {
            try
            {
                blocks = JsonSerializer.Deserialize<JsonElement>(message.ContentBlocksJson);
            }
            catch (JsonException)
            {
                blocks = default;
            }
        }

        return new MessageDto(
            message.Id,
            message.ConversationId,
            message.AuthorKind.ToString(),
            message.AuthorAgentKey,
            message.AuthorUserId,
            message.Kind,
            blocks,
            message.ContentHash,
            message.ReplyToMessageId,
            message.Status,
            message.CreatedAt);
    }
}

/// <summary>A paginated page of messages.</summary>
public sealed record MessagePage(
    IReadOnlyList<MessageDto> Items,
    int Total,
    int Page,
    int PageSize);

/// <summary>Request to send a staff note (and trigger the agent).</summary>
public sealed record SendMessageRequest(string Text);

/// <summary>
/// Request to decide a human-in-the-loop SignOff message. <see cref="ContentHash"/> is the
/// canonical hash of the content blocks the human saw; the server rejects the decision if the
/// message content no longer hashes to this value (i.e. it changed after display).
/// </summary>
public sealed record SignOffDecisionRequest(bool Approved, string ContentHash);
