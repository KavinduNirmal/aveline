using Aveline.Api.Modules.Commerce.DTOs;

namespace Aveline.Api.Modules.Commerce.Services;

public interface IBusinessRulesService
{
    Task<IReadOnlyList<BusinessRuleResponseDto>> GetAllRulesAsync(
        Guid organizationId,
        bool activeOnly = true,
        CancellationToken cancellationToken = default);

    Task<BusinessRuleResponseDto?> GetRuleByIdAsync(
        Guid id,
        Guid organizationId,
        CancellationToken cancellationToken = default);

    Task<BusinessRuleResponseDto> CreateRuleAsync(
        Guid organizationId,
        CreateBusinessRuleDto dto,
        CancellationToken cancellationToken = default);

    Task<BusinessRuleResponseDto?> UpdateRuleAsync(
        Guid id,
        Guid organizationId,
        UpdateBusinessRuleDto dto,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteRuleAsync(
        Guid id,
        Guid organizationId,
        CancellationToken cancellationToken = default);

    Task<EvaluateOrderRulesResponseDto> EvaluateOrderRulesAsync(
        Guid organizationId,
        EvaluateOrderRulesRequestDto request,
        CancellationToken cancellationToken = default);
}
