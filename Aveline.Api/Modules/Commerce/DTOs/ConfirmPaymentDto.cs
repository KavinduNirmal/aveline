using System.ComponentModel.DataAnnotations;

namespace Aveline.Api.Modules.Commerce.DTOs;

public class ConfirmPaymentDto
{
    [Required]
    [MaxLength(100)]
    public string GatewayTransactionId { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? PaymentMethod { get; set; }
}
