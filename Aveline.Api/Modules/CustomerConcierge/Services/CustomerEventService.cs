using Aveline.Api.Modules.CustomerConcierge.DTOs;
using Aveline.Api.Modules.CustomerConcierge.Models;
using Aveline.Api.Modules.CustomerConcierge.Repositories;

namespace Aveline.Api.Modules.CustomerConcierge.Services;

public class CustomerEventService : ICustomerEventService
{
    private readonly ICustomerEventRepository _events;

    public CustomerEventService(ICustomerEventRepository events) => _events = events;

    public async Task<CustomerEventDto> AddAsync(Guid customerId, AddEventRequest request, CancellationToken cancellationToken = default)
    {
        var customerEvent = await _events.AddAsync(new CustomerEvent
        {
            OrganizationId = request.OrganizationId,
            CustomerId = customerId,
            EventType = request.EventType,
            EventDate = request.EventDate,
            Description = request.Description,
            IsActive = true,
        }, cancellationToken);

        return CustomerEventDto.From(customerEvent);
    }

    public async Task<IReadOnlyList<CustomerEventDto>> ListAsync(Guid orgId, Guid customerId, CancellationToken cancellationToken = default)
        => (await _events.ListByCustomerAsync(orgId, customerId, cancellationToken))
            .Select(CustomerEventDto.From)
            .ToList();
}
