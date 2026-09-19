namespace Aveline.Api.Modules.Commerce.DTOs;

public class PaymentQueryParametersDto
{
    public Guid? OrderId { get; set; }
    public string? Status { get; set; } // e.g. "pending", "confirmed", "refunded", "failed"
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
