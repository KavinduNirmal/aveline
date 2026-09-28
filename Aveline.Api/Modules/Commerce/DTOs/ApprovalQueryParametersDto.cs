namespace Aveline.Api.Modules.Commerce.DTOs;

public class ApprovalQueryParametersDto
{
    public string? Status { get; set; } // e.g. "pending", "approved", "rejected", "revised"
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
