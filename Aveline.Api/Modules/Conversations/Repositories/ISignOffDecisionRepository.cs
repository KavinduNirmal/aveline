using Aveline.Api.Modules.Conversations.Models;

namespace Aveline.Api.Modules.Conversations.Repositories;

public interface ISignOffDecisionRepository
{
    /// <summary>Returns the most recent decision for a SignOff message, or null.</summary>
    Task<SignOffDecision?> GetByMessageIdAsync(Guid messageId, CancellationToken cancellationToken = default);

    Task SaveAsync(SignOffDecision decision, CancellationToken cancellationToken = default);
}
