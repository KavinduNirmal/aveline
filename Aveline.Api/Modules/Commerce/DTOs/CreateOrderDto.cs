using System.ComponentModel.DataAnnotations;

namespace Aveline.Api.Modules.Commerce.DTOs;

public class CreateOrderDto
{
    [Required]
    public Guid CustomerId { get; set; }

    [Required]
    [MaxLength(100)]
    public string CustomerName { get; set; } = string.Empty;

    [MaxLength(50)]
    public string OrderType { get; set; } = "whatsapp"; // in_store, whatsapp, instagram, sourcing

    [MinLength(1, ErrorMessage = "An order must contain at least one line item.")]
    public List<OrderItemDto> Items { get; set; } = new();

    [Range(0, double.MaxValue, ErrorMessage = "Discount cannot be negative.")]
    public decimal? Discount { get; set; }

    [MaxLength(50)]
    public string? CustomerTier { get; set; } // VIP, Platinum, Gold, Silver, Bronze, Standard

    [MaxLength(500)]
    public string? Notes { get; set; }

    /// <summary>LangGraph checkpoint thread id for HITL approval workflows (ADR-016).</summary>
    public string? ThreadId { get; set; }

    /// <summary>The Salon conversation this order originates from (ADR-016).</summary>
    public Guid? ConversationId { get; set; }
}
