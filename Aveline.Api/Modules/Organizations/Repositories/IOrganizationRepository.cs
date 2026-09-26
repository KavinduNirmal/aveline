using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Organizations.Models;

namespace Aveline.Api.Modules.Organizations.Repositories;

public interface IOrganizationRepository
{
    Task<Organization?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Organization?> GetByOwnerUserIdAsync(Guid ownerUserId, CancellationToken cancellationToken = default);
    Task<Organization?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);
    Task<bool> ExistsBySlugAsync(string slug, CancellationToken cancellationToken = default);
    Task<Organization> CreateAsync(Organization organization, CancellationToken cancellationToken = default);
    Task<Organization> UpdateAsync(Organization organization, CancellationToken cancellationToken = default);

    /// <summary>
    /// The organization's current subscription row, or <c>null</c> when plan selection has not yet
    /// provisioned one. Read-only: onboarding reports the price and status the plan selection
    /// recorded (plan §9.1 F1); the Billing module owns every write to the table.
    /// </summary>
    Task<OrganizationSubscription?> GetSubscriptionAsync(
        Guid organizationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cross-tenant organization search for the Aveline-team admin route (FR-4.8).
    /// Organizations are deliberately not tenant-scoped here: the caller's
    /// <c>admin:orgs:read</c> permission is the scope.
    /// </summary>
    Task<(IReadOnlyList<Organization> Items, int Total)> SearchAsync(
        string? term,
        bool? isActive,
        PlanTier? planTier,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<OrganizationMembership?> GetMembershipAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<OrganizationMembership> AddMembershipAsync(
        OrganizationMembership membership,
        CancellationToken cancellationToken = default);

    Task<OrganizationMembership> UpdateMembershipAsync(
        OrganizationMembership membership,
        CancellationToken cancellationToken = default);

    Task RemoveMembershipAsync(
        OrganizationMembership membership,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrganizationMembership>> ListMembershipsForUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>Returns whether the user holds at least one <see cref="MembershipStatus.Active"/> membership.</summary>
    Task<bool> UserHasActiveMembershipAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrganizationMembership>> ListMembershipsForOrganizationAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default);

    /// <summary>All memberships of an organization (any status) with the user populated.</summary>
    Task<IReadOnlyList<OrganizationMembership>> ListMembershipsWithUsersAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default);

    /// <summary>Removes every membership of a user across all organizations (FR-3.5).</summary>
    Task<int> RemoveAllMembershipsForUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the <see cref="MembershipStatus.Active"/> memberships of an organization,
    /// with the <see cref="OrganizationMembership.User"/> navigation populated.
    /// </summary>
    Task<IReadOnlyList<OrganizationMembership>> GetActiveMembersAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default);
}
