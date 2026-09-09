namespace Aveline.Application.DTOs.Visual;

public class AnalyzeImageDto
{
    public Guid OrgId { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    public string? Prompt { get; set; }
}
