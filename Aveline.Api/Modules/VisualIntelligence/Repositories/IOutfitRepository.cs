using Aveline.Api.Modules.VisualIntelligence.Models;

namespace Aveline.Api.Modules.VisualIntelligence.Repositories;

public interface IOutfitRepository
{
    Task<OutfitComposition?> GetByIdAsync(Guid id, Guid orgId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OutfitComposition>> GetByCustomerIdAsync(Guid customerId, Guid orgId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OutfitComposition>> GetByOrgIdAsync(Guid orgId, CancellationToken cancellationToken = default);
    Task AddAsync(OutfitComposition composition, CancellationToken cancellationToken = default);
}
