namespace Aveline.Api.Modules.VisualIntelligence.DTOs;

public class ImageAnalysisResultDto
{
    public string Category { get; set; } = string.Empty;
    public string PrimaryColor { get; set; } = string.Empty;
    public string? ColorHex { get; set; }
    public List<string> SecondaryColors { get; set; } = new();
    public string? Pattern { get; set; }
    public string? Style { get; set; }
    public string? Fabric { get; set; }
    public string? GarmentType { get; set; }
    public string? SuggestedItemName { get; set; }
    public string? Description { get; set; }
    public string? StylingNotes { get; set; }
    public double ConfidenceScore { get; set; } = 0.95;
    public bool IsFallback { get; set; } = false;
    public List<string> SuggestedKeywords { get; set; } = new();
}
