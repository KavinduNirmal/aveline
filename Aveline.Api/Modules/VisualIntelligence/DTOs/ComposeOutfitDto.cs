namespace Aveline.Api.Modules.VisualIntelligence.DTOs;

public class ComposeOutfitDto
{
    public Guid OrganizationId { get; set; }
    public Guid OrgId
    {
        get => OrganizationId;
        set => OrganizationId = value;
    }
    public Guid PrimaryItemId { get; set; }
    public string? Occasion { get; set; }
    public string? Style { get; set; }
}
