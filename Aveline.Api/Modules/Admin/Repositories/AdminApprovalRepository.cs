using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Admin.Models;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.Admin.Repositories;

public class AdminApprovalRepository : IAdminApprovalRepository
{
    private readonly AppDbContext _context;

    public AdminApprovalRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<AdminApprovalRequest?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.AdminApprovalRequests
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
    }

    public async Task<AdminApprovalRequest?> GetByClerkUserIdAsync(string clerkUserId, CancellationToken cancellationToken = default)
    {
        return await _context.AdminApprovalRequests
            .Where(r => r.ClerkUserId == clerkUserId)
            .OrderByDescending(r => r.RequestedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AdminApprovalRequest>> ListByStatusAsync(
        AdminApprovalStatus status,
        CancellationToken cancellationToken = default)
    {
        return await _context.AdminApprovalRequests
            .AsNoTracking()
            .Where(r => r.Status == status)
            .OrderByDescending(r => r.RequestedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<AdminApprovalRequest> CreateAsync(AdminApprovalRequest request, CancellationToken cancellationToken = default)
    {
        _context.AdminApprovalRequests.Add(request);
        await _context.SaveChangesAsync(cancellationToken);
        return request;
    }

    public async Task<AdminApprovalRequest> UpdateAsync(AdminApprovalRequest request, CancellationToken cancellationToken = default)
    {
        _context.AdminApprovalRequests.Update(request);
        await _context.SaveChangesAsync(cancellationToken);
        return request;
    }
}
