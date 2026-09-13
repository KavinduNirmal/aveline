namespace Aveline.Api.Modules.VisualIntelligence.DTOs;

public class AnalyzeImageDto
{
    public Guid OrganizationId { get; set; }
    public Guid OrgId
    {
        get => OrganizationId;
        set => OrganizationId = value;
    }
    public string ImageUrl { get; set; } = string.Empty;
    public string? Prompt { get; set; }
}
