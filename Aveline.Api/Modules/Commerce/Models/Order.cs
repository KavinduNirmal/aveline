using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Aveline.Api.Common.MultiTenancy;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Shared.Models;

namespace Aveline.Api.Modules.Commerce.Models;

[Table("Orders")]
public class Order : ITenantEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid OrganizationId { get; set; }

    [Required]
    public Guid CustomerId { get; set; }

    [Required]
    [MaxLength(100)]
    public string CustomerName { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string OrderType { get; set; } = "whatsapp"; // in_store, whatsapp, instagram, sourcing

    [Required]
    [MaxLength(50)]
    public string Status { get; set; } = "pending_hold"; 
    // pending_hold, payment_requested, payment_confirmed, payment_expired, 
    // pending_approval, approved, rejected, delivery_scheduled, delivered, completed, cancelled

    [Column(TypeName = "decimal(18,2)")]
    public decimal Subtotal { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Discount { get; set; } = 0.00m;

    [Column(TypeName = "decimal(18,2)")]
    public decimal Total { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal TotalCost { get; set; }

    [Column(TypeName = "decimal(5,4)")]
    public decimal Margin { get; set; } 

    public Guid? CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Navigation properties
    public Organization? Organization { get; set; }
    public User? CreatedByUser { get; set; }
    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
    public ICollection<ApprovalQueueEntry> Approvals { get; set; } = new List<ApprovalQueueEntry>();
    public DeliveryPlan? DeliveryPlan { get; set; }
}