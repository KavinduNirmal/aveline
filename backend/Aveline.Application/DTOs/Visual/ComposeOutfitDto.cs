namespace Aveline.Application.DTOs.Visual;

public class ComposeOutfitDto
{
    public Guid OrgId { get; set; }
    public Guid PrimaryItemId { get; set; }
    public string? Occasion { get; set; }
    public string? Style { get; set; }
}
