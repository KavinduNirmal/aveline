namespace Aveline.Api.Modules.VisualIntelligence.DTOs;

public class CreateCatalogTagDto
{
    public string Slug { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string? ColorHex { get; set; }
    public int SortOrder { get; set; } = 0;
}
