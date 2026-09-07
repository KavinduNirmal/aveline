using System.ComponentModel.DataAnnotations;

namespace Aveline.Api.Modules.Commerce.DTOs;

public record UpdateBusinessRuleDto(
    [MaxLength(100)]
    string? RuleName,

    string? RuleValue,

    [MaxLength(500)]
    string? Description,

    bool? IsActive
);