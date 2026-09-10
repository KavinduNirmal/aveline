using Aveline.Api.Modules.CustomerConcierge.Models;

namespace Aveline.Api.Modules.CustomerConcierge.Repositories;

/// <summary>Data access for <see cref="CustomerEvent"/> records. Tenant-scoped.</summary>
public interface ICustomerEventRepository
{
    /// <summary>Creates an event.</summary>
    Task<CustomerEvent> AddAsync(CustomerEvent customerEvent, CancellationToken cancellationToken = default);

    /// <summary>Returns a customer's active events ordered by upcoming date first.</summary>
    Task<IReadOnlyList<CustomerEvent>> ListByCustomerAsync(
        Guid orgId,
        Guid customerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns active events (across orgs) whose reminder is still due: not yet reminded and
    /// with an event date inside <c>[from, to]</c>. The customer navigation is loaded so the
    /// reminder can name the customer.
    /// </summary>
    Task<IReadOnlyList<CustomerEvent>> FindDueForReminderAsync(
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken = default);

    /// <summary>Marks an event's reminder as sent to prevent duplicate reminders.</summary>
    Task MarkReminderSentAsync(
        Guid eventId,
        DateTime sentAt,
        CancellationToken cancellationToken = default);
}
