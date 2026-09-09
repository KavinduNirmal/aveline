namespace Aveline.Application.DTOs.Visual;

public class CreateSourcingRequestDto
{
    public Guid OrgId { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal? TargetPrice { get; set; }
    public int QuantityNeeded { get; set; } = 1;
    public Guid? CustomerId { get; set; }
    public string Urgency { get; set; } = "medium";
}
