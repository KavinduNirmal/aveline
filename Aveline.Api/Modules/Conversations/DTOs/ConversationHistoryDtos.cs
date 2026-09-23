namespace Aveline.Api.Modules.Conversations.DTOs;

/// <summary>
/// One turn of a conversation as the agent service reads it (ADR-023, W1.1).
/// </summary>
/// <remarks>
/// Deliberately not <see cref="MessageDto"/>: the agent needs a transcript, not the rich-content
/// envelope a client renders. <see cref="Text"/> is the human-readable content flattened out of
/// the stored blocks, and <see cref="AuthorKind"/>/<see cref="AgentKey"/> are what let a reader
/// tell a customer turn from an agent turn from a staff note — the distinction the concierge
/// workflow currently cannot make because it only ever receives the newest message.
///
/// <para>
/// Customer messages are stored as <c>client_message</c> blocks authored by
/// <c>AuthorKind.System</c> (customers are external and never appear as a sender), so the
/// author pair alone does not identify them; <see cref="Text"/> plus the block shape does.
/// </para>
/// </remarks>
public sealed record ConversationHistoryTurnDto(
    Guid Id,
    string AuthorKind,
    string? AgentKey,
    string Kind,
    string? Text,
    DateTime CreatedAt);

/// <summary>
/// A bounded, oldest-first transcript window for an internal caller (ADR-023, W1.1).
/// </summary>
/// <param name="ConversationId">The conversation the window belongs to.</param>
/// <param name="OrganizationId">The owning organization, echoed so the caller can assert tenancy.</param>
/// <param name="Items">The turns, oldest first. Fewer than requested when the conversation is short.</param>
public sealed record ConversationHistoryDto(
    Guid ConversationId,
    Guid OrganizationId,
    IReadOnlyList<ConversationHistoryTurnDto> Items);
