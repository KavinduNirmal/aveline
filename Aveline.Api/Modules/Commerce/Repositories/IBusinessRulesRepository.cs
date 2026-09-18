using Aveline.Api.Modules.Commerce.Models;

namespace Aveline.Api.Modules.Commerce.Repositories;

public interface IBusinessRulesRepository
{
    Task<IReadOnlyList<BusinessRule>> GetAllAsync(
        Guid organizationId,
        bool activeOnly = true,
        CancellationToken cancellationToken = default);

    Task<BusinessRule?> GetByIdAsync(
        Guid id,
        Guid organizationId,
        CancellationToken cancellationToken = default);

    Task<BusinessRule?> GetByTypeAsync(
        string ruleType,
        Guid organizationId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BusinessRule>> GetByTypesAsync(
        IEnumerable<string> ruleTypes,
        Guid organizationId,
        CancellationToken cancellationToken = default);

    Task<BusinessRule> CreateAsync(
        BusinessRule rule,
        CancellationToken cancellationToken = default);

    Task<BusinessRule> UpdateAsync(
        BusinessRule rule,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        Guid id,
        Guid organizationId,
        CancellationToken cancellationToken = default);
}
