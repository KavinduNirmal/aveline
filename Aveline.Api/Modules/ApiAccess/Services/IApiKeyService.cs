using Aveline.Api.Modules.ApiAccess.Models;

namespace Aveline.Api.Modules.ApiAccess.Services;

/// <summary>Draft describing a new API key.</summary>
public sealed record CreateApiKeyCommand(
    string Name,
    IReadOnlyList<string> Scopes,
    ApiKeyEnvironment Environment = ApiKeyEnvironment.Live,
    DateTime? ExpiresAt = null);

/// <summary>A newly created key together with its one-time plaintext secret.</summary>
public sealed record CreatedApiKey(ApiKey Key, string Plaintext);

public interface IApiKeyService
{
    /// <summary>
    /// Creates a key for an organization. The plaintext secret is returned exactly once
    /// (FR-3.10) and is never retrievable again.
    /// </summary>
    Task<CreatedApiKey> CreateAsync(
        Guid organizationId,
        Guid createdByUserId,
        CreateApiKeyCommand command,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ApiKey>> ListAsync(Guid organizationId, CancellationToken cancellationToken = default);

    Task<ApiKey> RevokeAsync(
        Guid organizationId,
        Guid keyId,
        Guid? revokedByUserId,
        string? reason,
        CancellationToken cancellationToken = default);

    /// <summary>Hard-deletes a never-used key; a key that served traffic must be revoked instead (FR-3.13).</summary>
    Task DeleteAsync(Guid organizationId, Guid keyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a presented secret to its key, or <c>null</c> when the key is unknown,
    /// revoked, expired or fails the constant-time hash comparison (BR-3.5).
    /// </summary>
    Task<ApiKey?> AuthenticateAsync(string presentedSecret, CancellationToken cancellationToken = default);
}
