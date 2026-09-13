using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.ApiAccess.Models;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.ApiAccess.Repositories;

public class ApiKeyRepository : IApiKeyRepository
{
    private readonly AppDbContext _context;

    public ApiKeyRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(ApiKey key, CancellationToken cancellationToken = default)
    {
        _context.ApiKeys.Add(key);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<ApiKey?> GetByPrefixAsync(string prefix, CancellationToken cancellationToken = default) =>
        await _context.ApiKeys.FirstOrDefaultAsync(k => k.Prefix == prefix, cancellationToken);

    public async Task<ApiKey?> GetByIdAsync(
        Guid organizationId, Guid keyId, CancellationToken cancellationToken = default) =>
        await _context.ApiKeys
            .FirstOrDefaultAsync(k => k.Id == keyId && k.OrganizationId == organizationId, cancellationToken);

    public async Task<IReadOnlyList<ApiKey>> ListAsync(
        Guid organizationId, CancellationToken cancellationToken = default) =>
        await _context.ApiKeys
            .Where(k => k.OrganizationId == organizationId)
            .OrderByDescending(k => k.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task UpdateAsync(ApiKey key, CancellationToken cancellationToken = default)
    {
        _context.ApiKeys.Update(key);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(ApiKey key, CancellationToken cancellationToken = default)
    {
        _context.ApiKeys.Remove(key);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> RevokeAllForCreatorAsync(
        Guid createdByUserId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var keys = await _context.ApiKeys
            .Where(k => k.CreatedByUserId == createdByUserId && k.Status == ApiKeyStatus.Active)
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        foreach (var key in keys)
        {
            key.Status = ApiKeyStatus.Revoked;
            key.RevokedAt = now;
            key.RevokedReason = reason;
        }

        if (keys.Count > 0)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }

        return keys.Count;
    }
}
