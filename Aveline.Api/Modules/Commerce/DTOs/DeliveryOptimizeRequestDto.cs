namespace Aveline.Api.Modules.Commerce.DTOs;

public class DeliveryOptimizeRequestDto
{
    public List<Guid> DeliveryPlanIds { get; set; } = new();
    public string OptimizationStrategy { get; set; } = "shortest_distance"; // shortest_distance, fastest_time
}
