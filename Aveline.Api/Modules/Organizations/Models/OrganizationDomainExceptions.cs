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
