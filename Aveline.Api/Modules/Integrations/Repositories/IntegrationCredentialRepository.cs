using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Integrations.Models;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.Integrations.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IIntegrationCredentialRepository"/>.
/// </summary>
public sealed class IntegrationCredentialRepository(AppDbContext db) : IIntegrationCredentialRepository
{
    /// <inheritdoc/>
    public async Task<IntegrationCredential?> GetAsync(
        Guid organizationId,
        IntegrationType type,
        CancellationToken cancellationToken = default)
    {
        return await db.IntegrationCredentials
            .FirstOrDefaultAsync(
                c => c.OrganizationId == organizationId && c.IntegrationType == type,
                cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<IntegrationCredential>> ListByOrganizationAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        return await db.IntegrationCredentials
            .Where(c => c.OrganizationId == organizationId)
            .OrderBy(c => c.IntegrationType)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task UpsertAsync(
        Guid organizationId,
        IntegrationType type,
        string encryptedValue,
        string? metadata,
        IntegrationStatus status,
        CancellationToken cancellationToken = default)
    {
        var existing = await GetAsync(organizationId, type, cancellationToken);
        var now = DateTime.UtcNow;

        if (existing is not null)
        {
            existing.EncryptedValue = encryptedValue;
            existing.Metadata = metadata;
            existing.Status = status;
            existing.LastError = null;
            existing.UpdatedAt = now;
            db.IntegrationCredentials.Update(existing);
        }
        else
        {
            db.IntegrationCredentials.Add(new IntegrationCredential
            {
                OrganizationId = organizationId,
                IntegrationType = type,
                EncryptedValue = encryptedValue,
                Metadata = metadata,
                Status = status,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task UpdateStatusAsync(
        Guid organizationId,
        IntegrationType type,
        IntegrationStatus status,
        string? lastError = null,
        CancellationToken cancellationToken = default)
    {
        var existing = await GetAsync(organizationId, type, cancellationToken);
        if (existing is null)
        {
            return;
        }

        existing.Status = status;
        existing.LastError = lastError;
        if (status == IntegrationStatus.Connected)
        {
            existing.LastConnectedAt = DateTime.UtcNow;
        }

        existing.UpdatedAt = DateTime.UtcNow;
        db.IntegrationCredentials.Update(existing);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteAsync(
        Guid organizationId,
        IntegrationType type,
        CancellationToken cancellationToken = default)
    {
        var existing = await GetAsync(organizationId, type, cancellationToken);
        if (existing is null)
        {
            return false;
        }

        db.IntegrationCredentials.Remove(existing);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
