using Aveline.Api.Modules.Conversations.Models;

namespace Aveline.Api.Modules.Conversations.Repositories;

/// <summary>
/// One inbox row: a conversation plus the fields the list DTO joins onto it - the client's
/// name, the newest message, and whether the thread holds a SignOff awaiting a decision.
/// </summary>
/// <remarks>
/// The list query is the only place these are loaded, because the row has to be drawn from a
/// single round trip rather than one request per conversation.
/// </remarks>
public sealed record ConversationListRow(
    Conversation Conversation,
    string? CustomerName,
    Message? LastMessage,
    bool HasPendingSignOff);
