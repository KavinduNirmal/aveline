using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.CustomerConcierge.Models;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.CustomerConcierge.Repositories;

public class CustomerRepository : ICustomerRepository
{
    private readonly AppDbContext _context;

    public CustomerRepository(AppDbContext context) => _context = context;

    public async Task<Customer?> GetAsync(Guid orgId, Guid id, CancellationToken cancellationToken = default)
        => await _context.Customers
            .Include(c => c.Preferences)
            .Include(c => c.Tags)
            .FirstOrDefaultAsync(c => c.OrganizationId == orgId && c.Id == id, cancellationToken);

    public async Task<Customer?> GetByPhoneAsync(Guid orgId, string phoneNumber, CancellationToken cancellationToken = default)
        => await _context.Customers
            .Include(c => c.Preferences)
            .Include(c => c.Tags)
            .FirstOrDefaultAsync(
                c => c.OrganizationId == orgId && c.PhoneNumber == phoneNumber,
                cancellationToken);

    public async Task<Customer> AddAsync(Customer customer, CancellationToken cancellationToken = default)
    {
        customer.Status = "new";
        _context.Customers.Add(customer);
        await _context.SaveChangesAsync(cancellationToken);
        return customer;
    }

    public async Task SaveAsync(Customer customer, CancellationToken cancellationToken = default)
    {
        customer.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
    }
}
