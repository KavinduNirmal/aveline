using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Organizations.Models;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.Organizations.Repositories;

public class OrganizationRepository : IOrganizationRepository
{
    private readonly AppDbContext _context;

    public OrganizationRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<Organization?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Organizations
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
    }

    public async Task<Organization?> GetByOwnerUserIdAsync(Guid ownerUserId, CancellationToken cancellationToken = default)
    {
        return await _context.Organizations
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync(o => o.OwnerUserId == ownerUserId, cancellationToken);
    }

    public async Task<Organization?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        return await _context.Organizations
            .FirstOrDefaultAsync(o => o.Slug == slug, cancellationToken);
    }

    public async Task<bool> ExistsBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        return await _context.Organizations
            .AnyAsync(o => o.Slug == slug, cancellationToken);
    }

    public async Task<Organization> CreateAsync(Organization organization, CancellationToken cancellationToken = default)
    {
        organization.CreatedAt = DateTime.UtcNow;
        organization.UpdatedAt = DateTime.UtcNow;
        _context.Organizations.Add(organization);
        await _context.SaveChangesAsync(cancellationToken);
        return organization;
    }

    public async Task<Organization> UpdateAsync(Organization organization, CancellationToken cancellationToken = default)
    {
        organization.UpdatedAt = DateTime.UtcNow;
        _context.Organizations.Update(organization);
        await _context.SaveChangesAsync(cancellationToken);
        return organization;
    }

    public async Task<OrganizationMembership?> GetMembershipAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        return await _context.OrganizationMemberships
            .FirstOrDefaultAsync(
                m => m.OrganizationId == organizationId && m.UserId == userId,
                cancellationToken);
    }

    public async Task<OrganizationMembership> AddMembershipAsync(
        OrganizationMembership membership,
        CancellationToken cancellationToken = default)
    {
        membership.CreatedAt = DateTime.UtcNow;
        membership.UpdatedAt = DateTime.UtcNow;
        _context.OrganizationMemberships.Add(membership);
        await _context.SaveChangesAsync(cancellationToken);
        return membership;
    }

    public async Task<OrganizationMembership> UpdateMembershipAsync(
        OrganizationMembership membership,
        CancellationToken cancellationToken = default)
    {
        membership.UpdatedAt = DateTime.UtcNow;
        _context.OrganizationMemberships.Update(membership);
        await _context.SaveChangesAsync(cancellationToken);
        return membership;
    }

    public async Task RemoveMembershipAsync(
        OrganizationMembership membership,
        CancellationToken cancellationToken = default)
    {
        _context.OrganizationMemberships.Remove(membership);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OrganizationMembership>> ListMembershipsForUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        return await _context.OrganizationMemberships
            .AsNoTracking()
            .Where(m => m.UserId == userId)
            .OrderByDescending(m => m.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> UserHasActiveMembershipAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        return await _context.OrganizationMemberships
            .AsNoTracking()
            .AnyAsync(
                m => m.UserId == userId && m.Status == MembershipStatus.Active,
                cancellationToken);
    }

    public async Task<IReadOnlyList<OrganizationMembership>> ListMembershipsForOrganizationAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        return await _context.OrganizationMemberships
            .AsNoTracking()
            .Where(m => m.OrganizationId == organizationId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(cancellationToken);
    }
}
