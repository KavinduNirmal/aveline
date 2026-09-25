namespace Aveline.Api.Modules.VisualIntelligence.DTOs;

public class UpdateCatalogTagDto
{
    public string? Label { get; set; }
    public string? ColorHex { get; set; }
    public int? SortOrder { get; set; }
    public bool? IsArchived { get; set; }
}
