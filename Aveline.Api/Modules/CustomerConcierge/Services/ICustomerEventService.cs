using Aveline.Api.Modules.CustomerConcierge.DTOs;

namespace Aveline.Api.Modules.CustomerConcierge.Services;

/// <summary>Customer events (weddings, birthdays, etc.) add + list.</summary>
public interface ICustomerEventService
{
    /// <summary>Adds an event for a customer.</summary>
    Task<CustomerEventDto> AddAsync(Guid customerId, AddEventRequest request, CancellationToken cancellationToken = default);

    /// <summary>Lists a customer's active events, upcoming first.</summary>
    Task<IReadOnlyList<CustomerEventDto>> ListAsync(Guid orgId, Guid customerId, CancellationToken cancellationToken = default);
}
