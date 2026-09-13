using Aveline.Api.Modules.VisualIntelligence.Models;

namespace Aveline.Api.Modules.VisualIntelligence.Repositories;

public interface ISourcingRequestRepository
{
    Task<SourcingRequest?> GetByIdAsync(Guid id, Guid orgId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SourcingRequest>> GetByOrgIdAsync(Guid orgId, string? status = null, CancellationToken cancellationToken = default);
    Task AddAsync(SourcingRequest request, CancellationToken cancellationToken = default);
    Task UpdateAsync(SourcingRequest request, CancellationToken cancellationToken = default);
}
