using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.CustomerConcierge.Models;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.CustomerConcierge.Repositories;

public class CustomerTagRepository : ICustomerTagRepository
{
    private readonly AppDbContext _context;

    public CustomerTagRepository(AppDbContext context) => _context = context;

    public async Task AddAsync(CustomerTag tag, CancellationToken cancellationToken = default)
    {
        var exists = await _context.CustomerTags.AnyAsync(
            t => t.CustomerId == tag.CustomerId && t.Tag == tag.Tag,
            cancellationToken);
        if (exists)
        {
            return;
        }

        _context.CustomerTags.Add(tag);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> ListByCustomerAsync(
        Guid orgId,
        Guid customerId,
        CancellationToken cancellationToken = default)
        => await _context.CustomerTags
            .Where(t => t.OrganizationId == orgId && t.CustomerId == customerId)
            .Select(t => t.Tag)
            .ToListAsync(cancellationToken);
}
