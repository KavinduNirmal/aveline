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

    /// <summary>
    /// Claims the disclosure slot with a single conditional UPDATE. The <c>ExecuteUpdate</c> path is
    /// the production one: it compiles to <c>UPDATE ... WHERE ... AND "DisclosureShownAt" IS NULL</c>
    /// and the affected-row count is the atomic answer, so two concurrent inbound messages cannot
    /// both win. The non-relational fallback exists only for the in-memory test provider, which does
    /// not support <c>ExecuteUpdate</c>; it is a read-check-write and is therefore not atomic, which
    /// is acceptable for a single-threaded test database and is never used in production.
    /// </summary>
    public async Task<bool> TryClaimDisclosureAsync(
        Guid orgId,
        Guid customerId,
        string disclosureVersion,
        DateTime shownAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(disclosureVersion);

        if (_context.Database.IsRelational())
        {
            var affected = await _context.CustomerConsents
                .Where(c => c.OrganizationId == orgId
                            && c.CustomerId == customerId
                            && c.DisclosureShownAt == null)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(c => c.DisclosureShownAt, shownAt)
                        .SetProperty(c => c.DisclosureVersion, disclosureVersion)
                        .SetProperty(c => c.UpdatedAt, shownAt),
                    cancellationToken);

            return affected > 0;
        }

        var row = await GetForCustomerAsync(orgId, customerId, cancellationToken);
        if (row is null || row.DisclosureShownAt is not null)
        {
            return false;
        }

        row.DisclosureShownAt = shownAt;
        row.DisclosureVersion = disclosureVersion;
        row.UpdatedAt = shownAt;
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public async Task ReleaseDisclosureAsync(
        Guid orgId,
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        if (_context.Database.IsRelational())
        {
            await _context.CustomerConsents
                .Where(c => c.OrganizationId == orgId && c.CustomerId == customerId)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(c => c.DisclosureShownAt, (DateTime?)null)
                        .SetProperty(c => c.DisclosureVersion, (string?)null)
                        .SetProperty(c => c.UpdatedAt, now),
                    cancellationToken);
            return;
        }

        var row = await GetForCustomerAsync(orgId, customerId, cancellationToken);
        if (row is null)
        {
            return;
        }

        row.DisclosureShownAt = null;
        row.DisclosureVersion = null;
        row.UpdatedAt = now;
        await _context.SaveChangesAsync(cancellationToken);
    }
}
