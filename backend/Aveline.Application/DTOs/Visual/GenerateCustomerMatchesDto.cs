namespace Aveline.Application.DTOs.Visual;

public class GenerateCustomerMatchesDto
{
    public Guid OrgId { get; set; }
    public int MaxMatches { get; set; } = 10;
}
