using Aveline.Api.Modules.Conversations.Models;

namespace Aveline.Api.Modules.Conversations.Repositories;

public interface IMessageRepository
{
    Task<Message?> GetAsync(Guid conversationId, Guid messageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The message stored under a client-generated idempotency key, or <c>null</c> when the key
    /// has not been used in this conversation. This is what lets a retry land on the stored row.
    /// </summary>
    Task<Message?> GetByClientMessageIdAsync(
        Guid conversationId,
        Guid clientMessageId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// One page of a conversation's messages, oldest first. <c>Page</c> in the result is the page
    /// actually served, which is what a deep-linked <paramref name="around"/> changes.
    /// </summary>
    Task<(IReadOnlyList<Message> Items, int Total, int Page)> ListAsync(
        Guid conversationId,
        int page,
        int pageSize,
        Guid? around = null,
        CancellationToken cancellationToken = default);

    Task SaveAsync(Message message, CancellationToken cancellationToken = default);

    /// <summary>Persists changes to an existing message (loaded via <see cref="GetAsync"/>).</summary>
    Task UpdateAsync(Message message, CancellationToken cancellationToken = default);
}
