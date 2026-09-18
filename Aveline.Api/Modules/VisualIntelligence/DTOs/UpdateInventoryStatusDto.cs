namespace Aveline.Api.Modules.VisualIntelligence.DTOs;

public class UpdateInventoryStatusDto
{
    public Guid OrganizationId { get; set; }
    public Guid OrgId
    {
        get => OrganizationId;
        set => OrganizationId = value;
    }
    public string Status { get; set; } = string.Empty;
}
