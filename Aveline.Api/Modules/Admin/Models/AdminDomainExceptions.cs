namespace Aveline.Api.Modules.Admin.Models;

/// <summary>Domain exceptions for the Admin module.</summary>
public abstract class AdminDomainException(string message) : Exception(message);

/// <summary>
/// Raised when a reviewer tries to approve the admin access request they submitted
/// themselves (segregation of duties). The request is left Pending and no Clerk role
/// is granted.
/// </summary>
public sealed class AdminSelfApprovalException(Guid requestId)
    : AdminDomainException(
        $"Admin approval request '{requestId}' cannot be approved by the user who submitted it.");
