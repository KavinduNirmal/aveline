using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Aveline.Api.Modules.Commerce.Models;

[Table("Payments")]
public class Payment
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid OrderId { get; set; }

    [Required]
    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }

    [Required]
    [MaxLength(50)]
    public string PaymentType { get; set; } = "deposit"; // deposit, balance, full

    [Required]
    [MaxLength(50)]
    public string PaymentMethod { get; set; } = "online"; // card, cash, online, bank_transfer

    [Required]
    [MaxLength(50)]
    public string Status { get; set; } = "pending"; // pending, confirmed, failed, expired, refunded

    [MaxLength(100)]
    public string? GatewayTransactionId { get; set; }

    [MaxLength(500)]
    public string? PaymentLink { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ConfirmedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }

    // Navigation property
    [ForeignKey(nameof(OrderId))]
    public Order Order { get; set; } = null!;
}