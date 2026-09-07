using System.ComponentModel.DataAnnotations;

namespace Aveline.Api.Modules.Commerce.DTOs;

public record EvaluateOrderRulesRequestDto(
    [Required]
    decimal OrderTotal,

    [Required]
    decimal Margin, // e.g. 0.30 for 30%

    decimal RequestedDiscount = 0.00m,

    string? CustomerTier = null // "VIP", "Regular", "New"
);

public record EvaluateOrderRulesResponseDto(
    bool RequiresApproval,
    bool IsAutoApproved,
    decimal MaxAllowedDiscount,
    decimal MinRequiredMargin,
    decimal HighValueThreshold,
    List<string> Flags,
    List<string> TriggeredRules
);