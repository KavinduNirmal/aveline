using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.CustomerConcierge.Common;
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

    public async Task<IReadOnlyList<Customer>> ListMatchesAsync(
        Guid orgId,
        string? name,
        string? phoneNumber,
        int limit,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Customer> query = _context.Customers.Where(c => c.OrganizationId == orgId);

        if (!string.IsNullOrWhiteSpace(name))
        {
            var lower = name.Trim().ToLowerInvariant();
            query = query.Where(c => c.FullName != null && c.FullName.ToLower().Contains(lower));
        }

        if (!string.IsNullOrWhiteSpace(phoneNumber))
        {
            var candidates = PhoneCandidates(phoneNumber);
            query = query.Where(c => candidates.Contains(c.PhoneNumber));
        }

        // No criteria (or only whitespace) -> nothing to search for; do not scan the org.
        if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(phoneNumber))
        {
            return [];
        }

        return await query
            .OrderBy(c => c.FullName)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

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

    /// <summary>
    /// The distinct phone forms to match a query against: its E.164 normalization plus the raw
    /// query, so a locally-formatted query can still find canonically-stored rows (and vice
    /// versa).
    /// </summary>
    private static List<string> PhoneCandidates(string phoneNumber)
    {
        var normalized = PhoneNormalizer.ToE164(phoneNumber);
        var candidates = new List<string>(2);
        if (normalized is not null)
        {
            candidates.Add(normalized);
        }

        if (!string.Equals(phoneNumber, normalized, StringComparison.Ordinal))
        {
            candidates.Add(phoneNumber);
        }

        return candidates;
    }
}
