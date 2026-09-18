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
    /// plus that user's own general Salon (ADR-021).
    /// </summary>
    Task<(IReadOnlyList<Conversation> Items, int Total)> ListAsync(
        Guid orgId,
        Guid userId,
        int page,
        int pageSize,
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
    /// external ref. Used for inbound messages where no Aveline customer id is known yet.
    /// </summary>
    Task<Conversation> GetOrCreateSalonByExternalRefAsync(
        Guid orgId,
        string externalRef,
        string threadId,
        CancellationToken cancellationToken = default);

    Task SaveAsync(Conversation conversation, CancellationToken cancellationToken = default);
}
