namespace Aveline.Application.DTOs.Visual;

public class SourcingRequestDto
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrgId { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal? TargetPrice { get; set; }
    public int QuantityNeeded { get; set; }
    public Guid? CustomerId { get; set; }
    public string Urgency { get; set; } = "medium";
    public string Status { get; set; } = "pending";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
