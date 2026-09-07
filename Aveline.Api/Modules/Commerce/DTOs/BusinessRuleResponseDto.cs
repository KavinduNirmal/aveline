namespace Aveline.Api.Modules.Commerce.DTOs;

public record BusinessRuleResponseDto(
    Guid Id,
    string RuleName,
    string RuleType,
    string RuleValue,
    bool IsActive,
    string? Description,
    DateTime CreatedAt,
    DateTime? UpdatedAt
);