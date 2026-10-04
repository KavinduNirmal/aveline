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
    public decimal? EstimatedCost { get; set; }
    public decimal? ProposedMarkup { get; set; }
    public Guid? SupplierId { get; set; }
    public string? ReferenceImageUrl { get; set; }
    public string? ClientName { get; set; }
    public int QuantityNeeded { get; set; } = 1;
    public Guid? CustomerId { get; set; }
    public string Urgency { get; set; } = "medium";
}
