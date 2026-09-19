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
    public DateTime CreatedAt { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
}
