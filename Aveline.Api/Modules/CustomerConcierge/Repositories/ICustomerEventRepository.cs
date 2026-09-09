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
}
