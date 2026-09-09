namespace Aveline.Api.Modules.VisualIntelligence.Models;

public class SourcingRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrgId { get; set; }
    public Guid? CustomerId { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public string ReferenceImageUrl { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal? TargetPrice { get; set; }
    public string Status { get; set; } = "pending";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }
}

public class Supplier
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrgId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public string? ApiEndpoint { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
