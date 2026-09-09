using Aveline.Api.Modules.Conversations.Models;

namespace Aveline.Api.Modules.Conversations.Repositories;

public interface IMessageRepository
{
    Task<Message?> GetAsync(Guid conversationId, Guid messageId, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<Message> Items, int Total)> ListAsync(
        Guid conversationId,
        int page,
        int pageSize,
        Guid? around = null,
        CancellationToken cancellationToken = default);

    Task SaveAsync(Message message, CancellationToken cancellationToken = default);
}
