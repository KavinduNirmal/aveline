using Aveline.Api.Modules.Integrations.DTOs;
using Aveline.Api.Modules.Integrations.Models;

namespace Aveline.Api.Modules.Integrations.Services;

/// <summary>
/// Tenant-scoped credential management. Secrets are encrypted before persistence and
/// decrypted only for backend outbound use; callers and logs never receive plaintext.
/// </summary>
public interface IIntegrationService
{
    /// <summary>Validates, encrypts, and stores credentials for an integration.</summary>
    Task<IntegrationStatusDto> SaveAsync(
        Guid organizationId,
        IntegrationType type,
        SaveIntegrationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Returns a masked, non-secret view of all configured integrations.</summary>
    Task<IReadOnlyList<IntegrationStatusDto>> ListStatusAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Decrypts the stored credentials for backend outbound calls. Throws
    /// <see cref="IntegrationNotConfiguredException"/> when absent.
    /// </summary>
    Task<IDictionary<string, string>> GetCredentialsAsync(
        Guid organizationId,
        IntegrationType type,
        CancellationToken cancellationToken = default);

    /// <summary>Marks an integration as connected (records <c>LastConnectedAt</c>).</summary>
    Task<IntegrationStatusDto> MarkConnectedAsync(
        Guid organizationId,
        IntegrationType type,
        CancellationToken cancellationToken = default);

    /// <summary>Marks an integration as failed with a non-secret error message.</summary>
    Task<IntegrationStatusDto> MarkFailedAsync(
        Guid organizationId,
        IntegrationType type,
        string error,
        CancellationToken cancellationToken = default);

    /// <summary>Marks an integration as expired (e.g. provider token expired).</summary>
    Task<IntegrationStatusDto> MarkExpiredAsync(
        Guid organizationId,
        IntegrationType type,
        string error,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates the stored credentials against the provider and transitions the integration
    /// to <see cref="IntegrationStatus.Connected"/> or <see cref="IntegrationStatus.Error"/>.
    /// Only WhatsApp is validated today; other types are reported as connected without a live
    /// provider check.
    /// </summary>
    Task<IntegrationTestResultDto> TestConnectionAsync(
        Guid organizationId,
        IntegrationType type,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        Guid organizationId,
        IntegrationType type,
        CancellationToken cancellationToken = default);
}
