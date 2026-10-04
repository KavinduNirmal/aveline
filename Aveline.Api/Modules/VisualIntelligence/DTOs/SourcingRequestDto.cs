namespace Aveline.Api.Modules.VisualIntelligence.DTOs;

public class SourcingRequestDto
{
    public Guid Id { get; set; } = Guid.NewGuid();
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
    public decimal? ProposedPrice
    {
        get => TargetPrice;
        set => TargetPrice = value;
    }
    public decimal? EstimatedCost { get; set; }
    public decimal? ProposedMarkup { get; set; }
    public Guid? SupplierId { get; set; }
    public string? SupplierName { get; set; }
    public string? ClientName { get; set; }
    public string? ReferenceImageUrl { get; set; }
    public int QuantityNeeded { get; set; }
    public Guid? CustomerId { get; set; }
    public string Urgency { get; set; } = "medium";
    public string Status { get; set; } = "pending";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }
}
