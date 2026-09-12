namespace Aveline.Api.Modules.VisualIntelligence.DTOs;

public class ImageAnalysisResultDto
{
    public string Category { get; set; } = string.Empty;
    public string PrimaryColor { get; set; } = string.Empty;
    public List<string> SecondaryColors { get; set; } = new();
    public string? Pattern { get; set; }
    public string? Style { get; set; }
    public string? Fabric { get; set; }
    public double ConfidenceScore { get; set; } = 0.95;
    public List<string> SuggestedKeywords { get; set; } = new();
}
