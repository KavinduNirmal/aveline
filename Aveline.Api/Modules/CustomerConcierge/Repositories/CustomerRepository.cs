using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.CustomerConcierge.Common;
using Aveline.Api.Modules.CustomerConcierge.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aveline.Api.Modules.CustomerConcierge.Repositories;

public class CustomerRepository : ICustomerRepository
{
    private readonly AppDbContext _context;
    private readonly ILogger<CustomerRepository>? _logger;

    public CustomerRepository(AppDbContext context, ILogger<CustomerRepository>? logger = null)
        => (_context, _logger) = (context, logger);

    public async Task<Customer?> GetAsync(Guid orgId, Guid id, CancellationToken cancellationToken = default)
        => await _context.Customers
            .Include(c => c.Preferences)
            .Include(c => c.Tags)
            .FirstOrDefaultAsync(c => c.OrganizationId == orgId && c.Id == id, cancellationToken);

    public async Task<Customer?> GetByPhoneAsync(Guid orgId, string phoneNumber, CancellationToken cancellationToken = default)
    {
        // Matched on candidates rather than the raw string. The write paths disagree about format -
        // CustomerTenantService normalizes to E.164 while CustomerService stored whatever it was
        // handed - and Meta sends `from` as digits with no plus, so an exact comparison missed
        // customers added from the dashboard. A miss here is not cosmetic: WebhookEndpoints reads a
        // null customer as "unknown number", and that customer then gets no first-contact
        // disclosure and their WhatsApp thread is never linked to their profile (2026-09-26).
        var candidates = PhoneCandidates(phoneNumber);

        // Ordered, because more than one row can match: the same person can exist as `+94771234567`
        // and `0771234567`, and the unique index on (OrganizationId, PhoneNumber) only compares the
        // literal string. Both are candidates for a locally-formatted query, and without an ordering
        // the choice between them was arbitrary - which decides whose consent row a first-contact
        // disclosure stamps and which profile an inbound thread resolves to. The oldest row is the
        // one carrying the history, so it wins; Id breaks a tie between rows created together.
        var matches = await _context.Customers
            .Include(c => c.Preferences)
            .Include(c => c.Tags)
            .Where(c => c.OrganizationId == orgId && candidates.Contains(c.PhoneNumber))
            .OrderBy(c => c.CreatedAt)
            .ThenBy(c => c.Id)
            .Take(2)
            .ToListAsync(cancellationToken);

        if (matches.Count == 0)
        {
            return null;
        }

        if (matches.Count > 1 && _logger is not null)
        {
            // Identifiers only, never the number. This is the only signal that a duplicate profile
            // exists until a merge path does, and duplicates are otherwise invisible: the second row
            // is simply never returned.
            _logger.LogInformation(
                "A phone number matched more than one customer profile; the oldest is used and the others are unreachable until merged. organizationId={OrganizationId} customerId={CustomerId}",
                orgId, matches[0].Id);
        }

        return matches[0];
    }

    public async Task<IReadOnlyList<Customer>> ListMatchesAsync(
        Guid orgId,
        string? name,
        string? phoneNumber,
        int limit,
        CancellationToken cancellationToken = default,
        string? email = null)
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

        if (!string.IsNullOrWhiteSpace(email))
        {
            var lowerEmail = email.Trim().ToLowerInvariant();
            query = query.Where(c => c.Email != null && c.Email.ToLower() == lowerEmail);
        }

        // No criteria (or only whitespace) -> nothing to search for; do not scan the org.
        if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(phoneNumber) && string.IsNullOrWhiteSpace(email))
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
