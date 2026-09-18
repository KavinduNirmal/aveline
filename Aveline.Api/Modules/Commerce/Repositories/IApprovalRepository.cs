using Aveline.Api.Modules.Commerce.DTOs;
using Aveline.Api.Modules.Commerce.Models;

namespace Aveline.Api.Modules.Commerce.Repositories;

public interface IApprovalRepository
{
    Task<ApprovalQueueEntry?> GetByIdAsync(Guid id, Guid organizationId, CancellationToken ct = default);
    Task<ApprovalQueueEntry?> GetByOrderIdAsync(Guid orderId, Guid organizationId, CancellationToken ct = default);
    Task<PagedResult<ApprovalQueueEntry>> ListAsync(Guid organizationId, string? status, int page, int pageSize, CancellationToken ct = default);
    Task<ApprovalQueueEntry> AddAsync(ApprovalQueueEntry entry, CancellationToken ct = default);
    Task<ApprovalQueueEntry> UpdateAsync(ApprovalQueueEntry entry, CancellationToken ct = default);
    Task<int> GetPendingCountAsync(Guid organizationId, CancellationToken ct = default);
}
