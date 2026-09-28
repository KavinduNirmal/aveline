using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Commerce.DTOs;
using Aveline.Api.Modules.Commerce.Models;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.Commerce.Repositories;

public class PaymentRepository : IPaymentRepository
{
    private readonly AppDbContext _context;

    public PaymentRepository(AppDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<Payment?> GetByIdAsync(Guid id, Guid organizationId, CancellationToken ct = default)
    {
        return await _context.Payments
            .Include(p => p.Order)
            .FirstOrDefaultAsync(p => p.Id == id && p.OrganizationId == organizationId, ct);
    }

    public async Task<Payment?> GetByOrderIdAsync(Guid orderId, Guid organizationId, CancellationToken ct = default)
    {
        return await _context.Payments
            .Include(p => p.Order)
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync(p => p.OrderId == orderId && p.OrganizationId == organizationId, ct);
    }

    public async Task<PagedResult<Payment>> ListAsync(Guid organizationId, PaymentQueryParametersDto query, CancellationToken ct = default)
    {
        var pageNum = Math.Max(1, query.Page);
        var size = Math.Clamp(query.PageSize, 1, 100);

        var dbQuery = _context.Payments
            .Include(p => p.Order)
            .Where(p => p.OrganizationId == organizationId);

        if (query.OrderId.HasValue && query.OrderId.Value != Guid.Empty)
        {
            dbQuery = dbQuery.Where(p => p.OrderId == query.OrderId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            var normalized = query.Status.Trim().ToLowerInvariant();
            dbQuery = dbQuery.Where(p => p.Status.ToLower() == normalized);
        }

        var total = await dbQuery.CountAsync(ct);
        var items = await dbQuery
            .OrderByDescending(p => p.CreatedAt)
            .Skip((pageNum - 1) * size)
            .Take(size)
            .ToListAsync(ct);

        return new PagedResult<Payment>
        {
            Items = items,
            TotalCount = total,
            Page = pageNum,
            PageSize = size
        };
    }

    public async Task<Payment> AddAsync(Payment payment, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(payment);
        _context.Payments.Add(payment);
        await _context.SaveChangesAsync(ct);
        return payment;
    }

    public async Task<Payment> UpdateAsync(Payment payment, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(payment);
        _context.Payments.Update(payment);
        await _context.SaveChangesAsync(ct);
        return payment;
    }
}
