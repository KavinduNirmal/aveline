using Aveline.Api.Modules.Organizations.Models;

namespace Aveline.Api.Modules.Organizations.Services;

/// <summary>Draft describing who to invite and the role to grant.</summary>
public sealed record InviteMemberRequest(
    string BoutiqueRole,
    string? RecipientEmail = null,
    Guid? RecipientUserId = null,
    TimeSpan? ValidFor = null);

/// <summary>Result of creating an invitation — contains the one-time plaintext code.</summary>
public sealed record InviteMemberResult(OrganizationInvitation Invitation, string Code);

/// <summary>Result of accepting an invitation.</summary>
public sealed record AcceptedMembership(
    Guid OrganizationId,
    Guid UserId,
    string BoutiqueRole,
    string? ClerkOrgId);

public interface IOrganizationService
{
    /// <summary>Creates an organization owned by <paramref name="ownerUserId"/> and provisions the owner's membership.</summary>
    Task<Organization> CreateOrganizationAsync(
        Guid ownerUserId,
        string name,
        string? clerkOrgId,
        string? slug = null,
        CancellationToken cancellationToken = default);

    /// <summary>Creates an invitation for a boutique role and returns the one-time plaintext code.</summary>
    Task<InviteMemberResult> InviteMemberAsync(
        Guid organizationId,
        Guid inviterUserId,
        InviteMemberRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Accepts an invitation code once, activating a membership for the accepting user.</summary>
    Task<AcceptedMembership> AcceptInvitationAsync(
        string code,
        Guid acceptingUserId,
        string? acceptingEmail,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the user's active memberships.</summary>
    Task<IReadOnlyList<OrganizationMembership>> GetUserMembershipsAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}
