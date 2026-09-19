using System.ComponentModel.DataAnnotations;

namespace Aveline.Api.Modules.Commerce.DTOs;

public class UpdateDeliveryStatusDto
{
    [Required]
    [MaxLength(50)]
    public string Status { get; set; } = string.Empty; // planned, booked, in_transit, delivered, failed

    [MaxLength(100)]
    public string? TrackingNumber { get; set; }

    [MaxLength(50)]
    public string? CourierService { get; set; }
}
