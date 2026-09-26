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

    /// <summary>
    /// Ensures the organization-shared Salon for one client exists, seeding Aveline's greeting when
    /// it has to create it. Returns <c>true</c> when this call created the Salon.
    ///
    /// A client is only visible in the Salon list once its thread exists, so a client created
    /// without one is missing from the concierge view until somebody opens it by hand. Ownership is
    /// deliberately not a parameter: a client-bound Salon is organization-shared
    /// (<c>OwnerUserId IS NULL</c>, ADR-021), so a staff member's id must never be bound to it.
    /// </summary>
    Task<bool> EnsureCustomerSalonAsync(
        Guid orgId,
        Guid customerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches a conversation the caller may see. Returns <c>null</c> when it does not exist in
    /// the organization or belongs to another user (ADR-021).
    /// </summary>
    Task<ConversationDto?> GetAsync(
        Guid orgId,
        Guid userId,
        Guid conversationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the conversations visible to the caller: organization-shared Salons plus the
    /// caller's own general Salon (ADR-021).
    /// </summary>
    Task<(IReadOnlyList<ConversationDto> Items, int Total)> ListAsync(
        Guid orgId,
        Guid userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds the inbox tile for a conversation, with the routing context the realtime broadcast
    /// needs (the org, and the owner when the thread is a per-user general Salon). Returns
    /// <c>null</c> when the conversation is unknown. Derived exactly like the list row, so a tile
    /// that arrives over the hub equals the one a re-read returns.
    /// </summary>
    Task<ConversationTile?> GetTileAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Binds a Salon to a customer chosen from a resolution <c>choice</c> block (Issue #161)
    /// and re-triggers the agent with that customer in context. The Salon becomes
    /// organization-shared. Returns <c>null</c> when the conversation does not exist in the org
    /// or belongs to another user (ADR-021).
    /// </summary>
    Task<ConversationDto?> SelectCustomerAsync(
        Guid orgId,
        Guid userId,
        Guid conversationId,
        Guid customerId,
        string? query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// One page of a conversation's messages, oldest first. <c>Page</c> is the page actually
    /// served: a deep-linked <paramref name="around"/> changes which page that is, and echoing it
    /// is what lets a client compute <c>hasEarlier</c> from the response alone.
    /// </summary>
    Task<(IReadOnlyList<MessageDto> Items, int Total, int Page)> ListMessagesAsync(
        Guid orgId,
        Guid userId,
        Guid conversationId,
        int page,
        int pageSize,
        Guid? around = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-runs the agent for the turn that produced <paramref name="messageId"/>.
    /// </summary>
    /// <remarks>
    /// Regeneration re-runs the <em>question</em>, not the answer: the staff message the block
    /// replied to is resolved and triggered again with the thread's customer context. The fresh
    /// content arrives the way every other agent reply does — as message events applied by the
    /// subscriber and broadcast over the Salon hub — so this returns only whether the run was
    /// started. It writes no message of its own and does not touch the superseded block, whose
    /// stored row is immutable history a later read would return anyway.
    /// </remarks>
    /// <returns><c>false</c> when the conversation is not visible or the message is not in it.</returns>
    Task<bool> RegenerateAsync(
        Guid orgId,
        Guid userId,
        Guid conversationId,
        Guid messageId,
        CancellationToken cancellationToken = default);

    Task<MessageDto> SendStaffNoteAsync(
        Guid orgId,
        Guid userId,
        Guid conversationId,
        string text,
        Guid? clientMessageId = null,
        IReadOnlyList<Guid>? attachmentIds = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// A bounded, oldest-first transcript window for an internal (service-to-service) caller
    /// (ADR-023, W1.1).
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="ListMessagesAsync"/> on purpose: that path is scoped to a staff
    /// user's visibility, whereas the agent service is not a user and must be scoped by
    /// organization alone. The tenant check is therefore the <paramref name="orgId"/> match on
    /// the conversation, not a visibility rule.
    /// </remarks>
    /// <returns><c>null</c> when the conversation does not exist in <paramref name="orgId"/>.</returns>
    Task<ConversationHistoryDto?> GetHistoryAsync(
        Guid orgId,
        Guid conversationId,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores an uploaded attachment against a conversation, unbound. Returns <c>null</c> when
    /// the conversation is not visible to the caller. The caller has already applied the type
    /// and size policy; the service owns storage.
    /// </summary>
    Task<Models.MessageAttachment?> CreateAttachmentAsync(
        Guid orgId,
        Guid userId,
        Guid conversationId,
        byte[] bytes,
        string contentType,
        string fileName,
        int? width,
        int? height,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// An attachment the caller may read, or <c>null</c> when the conversation is not visible or
    /// the row is not in it. Visibility is the conversation's, not the row's.
    /// </summary>
    Task<Models.MessageAttachment?> GetAttachmentAsync(
        Guid orgId,
        Guid userId,
        Guid conversationId,
        Guid attachmentId,
        CancellationToken cancellationToken = default);

    /// <summary>The stored bytes behind an attachment, through the store boundary.</summary>
    Task<Stream?> OpenAttachmentAsync(
        Models.MessageAttachment attachment,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Advances the caller's read marker for one conversation to
    /// <paramref name="lastReadMessageId"/>.
    ///
    /// The write is monotonic — it never moves the marker backwards — and an absent row means
    /// nothing has been read. A position the caller may not see is
    /// <see cref="MarkReadOutcome.ConversationNotFound"/>; a message from another conversation
    /// is <see cref="MarkReadOutcome.MessageNotFound"/>.
    /// </summary>
    Task<MarkReadOutcome> MarkReadAsync(
        Guid orgId,
        Guid userId,
        Guid conversationId,
        Guid lastReadMessageId,
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
    /// Revokes an approved SignOff, returning it to the associate's queue.
    ///
    /// Appends a <see cref="SignOffDecisionKind.Revoked"/> row to the immutable decision log
    /// (the original approval is untouched), sets the message and the conversation back to
    /// <see cref="MessageStatus.AwaitingSignOff"/>, and touches no workflow: ADR-018 has no
    /// LangGraph resume to undo. A SignOff whose newest decision is not an approval is
    /// refused, as is one that is not a SignOff at all.
    /// </summary>
    Task<MessageDto> RevokeSignOffAsync(
        Guid orgId,
        Guid userId,
        Guid conversationId,
        Guid messageId,
        string? reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records an inbound customer message (e.g. WhatsApp) as a <see cref="MessageKind.ClientMessage"/>
    /// in the customer's Salon, creating the Salon by external channel reference when needed.
    /// <paramref name="customerId"/> is the customer resolved from the channel handle at creation
    /// (the caller owns the lookup); when it is null the thread is created with only its
    /// <c>ExternalRef</c>, which is the rendered "not yet identified" state.
    /// </summary>
    Task<MessageDto> RecordInboundClientMessageAsync(
        Guid orgId,
        string externalRef,
        string from,
        string text,
        Guid? customerId,
        Guid? attachmentId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores an inbound channel attachment against the conversation for
    /// <paramref name="externalRef"/>, creating it when needed. No uploader is recorded: the
    /// bytes came from the customer's channel, not from a staff device.
    /// </summary>
    Task<Models.MessageAttachment> StoreInboundAttachmentAsync(
        Guid orgId,
        string externalRef,
        Guid? customerId,
        byte[] bytes,
        string contentType,
        string fileName,
        CancellationToken cancellationToken = default);
}
