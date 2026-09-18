using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.CustomerConcierge.Models;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.CustomerConcierge.Repositories;

public class CustomerInteractionRepository : ICustomerInteractionRepository
{
    private readonly AppDbContext _context;

    public CustomerInteractionRepository(AppDbContext context) => _context = context;

    public async Task<CustomerInteraction> AddAsync(CustomerInteraction interaction, CancellationToken cancellationToken = default)
    {
        _context.CustomerInteractions.Add(interaction);
        await _context.SaveChangesAsync(cancellationToken);
        return interaction;
    }

    public async Task<IReadOnlyList<CustomerInteraction>> ListByCustomerAsync(
        Guid orgId,
        Guid customerId,
        CancellationToken cancellationToken = default)
        => await _context.CustomerInteractions
            .Where(i => i.OrganizationId == orgId && i.CustomerId == customerId)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(cancellationToken);
}
