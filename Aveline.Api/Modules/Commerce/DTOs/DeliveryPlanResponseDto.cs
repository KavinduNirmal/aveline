namespace Aveline.Api.Modules.Commerce.DTOs;

public class DeliveryPlanResponseDto
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid OrderId { get; set; }
    public string? CourierService { get; set; }
    public string? TrackingNumber { get; set; }
    public string DeliveryAddress { get; set; } = string.Empty;
    public DateTime? PreferredDeliveryTime { get; set; }
    public string? RouteOptimized { get; set; }
    public decimal? EstimatedCost { get; set; }
    public DateTime? EstimatedEta { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
