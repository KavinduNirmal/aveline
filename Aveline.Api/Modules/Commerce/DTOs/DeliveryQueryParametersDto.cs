namespace Aveline.Api.Modules.Commerce.DTOs;

public class DeliveryQueryParametersDto
{
    public Guid? OrderId { get; set; }
    public string? Status { get; set; }
    public string? CourierService { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
