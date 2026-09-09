using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.VisualIntelligence.Models;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.VisualIntelligence.Repositories;

public class SupplierRepository : ISupplierRepository
{
    private readonly AppDbContext _db;

    public SupplierRepository(AppDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task<Supplier?> GetByIdAsync(Guid id, Guid orgId, CancellationToken cancellationToken = default)
    {
        return await _db.Suppliers
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.OrgId == orgId, cancellationToken);
    }

    public async Task<IReadOnlyList<Supplier>> GetByOrgIdAsync(Guid orgId, CancellationToken cancellationToken = default)
    {
        return await _db.Suppliers
            .AsNoTracking()
            .Where(x => x.OrgId == orgId && x.IsActive)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(Supplier supplier, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(supplier);
        await _db.Suppliers.AddAsync(supplier, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
