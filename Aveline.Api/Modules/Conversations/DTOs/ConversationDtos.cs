using Aveline.Api.Modules.Conversations.Models;

namespace Aveline.Api.Modules.Conversations.DTOs;

/// <summary>
/// A conversation (Salon) as returned to clients. The list row carries enough to draw a tile:
/// the client's name, a block-aware preview of the newest message, the block it came from (the
/// row's category), who spoke last, the agent persona, and the actionable marker set.
/// </summary>
/// <remarks>
/// <see cref="Kind"/> keeps its declared meaning - the thread's nature and audience. Customer
/// context is the separate <see cref="CustomerId"/> axis, and both clients classify by it first.
/// <see cref="ExternalRef"/> lets a channel-created thread whose customer is not yet identified
/// render as an unnamed client rather than being mistaken for the general concierge.
/// </remarks>
public sealed record ConversationDto(
    Guid Id,
    string Kind,
    Guid? CustomerId,
    string? CustomerName,
    string? ExternalRef,
    string ThreadId,
    string Status,
    DateTime? LastMessageAt,
    string? LastMessagePreview,
    string? LastMessageKind,
    string? LastMessageBlock,
    string? LastMessageAuthor,
    string? LastMessageAgentKey,
    IReadOnlyList<string> Markers)
{
    /// <summary>
    /// The identity-only projection, for callers that hold a conversation without its joined row
    /// (the create, get-by-id and select-customer responses). The list endpoint and the realtime
    /// broadcast build the full row through <c>ConversationTileMapper</c>.
    /// </summary>
    public static ConversationDto From(Conversation conversation) => new(
        conversation.Id,
        conversation.Kind.ToString(),
        conversation.CustomerId,
        null,
        conversation.ExternalRef,
        conversation.ThreadId,
        conversation.Status.ToString(),
        conversation.LastMessageAt,
        null,
        null,
        null,
        null,
        null,
        Array.Empty<string>());
}

/// <summary>A paginated page of conversations.</summary>
public sealed record ConversationPage(
    IReadOnlyList<ConversationDto> Items,
    int Total,
    int Page,
    int PageSize);

/// <summary>Request to get-or-create a Salon for an optional customer.</summary>
public sealed record CreateConversationRequest(Guid? CustomerId);

/// <summary>
/// Request to bind a Salon to a customer chosen from a resolution <c>choice</c> block
/// (Issue #161). <see cref="Query"/> is the original staff text that triggered the lookup, so
/// the agent can re-run against the resolved customer.
/// </summary>
public sealed record SelectCustomerRequest(Guid CustomerId, string? Query);
