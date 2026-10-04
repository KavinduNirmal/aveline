namespace Aveline.Api.Modules.VisualIntelligence.DTOs;

public class SupplierDto
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public string Name
    {
        get => SupplierName;
        set => SupplierName = value;
    }
    public string? Specialty { get; set; }
    public string? Location { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public string? WebsiteUrl { get; set; }
    public string? ApiEndpoint { get; set; }
    public decimal? MinimumOrder { get; set; }
    public int? DeliveryTimeDays { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
