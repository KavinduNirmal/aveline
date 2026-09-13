namespace Aveline.Api.Modules.VisualIntelligence.DTOs;

public class CreateSourcingRequestDto
{
    public Guid OrganizationId { get; set; }
    public Guid OrgId
    {
        get => OrganizationId;
        set => OrganizationId = value;
    }
    public string Category { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal? TargetPrice { get; set; }
    public int QuantityNeeded { get; set; } = 1;
    public Guid? CustomerId { get; set; }
    public string Urgency { get; set; } = "medium";
}
