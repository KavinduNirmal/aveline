using System.Text.Json;
using Aveline.Api.Modules.Conversations.Models;

namespace Aveline.Api.Modules.Conversations.DTOs;

/// <summary>A message as returned to clients. <see cref="ContentBlocks"/> is the parsed
/// JSON block array.</summary>
public sealed record MessageDto(
    Guid Id,
    Guid ConversationId,
    string AuthorKind,
    string? AgentKey,
    Guid? AuthorUserId,
    MessageKind Kind,
    JsonElement ContentBlocks,
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
