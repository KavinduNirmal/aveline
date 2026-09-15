using Aveline.Api.Modules.ApiAccess.Models;

namespace Aveline.Api.Modules.ApiAccess.Repositories;

/// <summary>Persistence for API keys. Every query is tenant-scoped by the caller.</summary>
public interface IApiKeyRepository
{
    Task AddAsync(ApiKey key, CancellationToken cancellationToken = default);

    /// <summary>Resolves a key by its indexed prefix (the authentication hot path).</summary>
    Task<ApiKey?> GetByPrefixAsync(string prefix, CancellationToken cancellationToken = default);

    Task<ApiKey?> GetByIdAsync(Guid organizationId, Guid keyId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ApiKey>> ListAsync(Guid organizationId, CancellationToken cancellationToken = default);

    Task UpdateAsync(ApiKey key, CancellationToken cancellationToken = default);

    Task DeleteAsync(ApiKey key, CancellationToken cancellationToken = default);

    /// <summary>Revokes every active key created by a user (FR-3.5 account deletion).</summary>
    Task<int> RevokeAllForCreatorAsync(
        Guid createdByUserId,
        string reason,
        CancellationToken cancellationToken = default);
}
