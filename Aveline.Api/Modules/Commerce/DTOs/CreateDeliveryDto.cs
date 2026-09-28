using System.ComponentModel.DataAnnotations;

namespace Aveline.Api.Modules.Commerce.DTOs;

public class CreateDeliveryDto
{
    [Required]
    public Guid OrderId { get; set; }

    [MaxLength(50)]
    public string? CourierService { get; set; } = "PickMe"; // PickMe, Uber, In-house

    [Required]
    [MaxLength(500)]
    public string DeliveryAddress { get; set; } = string.Empty;

    public DateTime? PreferredDeliveryTime { get; set; }

    public decimal? EstimatedCost { get; set; }
}
