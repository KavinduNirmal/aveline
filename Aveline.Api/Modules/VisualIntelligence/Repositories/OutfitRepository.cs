using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.VisualIntelligence.Models;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.VisualIntelligence.Repositories;

public class OutfitRepository : IOutfitRepository
{
    private readonly AppDbContext _db;

    public OutfitRepository(AppDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task<OutfitComposition?> GetByIdAsync(Guid id, Guid orgId, CancellationToken cancellationToken = default)
    {
        return await _db.OutfitCompositions
            .Include(x => x.Items)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.OrgId == orgId, cancellationToken);
    }

    public async Task<IReadOnlyList<OutfitComposition>> GetByCustomerIdAsync(Guid customerId, Guid orgId, CancellationToken cancellationToken = default)
    {
        return await _db.OutfitCompositions
            .Include(x => x.Items)
            .AsNoTracking()
            .Where(x => x.OrgId == orgId && x.CustomerId == customerId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OutfitComposition>> GetByOrgIdAsync(Guid orgId, CancellationToken cancellationToken = default)
    {
        return await _db.OutfitCompositions
            .Include(x => x.Items)
            .AsNoTracking()
            .Where(x => x.OrgId == orgId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(OutfitComposition composition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(composition);
        await _db.OutfitCompositions.AddAsync(composition, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
