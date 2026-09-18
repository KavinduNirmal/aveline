using Aveline.Api.Modules.Commerce.DTOs;

namespace Aveline.Api.Modules.Commerce.Services;

public interface IApprovalService
{
    Task<PagedResult<ApprovalQueueResponseDto>> GetPendingApprovalsAsync(Guid organizationId, ApprovalQueryParametersDto query, CancellationToken ct = default);
    Task<ApprovalQueueResponseDto?> GetApprovalByIdAsync(Guid id, Guid organizationId, CancellationToken ct = default);
    Task<ApprovalQueueResponseDto> ProcessDecisionAsync(Guid id, Guid organizationId, ApprovalDecisionDto dto, Guid? decidedBy, CancellationToken ct = default);
}
