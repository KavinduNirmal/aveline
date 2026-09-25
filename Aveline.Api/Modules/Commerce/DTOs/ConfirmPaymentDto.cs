using System.ComponentModel.DataAnnotations;

namespace Aveline.Api.Modules.Commerce.DTOs;

public class ConfirmPaymentDto
{
    /// <summary>
    /// The provider's transaction reference. <b>Advisory only after Phase 9.</b> The route reads
    /// settlement from the payment's linked intent, so a caller cannot settle a charge by naming a
    /// transaction id (plan §9.7). The field is retained because the shipped clients still send one
    /// and the rollback release still reads it.
    /// </summary>
    [MaxLength(100)]
    public string? GatewayTransactionId { get; set; }

    [MaxLength(50)]
    public string? PaymentMethod { get; set; }
}
