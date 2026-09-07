using Aveline.Api.Modules.Admin.Models;

namespace Aveline.Api.Modules.Admin.Repositories;

public interface IAdminApprovalRepository
{
    Task<AdminApprovalRequest?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<AdminApprovalRequest?> GetByClerkUserIdAsync(string clerkUserId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AdminApprovalRequest>> ListByStatusAsync(
        AdminApprovalStatus status,
        CancellationToken cancellationToken = default);

    Task<AdminApprovalRequest> CreateAsync(AdminApprovalRequest request, CancellationToken cancellationToken = default);

    Task<AdminApprovalRequest> UpdateAsync(AdminApprovalRequest request, CancellationToken cancellationToken = default);
}
