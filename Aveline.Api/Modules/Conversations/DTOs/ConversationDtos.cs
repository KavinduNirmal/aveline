using Aveline.Api.Modules.Conversations.Models;

namespace Aveline.Api.Modules.Conversations.DTOs;

/// <summary>A conversation (Salon) as returned to clients.</summary>
public sealed record ConversationDto(
    Guid Id,
    string Kind,
    Guid? CustomerId,
    string ThreadId,
    string Status,
    DateTime? LastMessageAt)
{
    public static ConversationDto From(Conversation conversation) => new(
        conversation.Id,
        conversation.Kind.ToString(),
        conversation.CustomerId,
        conversation.ThreadId,
        conversation.Status.ToString(),
        conversation.LastMessageAt);
}

/// <summary>A paginated page of conversations.</summary>
public sealed record ConversationPage(
    IReadOnlyList<ConversationDto> Items,
    int Total,
    int Page,
    int PageSize);

/// <summary>Request to get-or-create a Salon for an optional customer.</summary>
public sealed record CreateConversationRequest(Guid? CustomerId);
