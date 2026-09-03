using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Aveline.Api.Modules.Commerce.Models;

[Table("Delivery_Plans")]
public class DeliveryPlan
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid OrderId { get; set; }

    [MaxLength(50)]
    public string? CourierService { get; set; } // Uber, PickMe, In-house

    [MaxLength(100)]
    public string? TrackingNumber { get; set; }

    [Required]
    [MaxLength(500)]
    public string DeliveryAddress { get; set; } = string.Empty;

    public DateTime? PreferredDeliveryTime { get; set; }

    [Column(TypeName = "jsonb")]
    public string? RouteOptimized { get; set; } // JSON string for waypoints/route

    [Column(TypeName = "decimal(18,2)")]
    public decimal? EstimatedCost { get; set; }

    public DateTime? EstimatedEta { get; set; }

    [Required]
    [MaxLength(50)]
    public string Status { get; set; } = "planned"; // planned, booked, in_transit, delivered, failed

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Navigation property
    [ForeignKey(nameof(OrderId))]
    public Order Order { get; set; } = null!;
}
