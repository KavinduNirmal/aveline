using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Aveline.Api.Modules.Commerce.Models;

[Table("Order_Items")]
public class OrderItem
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid OrderId { get; set; }

    [Required]
    public Guid ItemId { get; set; } // Pointing to Inventory slice

    [Required]
    [MaxLength(200)]
    public string ItemName { get; set; } = string.Empty;

    [Required]
    public int Quantity { get; set; } = 1;

    [Required]
    [Column(TypeName = "decimal(18,2)")]
    public decimal UnitPrice { get; set; }

    [Required]
    [Column(TypeName = "decimal(18,2)")]
    public decimal WholesaleCost { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal TotalPrice { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation property
    [ForeignKey(nameof(OrderId))]
    public Order Order { get; set; } = null!;
}