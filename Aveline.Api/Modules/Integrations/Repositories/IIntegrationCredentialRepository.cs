using Aveline.Api.Modules.Integrations.Models;

namespace Aveline.Api.Modules.Integrations.Repositories;

/// <summary>
/// Persistence for tenant-scoped <see cref="IntegrationCredential"/> rows. Every method
/// takes an explicit <paramref name="organizationId"/> and filters by it, so credentials
/// are always isolated per boutique (tenant isolation).
/// </summary>
public interface IIntegrationCredentialRepository
{
    Task<IntegrationCredential?> GetAsync(
        Guid organizationId,
        IntegrationType type,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<IntegrationCredential>> ListByOrganizationAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default);

    /// <summary>Inserts, or updates the existing row for <c>(organizationId, type)</c>.</summary>
    Task UpsertAsync(
        Guid organizationId,
        IntegrationType type,
        string encryptedValue,
        string? metadata,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        Guid organizationId,
        IntegrationType type,
        CancellationToken cancellationToken = default);
}
