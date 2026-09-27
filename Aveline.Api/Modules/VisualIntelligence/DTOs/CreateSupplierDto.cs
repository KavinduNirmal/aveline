using System.ComponentModel.DataAnnotations;

namespace Aveline.Api.Modules.VisualIntelligence.DTOs;

public class CreateSupplierDto
{
    [Required]
    public string SupplierName { get; set; } = string.Empty;

    public string? Specialty { get; set; }

    public string? Location { get; set; }

    public string? ContactEmail { get; set; }

    public string? ContactPhone { get; set; }

    public string? WebsiteUrl { get; set; }

    public string? ApiEndpoint { get; set; }

    public decimal? MinimumOrder { get; set; }

    public int? DeliveryTimeDays { get; set; }

    public bool IsActive { get; set; } = true;
}
