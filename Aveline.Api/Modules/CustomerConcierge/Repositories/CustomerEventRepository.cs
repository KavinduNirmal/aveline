using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.CustomerConcierge.Models;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.CustomerConcierge.Repositories;

public class CustomerEventRepository : ICustomerEventRepository
{
    private readonly AppDbContext _context;

    public CustomerEventRepository(AppDbContext context) => _context = context;

    public async Task<CustomerEvent> AddAsync(CustomerEvent customerEvent, CancellationToken cancellationToken = default)
    {
        _context.CustomerEvents.Add(customerEvent);
        await _context.SaveChangesAsync(cancellationToken);
        return customerEvent;
    }

    public async Task<IReadOnlyList<CustomerEvent>> ListByCustomerAsync(
        Guid orgId,
        Guid customerId,
        CancellationToken cancellationToken = default)
        => await _context.CustomerEvents
            .Where(e => e.OrganizationId == orgId && e.CustomerId == customerId && e.IsActive)
            .OrderBy(e => e.EventDate)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<CustomerEvent>> FindDueForReminderAsync(
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken = default)
        => await _context.CustomerEvents
            .Include(e => e.Customer)
            .Where(e => e.IsActive && e.ReminderSentAt == null && e.EventDate >= from && e.EventDate <= to)
            .OrderBy(e => e.EventDate)
            .ToListAsync(cancellationToken);

    public async Task MarkReminderSentAsync(
        Guid eventId,
        DateTime sentAt,
        CancellationToken cancellationToken = default)
    {
        var customerEvent = await _context.CustomerEvents.FindAsync([eventId], cancellationToken);
        if (customerEvent is null)
        {
            return;
        }

        customerEvent.ReminderSentAt = sentAt;
        customerEvent.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
    }
}
