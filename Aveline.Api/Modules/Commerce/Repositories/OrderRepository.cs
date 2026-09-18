using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Commerce.DTOs;
using Aveline.Api.Modules.Commerce.Models;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.Commerce.Repositories;

public class OrderRepository : IOrderRepository
{
    private readonly AppDbContext _context;

    public OrderRepository(AppDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<Order?> GetByIdAsync(
        Guid id,
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Orders
            .Include(o => o.Items)
            .Include(o => o.Payments)
            .Include(o => o.Approvals)
            .Include(o => o.DeliveryPlan)
            .FirstOrDefaultAsync(o => o.Id == id && o.OrganizationId == organizationId, cancellationToken);
    }

    public async Task<IReadOnlyList<Order>> GetAllAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Orders
            .Include(o => o.Items)
            .Where(o => o.OrganizationId == organizationId)
            .OrderByDescending(o => o.CreatedAt)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<(IReadOnlyList<Order> Items, int TotalCount)> GetPagedAsync(
        Guid organizationId,
        OrderQueryParametersDto query,
        CancellationToken cancellationToken = default)
    {
        var dbQuery = _context.Orders
            .Include(o => o.Items)
            .Where(o => o.OrganizationId == organizationId);

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            var status = query.Status.Trim();
            dbQuery = dbQuery.Where(o => o.Status == status);
        }

        if (query.CustomerId.HasValue && query.CustomerId.Value != Guid.Empty)
        {
            dbQuery = dbQuery.Where(o => o.CustomerId == query.CustomerId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.OrderType))
        {
            var orderType = query.OrderType.Trim();
            dbQuery = dbQuery.Where(o => o.OrderType == orderType);
        }

        if (query.FromDate.HasValue)
        {
            dbQuery = dbQuery.Where(o => o.CreatedAt >= query.FromDate.Value);
        }

        if (query.ToDate.HasValue)
        {
            dbQuery = dbQuery.Where(o => o.CreatedAt <= query.ToDate.Value);
        }

        var totalCount = await dbQuery.CountAsync(cancellationToken);

        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize < 1 ? 20 : (query.PageSize > 100 ? 100 : query.PageSize);

        var items = await dbQuery
            .OrderByDescending(o => o.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<IReadOnlyList<Order>> GetByCustomerIdAsync(
        Guid customerId,
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Orders
            .Include(o => o.Items)
            .Where(o => o.OrganizationId == organizationId && o.CustomerId == customerId)
            .OrderByDescending(o => o.CreatedAt)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Order>> GetByStatusAsync(
        string status,
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Orders
            .Include(o => o.Items)
            .Where(o => o.OrganizationId == organizationId && o.Status == status)
            .OrderByDescending(o => o.CreatedAt)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<Order> CreateAsync(
        Order order,
        CancellationToken cancellationToken = default)
    {
        if (order.Id == Guid.Empty)
        {
            order.Id = Guid.NewGuid();
        }

        order.CreatedAt = DateTime.UtcNow;
        order.UpdatedAt = null;

        foreach (var item in order.Items)
        {
            if (item.Id == Guid.Empty)
            {
                item.Id = Guid.NewGuid();
            }
            item.OrderId = order.Id;
            item.OrganizationId = order.OrganizationId;
            item.CreatedAt = order.CreatedAt;
        }

        await _context.Orders.AddAsync(order, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        return order;
    }

    public async Task<Order> UpdateAsync(
        Order order,
        CancellationToken cancellationToken = default)
    {
        order.UpdatedAt = DateTime.UtcNow;

        _context.Orders.Update(order);
        await _context.SaveChangesAsync(cancellationToken);

        return order;
    }

    public async Task<bool> DeleteAsync(
        Guid id,
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        var order = await GetByIdAsync(id, organizationId, cancellationToken);
        if (order is null)
        {
            return false;
        }

        _context.Orders.Remove(order);
        await _context.SaveChangesAsync(cancellationToken);

        return true;
    }
}
