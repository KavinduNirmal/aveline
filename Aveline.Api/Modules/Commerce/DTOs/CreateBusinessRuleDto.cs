using System.ComponentModel.DataAnnotations;

namespace Aveline.Api.Modules.Commerce.DTOs;

public record CreateBusinessRuleDto(
    [Required]
    [MaxLength(100)]
    string RuleName,

    [Required]
    [MaxLength(50)]
    string RuleType, // discount, approval_threshold, loyalty_tier, min_margin

    [Required]
    string RuleValue, // JSON payload e.g. {"threshold": 40000}

    [MaxLength(500)]
    string? Description,

    bool IsActive = true
);