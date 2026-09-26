using Aveline.Api.Modules.Commerce.DTOs;
using Aveline.Api.Modules.Commerce.Models;

namespace Aveline.Api.Modules.Commerce.Repositories;

public interface IApprovalRepository
{
    Task<ApprovalQueueEntry?> GetByIdAsync(Guid id, Guid organizationId, CancellationToken ct = default);
    Task<ApprovalQueueEntry?> GetByOrderIdAsync(Guid orderId, Guid organizationId, CancellationToken ct = default);

    /// <summary>
    /// The outstanding approval for a workflow thread, or <c>null</c> when the thread has none.
    /// </summary>
    /// <remarks>
    /// This is the pause path's idempotency key (ADR-024, invariant A4): creating an order when a run
    /// pauses is a side effect of a run that may be retried, and a second order for one pause would
    /// double every figure derived from it. Only <c>pending</c> rows count - a thread may
    /// legitimately place another order once the first has been decided.
    /// </remarks>
    Task<ApprovalQueueEntry?> GetPendingByThreadIdAsync(Guid organizationId, string threadId, CancellationToken ct = default);

    Task<PagedResult<ApprovalQueueEntry>> ListAsync(Guid organizationId, string? status, int page, int pageSize, CancellationToken ct = default);
    Task<ApprovalQueueEntry> AddAsync(ApprovalQueueEntry entry, CancellationToken ct = default);
    Task<ApprovalQueueEntry> UpdateAsync(ApprovalQueueEntry entry, CancellationToken ct = default);
    Task<int> GetPendingCountAsync(Guid organizationId, CancellationToken ct = default);
}
