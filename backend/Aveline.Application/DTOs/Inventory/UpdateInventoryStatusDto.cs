namespace Aveline.Application.DTOs.Inventory;

public class UpdateInventoryStatusDto
{
    public Guid OrgId { get; set; }
    public string Status { get; set; } = string.Empty;
}
