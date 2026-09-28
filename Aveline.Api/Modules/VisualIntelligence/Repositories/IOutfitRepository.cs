using Aveline.Api.Modules.VisualIntelligence.Models;

namespace Aveline.Api.Modules.VisualIntelligence.Repositories;

public interface IOutfitRepository
{
    Task<OutfitComposition?> GetByIdAsync(Guid id, Guid orgId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The same row as <see cref="GetByIdAsync"/>, but **tracked**: an edit needs the change tracker
    /// to see the mutation, and the read method above is deliberately <c>AsNoTracking</c> so a list
    /// read cannot accidentally persist a change nobody asked for.
    /// </summary>
    Task<OutfitComposition?> GetTrackedByIdAsync(Guid id, Guid orgId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OutfitComposition>> GetByCustomerIdAsync(Guid customerId, Guid orgId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OutfitComposition>> GetByOrgIdAsync(Guid orgId, CancellationToken cancellationToken = default);
    Task AddAsync(OutfitComposition composition, CancellationToken cancellationToken = default);

    /// <summary>Persists a tracked composition's changed metadata.</summary>
    Task UpdateAsync(OutfitComposition composition, CancellationToken cancellationToken = default);

    /// <summary>
    /// Hard-deletes the composition. Its <c>OutfitItems</c> cascade with it (the FK is configured
    /// <c>OnDelete(Cascade)</c>), which is what "removed from the lookbook" means: the composition
    /// is not append-only and carries no money, unlike the takings journal.
    /// </summary>
    Task DeleteAsync(OutfitComposition composition, CancellationToken cancellationToken = default);
}
