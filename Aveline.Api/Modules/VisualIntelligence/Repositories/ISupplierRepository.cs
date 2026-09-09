using Aveline.Api.Modules.VisualIntelligence.Models;

namespace Aveline.Api.Modules.VisualIntelligence.Repositories;

public interface ISupplierRepository
{
    Task<Supplier?> GetByIdAsync(Guid id, Guid orgId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Supplier>> GetByOrgIdAsync(Guid orgId, CancellationToken cancellationToken = default);
    Task AddAsync(Supplier supplier, CancellationToken cancellationToken = default);
}
