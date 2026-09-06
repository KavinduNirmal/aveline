namespace Aveline.Api.Modules.Organizations.Models;

/// <summary>
/// Membership lifecycle state of a <see cref="User"/> within an
/// <see cref="Organization"/>.
/// </summary>
public enum MembershipStatus
{
    /// <summary>Invitation issued, not yet accepted (reserved for pre-provisioned memberships).</summary>
    Pending,

    /// <summary>Full, active member (granted after an invitation is accepted).</summary>
    Active,

    /// <summary>Membership suspended by an owner/manager.</summary>
    Suspended,
}
