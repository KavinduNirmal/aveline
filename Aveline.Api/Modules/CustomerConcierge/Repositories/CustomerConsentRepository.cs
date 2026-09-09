using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.CustomerConcierge.Models;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.CustomerConcierge.Repositories;

public class CustomerConsentRepository : ICustomerConsentRepository
{
    private readonly AppDbContext _context;

    public CustomerConsentRepository(AppDbContext context) => _context = context;

    public async Task<CustomerConsent?> GetForCustomerAsync(
        Guid orgId,
        Guid customerId,
        CancellationToken cancellationToken = default)
        => await _context.CustomerConsents
            .FirstOrDefaultAsync(
                c => c.OrganizationId == orgId && c.CustomerId == customerId,
                cancellationToken);

    public async Task<CustomerConsent> AddAsync(CustomerConsent consent, CancellationToken cancellationToken = default)
    {
        _context.CustomerConsents.Add(consent);
        await _context.SaveChangesAsync(cancellationToken);
        return consent;
    }

    public async Task SaveAsync(CustomerConsent consent, CancellationToken cancellationToken = default)
    {
        consent.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
    }
}
