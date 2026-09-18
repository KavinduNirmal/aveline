using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Notifications.Models;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.Notifications.Repositories;

public class DeviceTokenRepository : IDeviceTokenRepository
{
    private readonly AppDbContext _context;

    public DeviceTokenRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task UpsertAsync(UserDeviceToken token, CancellationToken cancellationToken = default)
    {
        var existing = await _context.UserDeviceTokens
            .FirstOrDefaultAsync(t => t.Token == token.Token, cancellationToken);

        if (existing is null)
        {
            token.CreatedAt = DateTime.UtcNow;
            token.LastSeenAt = DateTime.UtcNow;
            token.IsActive = true;
            _context.UserDeviceTokens.Add(token);
        }
        else
        {
            existing.UserId = token.UserId;
            existing.Platform = token.Platform;
            existing.LastSeenAt = DateTime.UtcNow;
            existing.IsActive = true;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeactivateAsync(Guid userId, string token, CancellationToken cancellationToken = default)
    {
        var existing = await _context.UserDeviceTokens
            .FirstOrDefaultAsync(t => t.UserId == userId && t.Token == token, cancellationToken);

        if (existing is not null)
        {
            existing.IsActive = false;
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<IReadOnlyList<string>> ListActiveTokensAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _context.UserDeviceTokens
            .AsNoTracking()
            .Where(t => t.UserId == userId && t.IsActive)
            .Select(t => t.Token)
            .ToListAsync(cancellationToken);
    }
}
