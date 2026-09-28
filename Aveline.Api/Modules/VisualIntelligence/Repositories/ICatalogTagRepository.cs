using Aveline.Api.Modules.VisualIntelligence.Models;

namespace Aveline.Api.Modules.VisualIntelligence.Repositories;

public interface ICatalogTagRepository
{
    Task<IReadOnlyList<CatalogTag>> GetTagsByOrgAsync(Guid orgId, bool includeArchived = false, CancellationToken cancellationToken = default);
    Task<CatalogTag?> GetByIdAsync(Guid id, Guid orgId, CancellationToken cancellationToken = default);
    Task<CatalogTag?> GetBySlugAsync(Guid orgId, string slug, CancellationToken cancellationToken = default);
    Task AddAsync(CatalogTag tag, CancellationToken cancellationToken = default);
    Task UpdateAsync(CatalogTag tag, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, Guid orgId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CatalogTag>> GetTagsForItemAsync(Guid orgId, Guid itemId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> AssignTagsToItemAsync(Guid orgId, Guid itemId, IEnumerable<string> tagSlugsOrIds, CancellationToken cancellationToken = default);
    Task EnsureDefaultTagsAsync(Guid orgId, CancellationToken cancellationToken = default);
}
