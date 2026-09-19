using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Commerce.DTOs;
using Aveline.Api.Modules.Commerce.Models;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.Commerce.Repositories;

public class DeliveryRepository : IDeliveryRepository
{
    private readonly AppDbContext _context;

    public DeliveryRepository(AppDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<DeliveryPlan?> GetByIdAsync(Guid id, Guid organizationId, CancellationToken ct = default)
    {
        return await _context.DeliveryPlans
            .Include(d => d.Order)
            .FirstOrDefaultAsync(d => d.Id == id && d.OrganizationId == organizationId, ct);
    }

    public async Task<DeliveryPlan?> GetByOrderIdAsync(Guid orderId, Guid organizationId, CancellationToken ct = default)
    {
        return await _context.DeliveryPlans
            .Include(d => d.Order)
            .OrderByDescending(d => d.CreatedAt)
            .FirstOrDefaultAsync(d => d.OrderId == orderId && d.OrganizationId == organizationId, ct);
    }

    public async Task<PagedResult<DeliveryPlan>> ListAsync(Guid organizationId, DeliveryQueryParametersDto query, CancellationToken ct = default)
    {
        var pageNum = Math.Max(1, query.Page);
        var size = Math.Clamp(query.PageSize, 1, 100);

        var dbQuery = _context.DeliveryPlans
            .Include(d => d.Order)
            .Where(d => d.OrganizationId == organizationId);

        if (query.OrderId.HasValue && query.OrderId.Value != Guid.Empty)
        {
            dbQuery = dbQuery.Where(d => d.OrderId == query.OrderId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            var normalized = query.Status.Trim().ToLowerInvariant();
            dbQuery = dbQuery.Where(d => d.Status.ToLower() == normalized);
        }

        if (!string.IsNullOrWhiteSpace(query.CourierService))
        {
            var carrier = query.CourierService.Trim().ToLowerInvariant();
            dbQuery = dbQuery.Where(d => d.CourierService != null && d.CourierService.ToLower() == carrier);
        }

        var total = await dbQuery.CountAsync(ct);
        var items = await dbQuery
            .OrderByDescending(d => d.CreatedAt)
            .Skip((pageNum - 1) * size)
            .Take(size)
            .ToListAsync(ct);

        return new PagedResult<DeliveryPlan>
        {
            Items = items,
            TotalCount = total,
            Page = pageNum,
            PageSize = size
        };
    }

    public async Task<DeliveryPlan> AddAsync(DeliveryPlan plan, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        _context.DeliveryPlans.Add(plan);
        await _context.SaveChangesAsync(ct);
        return plan;
    }

    public async Task<DeliveryPlan> UpdateAsync(DeliveryPlan plan, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        _context.DeliveryPlans.Update(plan);
        await _context.SaveChangesAsync(ct);
        return plan;
    }
}
