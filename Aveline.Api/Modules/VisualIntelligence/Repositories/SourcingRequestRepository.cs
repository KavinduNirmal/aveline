using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.VisualIntelligence.Models;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.VisualIntelligence.Repositories;

public class SourcingRequestRepository : ISourcingRequestRepository
{
    private readonly AppDbContext _db;

    public SourcingRequestRepository(AppDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task<SourcingRequest?> GetByIdAsync(Guid id, Guid orgId, CancellationToken cancellationToken = default)
    {
        return await _db.SourcingRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.OrgId == orgId, cancellationToken);
    }

    public async Task<IReadOnlyList<SourcingRequest>> GetByOrgIdAsync(Guid orgId, string? status = null, CancellationToken cancellationToken = default)
    {
        var query = _db.SourcingRequests
            .AsNoTracking()
            .Where(x => x.OrgId == orgId);

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(x => x.Status == status);
        }

        return await query.OrderByDescending(x => x.CreatedAtUtc).ToListAsync(cancellationToken);
    }

    public async Task AddAsync(SourcingRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _db.SourcingRequests.AddAsync(request, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(SourcingRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.UpdatedAtUtc = DateTime.UtcNow;
        _db.SourcingRequests.Update(request);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
