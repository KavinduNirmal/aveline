using System.ComponentModel.DataAnnotations;

namespace Aveline.Api.Modules.Commerce.DTOs;

public class ApprovalDecisionDto
{
    [Required]
    public string Decision { get; set; } = string.Empty; // "approve", "reject", "revise"

    [MaxLength(1000)]
    public string? Reason { get; set; }

    public decimal? RevisedDiscount { get; set; }
}
