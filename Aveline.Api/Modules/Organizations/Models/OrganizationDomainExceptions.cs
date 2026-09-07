namespace Aveline.Api.Modules.Organizations.Models;

/// <summary>Raised when an organization slug is already taken.</summary>
public class OrganizationSlugAlreadyInUseException : InvalidOperationException
{
    public OrganizationSlugAlreadyInUseException(string slug)
        : base($"An organization with slug '{slug}' already exists.")
    {
    }
}

/// <summary>Raised when an invitation code is unknown.</summary>
public class InvitationNotFoundException : KeyNotFoundException
{
    public InvitationNotFoundException()
        : base("The invitation was not found.")
    {
    }
}

/// <summary>Raised when a role is not a valid staff role that can be invited.</summary>
public class InvalidInvitationRoleException(string role)
    : InvalidOperationException($"'{role}' is not an invitable boutique staff role.")
{
}

/// <summary>
/// Raised when the invitation code store (Redis/distributed cache) is unavailable during
/// invitation creation. Creation is fail-fast so an invitation is never issued whose code
/// cannot be redeemed promptly.
/// </summary>
public class InvitationCodeStoreUnavailableException()
    : InvalidOperationException("The invitation code store is unavailable. Please retry shortly.")
{
}

/// <summary>Raised when an already-accepted invitation is revoked.</summary>
public class CannotRevokeAcceptedInvitationException()
    : InvalidOperationException("An already-accepted invitation cannot be revoked.")
{
}

/// <summary>Raised when an invitation is expired, revoked, or already accepted.</summary>
public class InvitationNotAcceptableException : InvalidOperationException
{
    public InvitationNotAcceptableException(string reason)
        : base($"The invitation cannot be accepted: {reason}")
    {
    }
}

/// <summary>Raised when the accepting user does not match the invitation recipient.</summary>
public class InvitationRecipientMismatchException : InvalidOperationException
{
    public InvitationRecipientMismatchException()
        : base("This invitation was issued for a different recipient.")
    {
    }
}

/// <summary>Raised when a user already has a membership in the organization.</summary>
public class MembershipAlreadyExistsException : InvalidOperationException
{
    public MembershipAlreadyExistsException()
        : base("The user is already a member of this organization.")
    {
    }
}

/// <summary>Raised when a membership is not found for the organization/user pair.</summary>
public class MembershipNotFoundException : KeyNotFoundException
{
    public MembershipNotFoundException(Guid organizationId, Guid userId)
        : base($"No membership found for user '{userId}' in organization '{organizationId}'.")
    {
    }
}

/// <summary>Raised when a caller tries to suspend or remove an organization owner.</summary>
public class CannotManageOwnerMembershipException : InvalidOperationException
{
    public CannotManageOwnerMembershipException()
        : base("The organization owner's membership cannot be suspended or removed.")
    {
    }
}
