using Aveline.Api.Modules.Conversations.Models;

namespace Aveline.Api.Modules.Conversations.Repositories;

public interface IConversationRepository
{
    Task<Conversation?> GetAsync(Guid orgId, Guid id, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<Conversation> Items, int Total)> ListAsync(
        Guid orgId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the existing <see cref="ConversationKind.Salon"/> for an org + optional
    /// customer, or creates one with the given <paramref name="threadId"/>. Idempotent per
    /// org + customer.
    /// </summary>
    Task<Conversation> GetOrCreateSalonAsync(
        Guid orgId,
        Guid? customerId,
        string threadId,
        CancellationToken cancellationToken = default);

    Task SaveAsync(Conversation conversation, CancellationToken cancellationToken = default);
}
