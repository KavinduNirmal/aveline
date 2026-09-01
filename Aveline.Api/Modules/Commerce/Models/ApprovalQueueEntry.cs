using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Aveline.Api.Modules.Commerce.Models;

[Table("Approval_Queue")]
public class ApprovalQueueEntry
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid OrderId { get; set; }

    [Required]
    [MaxLength(50)]
    public string ApprovalType { get; set; } = string.Empty; // high_value_order, sourcing_request, discount, delivery, low_margin

    [Required]
    [MaxLength(50)]
    public string Status { get; set; } = "pending"; // pending, approved, rejected, revised

    public bool ThresholdExceeded { get; set; } = true;

    [MaxLength(500)]
    public string Reason { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? DecisionComment { get; set; }

    public Guid? DecidedBy { get; set; } // Owner / Manager User ID

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DecidedAt { get; set; }

    // Navigation property
    [ForeignKey(nameof(OrderId))]
    public Order Order { get; set; } = null!;
}
