using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Aveline.Api.Common.MultiTenancy;
using Aveline.Api.Modules.Organizations.Models;

namespace Aveline.Api.Modules.Commerce.Models;

[Table("Business_Rules")]
public class BusinessRule : ITenantEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid OrganizationId { get; set; }

    [Required]
    [MaxLength(100)]
    public string RuleName { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string RuleType { get; set; } = string.Empty; // discount, approval_threshold, loyalty_tier, min_margin

    [Required]
    [Column(TypeName = "jsonb")]
    public string RuleValue { get; set; } = "{}"; // JSON string storing dynamic threshold config

    public bool IsActive { get; set; } = true;

    [MaxLength(500)]
    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Navigation property
    public Organization? Organization { get; set; }
}