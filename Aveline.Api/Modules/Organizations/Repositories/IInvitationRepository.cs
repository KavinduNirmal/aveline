using Aveline.Api.Modules.Organizations.Models;

namespace Aveline.Api.Modules.Organizations.Repositories;

public interface IInvitationRepository
{
    Task<OrganizationInvitation?> GetByTokenHashAsync(
        string tokenHash,
        CancellationToken cancellationToken = default);

    Task<OrganizationInvitation?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<OrganizationInvitation> CreateAsync(
        OrganizationInvitation invitation,
        CancellationToken cancellationToken = default);

    Task<OrganizationInvitation> UpdateAsync(
        OrganizationInvitation invitation,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrganizationInvitation>> ListPendingByOrganizationAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically activates <paramref name="membership"/> and marks
    /// <paramref name="invitation"/> accepted in a single <c>SaveChanges</c>,
    /// so an invitation can only ever produce one membership.
    /// </summary>
    Task<OrganizationInvitation> AcceptAsync(
        OrganizationInvitation invitation,
        OrganizationMembership membership,
        DateTime acceptedAtUtc,
        CancellationToken cancellationToken = default);
}
