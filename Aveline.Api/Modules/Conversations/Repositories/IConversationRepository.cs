using Aveline.Api.Modules.Conversations.Models;

namespace Aveline.Api.Modules.Conversations.Repositories;

public interface IConversationRepository
{
    Task<Conversation?> GetAsync(Guid orgId, Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches a conversation the given user is allowed to see: organization-shared Salons
    /// (customer-bound and channel threads) plus that user's own general Salon. Returns
    /// <c>null</c> when the conversation belongs to another user (ADR-021).
    /// </summary>
    Task<Conversation?> GetVisibleToUserAsync(
        Guid orgId,
        Guid id,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>Finds a conversation by its LangGraph checkpoint thread id.</summary>
    Task<Conversation?> GetByThreadIdAsync(string threadId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the conversations visible to <paramref name="userId"/>: organization-shared Salons
    /// plus that user's own general Salon (ADR-021). Each row carries the client's name, the
    /// newest message and whether the thread holds a SignOff awaiting a decision, so the inbox
    /// can draw a tile from one read. Ordered newest-first with an id tiebreak, so paging is
    /// total and two conversations with equal timestamps cannot duplicate or skip.
    /// </summary>
    Task<(IReadOnlyList<ConversationListRow> Items, int Total)> ListAsync(
        Guid orgId,
        Guid userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The inbox tile row for one conversation, resolved by its globally unique id. Used by the
    /// realtime broadcast so the tile sent over the hub is derived exactly like the list's.
    /// </summary>
    Task<ConversationListRow?> GetRowAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the existing <see cref="ConversationKind.Salon"/> for an org + optional
    /// customer, or creates one with the given <paramref name="threadId"/>. The general Salon
    /// (<paramref name="customerId"/> is <c>null</c>) is scoped to <paramref name="userId"/>;
    /// customer-bound Salons stay organization-shared (ADR-021). The returned bool is
    /// <c>true</c> when a new Salon was created.
    /// </summary>
    Task<(Conversation Conversation, bool Created)> GetOrCreateSalonAsync(
        Guid orgId,
        Guid userId,
        Guid? customerId,
        string threadId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the existing <see cref="ConversationKind.Salon"/> for an org + external
    /// channel reference (e.g. a WhatsApp number), or creates one. Idempotent per org +
    /// external ref. <paramref name="customerId"/> is the customer resolved from the channel
    /// handle at creation; when it is known it is bound on the row, and an existing unbound
    /// thread is upgraded in place rather than being left context-less.
    /// </summary>
    Task<Conversation> GetOrCreateSalonByExternalRefAsync(
        Guid orgId,
        string externalRef,
        string threadId,
        Guid? customerId,
        CancellationToken cancellationToken = default);

    Task SaveAsync(Conversation conversation, CancellationToken cancellationToken = default);
}
