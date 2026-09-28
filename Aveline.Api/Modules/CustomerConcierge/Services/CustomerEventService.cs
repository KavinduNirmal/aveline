using Aveline.Api.Modules.CustomerConcierge.DTOs;
using Aveline.Api.Modules.CustomerConcierge.Models;
using Aveline.Api.Modules.CustomerConcierge.Repositories;

namespace Aveline.Api.Modules.CustomerConcierge.Services;

public class CustomerEventService : ICustomerEventService
{
    private readonly ICustomerEventRepository _events;
    private readonly IConsentGateService _consentGate;

    public CustomerEventService(
        ICustomerEventRepository events,
        IConsentGateService consentGate)
    {
        _events = events;
        _consentGate = consentGate;
    }

    public async Task<CustomerEventDto?> AddAsync(Guid customerId, AddEventRequest request, CancellationToken cancellationToken = default)
    {
        // §5.5: an event is customer personal data (a wedding date is sensitive). A revoked
        // customer gets no new row; an unreadable consent store fails closed.
        var decision = await _consentGate.CheckAsync(request.OrganizationId, customerId, cancellationToken);
        if (!decision.ShouldProcess)
        {
            return null;
        }

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
