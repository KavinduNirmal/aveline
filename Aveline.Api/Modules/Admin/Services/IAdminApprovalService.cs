using Aveline.Api.Modules.Admin.Models;

namespace Aveline.Api.Modules.Admin.Services;

/// <summary>Input for submitting an administrator access request.</summary>
public sealed record SubmitAdminRequestInput(
    string ClerkUserId,
    string Email,
    string FirstName,
    string LastName);

public interface IAdminApprovalService
{
    /// <summary>
    /// Submits a request (idempotent per user while a Pending/Approved request exists).
    /// </summary>
    Task<AdminApprovalRequest> SubmitAsync(
        SubmitAdminRequestInput input,
        CancellationToken cancellationToken = default);

    /// <summary>Lists requests awaiting review.</summary>
    Task<IReadOnlyList<AdminApprovalRequest>> ListPendingAsync(CancellationToken cancellationToken = default);

    /// <summary>Approves a request, grants the Clerk admin role, and updates the local read model.</summary>
    Task<AdminApprovalRequest> ApproveAsync(
        Guid requestId,
        string reviewerClerkUserId,
        CancellationToken cancellationToken = default);

    /// <summary>Rejects a pending request.</summary>
    Task<AdminApprovalRequest> RejectAsync(
        Guid requestId,
        string reviewerClerkUserId,
        CancellationToken cancellationToken = default);
}
