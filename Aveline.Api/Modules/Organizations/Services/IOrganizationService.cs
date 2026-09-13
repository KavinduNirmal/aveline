using Aveline.Api.Modules.Organizations.DTOs;
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

    /// <summary>Returns pending (unaccepted, unrevoked, unexpired) invitations for an organization.</summary>
    Task<IReadOnlyList<OrganizationInvitation>> ListPendingInvitationsAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default);

    /// <summary>Revokes a pending invitation so it can no longer be accepted.</summary>
    Task RevokeInvitationAsync(
        Guid organizationId,
        Guid invitationId,
        Guid revokingUserId,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the user's active memberships.</summary>
    Task<IReadOnlyList<OrganizationMembership>> GetUserMembershipsAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>Returns whether the user holds at least one active organization membership.</summary>
    Task<bool> HasActiveMembershipAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a boutique by its <paramref name="slug"/> and pairs it with the
    /// requesting user's membership (if any). Returns null when no boutique has the slug.
    /// </summary>
    Task<OrganizationProfileWithMembershipDto?> GetOrganizationProfileBySlugAsync(
        string slug,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the <see cref="OrganizationProfileDto"/> for a boutique by id, or null when
    /// it does not exist. Callers are expected to be authorized for the organization first
    /// (see the org-scoped <c>BoutiqueAccess</c> policy).
    /// </summary>
    Task<OrganizationProfileDto?> GetOrganizationProfileAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default);

    /// <summary>Changes a member's membership status (suspend/reactivate) and invalidates their cached state.</summary>
    Task<OrganizationMembership> SetMembershipStatusAsync(
        Guid organizationId,
        Guid memberUserId,
        MembershipStatus status,
        CancellationToken cancellationToken = default);

    /// <summary>Removes a member's membership and invalidates their cached state.</summary>
    Task RemoveMembershipAsync(
        Guid organizationId,
        Guid memberUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a partial update to an organization's profile and settings (FR-4.1), gating
    /// the AI-context fields on the <c>ai.customContext</c> entitlement.
    /// </summary>
    Task<Organization> UpdateSettingsAsync(
        Guid organizationId,
        UpdateOrganizationSettingsRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken = default);

    /// <summary>Lists an organization's members with status/role/search filters (FR-3.1).</summary>
    Task<PagedOrganizationMembers> ListMembersAsync(
        Guid organizationId,
        string? status,
        string? role,
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes a member's boutique role, enforcing FR-3.4: no self-change, no demotion of
    /// the last active owner, and only an owner may grant or revoke the owner role.
    /// </summary>
    Task<OrganizationMemberDto> ChangeMemberRoleAsync(
        Guid organizationId,
        Guid memberUserId,
        string newRole,
        Guid actingUserId,
        CancellationToken cancellationToken = default);
}
