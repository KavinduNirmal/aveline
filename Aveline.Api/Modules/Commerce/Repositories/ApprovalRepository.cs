using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Commerce.DTOs;
using Aveline.Api.Modules.Commerce.Models;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.Commerce.Repositories;

public class ApprovalRepository : IApprovalRepository
{
    private readonly AppDbContext _context;

    public ApprovalRepository(AppDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<ApprovalQueueEntry?> GetByIdAsync(Guid id, Guid organizationId, CancellationToken ct = default)
    {
        return await _context.ApprovalQueue
            .Include(a => a.Order)
                .ThenInclude(o => o.Items)
            .FirstOrDefaultAsync(a => a.Id == id && a.OrganizationId == organizationId, ct);
    }

    public async Task<ApprovalQueueEntry?> GetByOrderIdAsync(Guid orderId, Guid organizationId, CancellationToken ct = default)
    {
        return await _context.ApprovalQueue
            .Include(a => a.Order)
                .ThenInclude(o => o.Items)
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync(a => a.OrderId == orderId && a.OrganizationId == organizationId, ct);
    }

    public async Task<ApprovalQueueEntry?> GetPendingByThreadIdAsync(Guid organizationId, string threadId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(threadId))
        {
            return null;
        }

        var normalized = threadId.Trim();
        return await _context.ApprovalQueue
            .Include(a => a.Order)
                .ThenInclude(o => o.Items)
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync(
                a => a.OrganizationId == organizationId
                     && a.ThreadId == normalized
                     && a.Status.ToLower() == "pending",
                ct);
    }

    public async Task<PagedResult<ApprovalQueueEntry>> ListAsync(Guid organizationId, string? status, int page, int pageSize, CancellationToken ct = default)
    {
        var pageNum = Math.Max(1, page);
        var size = Math.Clamp(pageSize, 1, 100);

        var query = _context.ApprovalQueue
            .Include(a => a.Order)
                .ThenInclude(o => o.Items)
            .Where(a => a.OrganizationId == organizationId);

        if (!string.IsNullOrWhiteSpace(status))
        {
            var normalized = status.Trim().ToLowerInvariant();
            query = query.Where(a => a.Status.ToLower() == normalized);
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip((pageNum - 1) * size)
            .Take(size)
            .ToListAsync(ct);

        return new PagedResult<ApprovalQueueEntry>
        {
            Items = items,
            TotalCount = total,
            Page = pageNum,
            PageSize = size
        };
    }

    public async Task<ApprovalQueueEntry> AddAsync(ApprovalQueueEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _context.ApprovalQueue.Add(entry);
        await _context.SaveChangesAsync(ct);
        return entry;
    }

    public async Task<ApprovalQueueEntry> UpdateAsync(ApprovalQueueEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _context.ApprovalQueue.Update(entry);
        await _context.SaveChangesAsync(ct);
        return entry;
    }

    public async Task<int> GetPendingCountAsync(Guid organizationId, CancellationToken ct = default)
    {
        return await _context.ApprovalQueue
            .CountAsync(a => a.OrganizationId == organizationId && a.Status.ToLower() == "pending", ct);
    }
}
