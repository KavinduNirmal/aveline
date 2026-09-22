namespace Aveline.Api.Modules.VisualIntelligence.DTOs;

public class CatalogFacetsResponse
{
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public int TotalItems { get; set; }
    public List<CatalogFacetGroupDto> Groups { get; set; } = new();
}

public class CatalogFacetGroupDto
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool SingleSelect { get; set; }
    public List<CatalogFacetOptionDto> Options { get; set; } = new();
}

public class CatalogFacetOptionDto
{
    public string Value { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int Count { get; set; }
    public bool Available => Count > 0;
}
