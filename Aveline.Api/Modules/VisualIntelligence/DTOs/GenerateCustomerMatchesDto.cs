namespace Aveline.Api.Modules.VisualIntelligence.DTOs;

public class GenerateCustomerMatchesDto
{
    public Guid OrganizationId { get; set; }
    public Guid OrgId
    {
        get => OrganizationId;
        set => OrganizationId = value;
    }
    public int MaxMatches { get; set; } = 10;
}
