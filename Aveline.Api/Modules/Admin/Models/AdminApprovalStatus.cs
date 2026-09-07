namespace Aveline.Api.Modules.Admin.Models;

/// <summary>Lifecycle of an administrator access request.</summary>
public enum AdminApprovalStatus
{
    /// <summary>Awaiting review by an existing administrator.</summary>
    Pending,

    /// <summary>Approved — the Clerk <c>admin</c> role was granted.</summary>
    Approved,

    /// <summary>Rejected by a reviewer.</summary>
    Rejected,
}
