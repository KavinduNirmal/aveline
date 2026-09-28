namespace Aveline.Api.Modules.Commerce.DTOs;

public class PaymentResponseDto
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid OrderId { get; set; }
    public decimal Amount { get; set; }
    public string PaymentType { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? GatewayTransactionId { get; set; }
    public string? PaymentLink { get; set; }

    /// <summary>
    /// The provider-neutral payment intent this charge was created through, or null for a counter
    /// payment with no provider behind it (Phase 9). Additive: the shipped clients ignore it, and
    /// the confirmation route polls by the payment id regardless.
    /// </summary>
    public Guid? PaymentIntentId { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
}
