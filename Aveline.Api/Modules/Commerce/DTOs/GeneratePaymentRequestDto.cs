using System.ComponentModel.DataAnnotations;

namespace Aveline.Api.Modules.Commerce.DTOs;

public class GeneratePaymentRequestDto
{
    [Required]
    public Guid OrderId { get; set; }

    [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than zero.")]
    public decimal Amount { get; set; }

    [MaxLength(50)]
    public string PaymentType { get; set; } = "full"; // deposit, balance, full

    [MaxLength(50)]
    public string PaymentMethod { get; set; } = "online"; // card, cash, online, bank_transfer
}
