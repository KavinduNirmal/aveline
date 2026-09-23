using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Aveline.Api.Common.MultiTenancy;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Shared.Models;

namespace Aveline.Api.Modules.Commerce.Models;

[Table("ApprovalQueue")]
public class ApprovalQueueEntry : ITenantEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid OrganizationId { get; set; }

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

    /// <summary>
    /// LangGraph checkpoint thread id for resuming the paused workflow (ADR-016).
    /// </summary>
    /// <remarks>
    /// Required, because a pause is actionable only if it is linked (ADR-024, Decision 4): the resume
    /// silently skipped a row whose thread was null, so the owner's decision was recorded and never
    /// delivered. A staff order created outside any conversation still gets a generated value - it
    /// names no checkpoint, and <see cref="ConversationId"/> being null is what marks it as such.
    /// </remarks>
    [Required]
    [MaxLength(64)]
    public string ThreadId { get; set; } = string.Empty;

    /// <summary>The Salon conversation this approval surfaces in (ADR-016).</summary>
    public Guid? ConversationId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DecidedAt { get; set; }

    // Navigation property
    // Navigation properties
    public Organization? Organization { get; set; }
    public User? DecidedByUser { get; set; }
    [ForeignKey(nameof(OrderId))]
    public Order Order { get; set; } = null!;
}