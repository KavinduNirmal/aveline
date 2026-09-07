using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Organizations.Models;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.Organizations.Repositories;

public class InvitationRepository : IInvitationRepository
{
    private readonly AppDbContext _context;

    public InvitationRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<OrganizationInvitation?> GetByTokenHashAsync(
        string tokenHash,
        CancellationToken cancellationToken = default)
    {
        return await _context.OrganizationInvitations
            .FirstOrDefaultAsync(i => i.TokenHash == tokenHash, cancellationToken);
    }

    public async Task<OrganizationInvitation?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return await _context.OrganizationInvitations
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
    }

    public async Task<OrganizationInvitation> CreateAsync(
        OrganizationInvitation invitation,
        CancellationToken cancellationToken = default)
    {
        invitation.CreatedAt = DateTime.UtcNow;
        invitation.UpdatedAt = DateTime.UtcNow;
        _context.OrganizationInvitations.Add(invitation);
        await _context.SaveChangesAsync(cancellationToken);
        return invitation;
    }

    public async Task<OrganizationInvitation> UpdateAsync(
        OrganizationInvitation invitation,
        CancellationToken cancellationToken = default)
    {
        invitation.UpdatedAt = DateTime.UtcNow;
        _context.OrganizationInvitations.Update(invitation);
        await _context.SaveChangesAsync(cancellationToken);
        return invitation;
    }

    public async Task<bool> DeleteAsync(
        Guid invitationId,
        CancellationToken cancellationToken = default)
    {
        var invitation = await _context.OrganizationInvitations
            .FirstOrDefaultAsync(i => i.Id == invitationId, cancellationToken);
        if (invitation is null)
        {
            return false;
        }

        _context.OrganizationInvitations.Remove(invitation);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<OrganizationInvitation>> ListPendingByOrganizationAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        return await _context.OrganizationInvitations
            .AsNoTracking()
            .Where(i => i.OrganizationId == organizationId
                        && i.AcceptedAt == null
                        && i.RevokedAt == null
                        && i.ExpiresAt > now)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<OrganizationInvitation> AcceptAsync(
        OrganizationInvitation invitation,
        OrganizationMembership membership,
        DateTime acceptedAtUtc,
        CancellationToken cancellationToken = default)
    {
        membership.CreatedAt = acceptedAtUtc;
        membership.UpdatedAt = acceptedAtUtc;
        invitation.AcceptedAt = acceptedAtUtc;
        invitation.UpdatedAt = acceptedAtUtc;

        _context.OrganizationMemberships.Add(membership);
        _context.OrganizationInvitations.Update(invitation);
        await _context.SaveChangesAsync(cancellationToken);

        return invitation;
    }
}
