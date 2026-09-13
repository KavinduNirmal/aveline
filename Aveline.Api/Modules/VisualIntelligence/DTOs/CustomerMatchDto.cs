namespace Aveline.Api.Modules.VisualIntelligence.DTOs;

public class CustomerMatchDto
{
    public Guid CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string? CustomerPhone { get; set; }
    public double MatchScore { get; set; }
    public string MatchReason { get; set; } = string.Empty;
    public List<string> MatchingPreferences { get; set; } = new();
}
