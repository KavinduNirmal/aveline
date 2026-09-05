using Aveline.Api.Modules.Organizations.Models;

namespace Aveline.Api.Modules.Organizations.Repositories;

public interface IOrganizationRepository
{
    Task<Organization?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Organization?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);
    Task<bool> ExistsBySlugAsync(string slug, CancellationToken cancellationToken = default);
    Task<Organization> CreateAsync(Organization organization, CancellationToken cancellationToken = default);
    Task<Organization> UpdateAsync(Organization organization, CancellationToken cancellationToken = default);

    Task<OrganizationMembership?> GetMembershipAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<OrganizationMembership> AddMembershipAsync(
        OrganizationMembership membership,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrganizationMembership>> ListMembershipsForUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrganizationMembership>> ListMembershipsForOrganizationAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default);
}
