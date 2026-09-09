using Aveline.Api.Modules.CustomerConcierge.DTOs;
using Aveline.Api.Modules.CustomerConcierge.Models;
using Aveline.Api.Modules.CustomerConcierge.Repositories;

namespace Aveline.Api.Modules.CustomerConcierge.Services;

public class CustomerService : ICustomerService
{
    private readonly ICustomerRepository _customers;
    private readonly ICustomerConsentRepository _consent;
    private readonly ICustomerTagRepository _tags;

    public CustomerService(
        ICustomerRepository customers,
        ICustomerConsentRepository consent,
        ICustomerTagRepository tags)
    {
        _customers = customers;
        _consent = consent;
        _tags = tags;
    }

    public async Task<CustomerProfileDto> IdentifyOrCreateAsync(
        Guid orgId,
        string phoneNumber,
        string? fullName = null,
        CancellationToken cancellationToken = default)
    {
        var customer = await _customers.GetByPhoneAsync(orgId, phoneNumber, cancellationToken);
        if (customer is not null)
        {
            if (!string.IsNullOrWhiteSpace(fullName) && string.IsNullOrWhiteSpace(customer.FullName))
            {
                customer.FullName = fullName;
                await _customers.SaveAsync(customer, cancellationToken);
            }
            return await BuildProfileAsync(orgId, customer, cancellationToken);
        }

        var created = await _customers.AddAsync(new Customer
        {
            OrganizationId = orgId,
            PhoneNumber = phoneNumber,
            FullName = fullName,
            Status = "new",
        }, cancellationToken);

        await _consent.AddAsync(new CustomerConsent
        {
            OrganizationId = orgId,
            CustomerId = created.Id,
            ConsentStatus = "pending",
        }, cancellationToken);

        return await BuildProfileAsync(orgId, created, cancellationToken);
    }

    public async Task<CustomerProfileDto?> GetProfileAsync(
        Guid orgId,
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        var customer = await _customers.GetAsync(orgId, customerId, cancellationToken);
        return customer is null ? null : await BuildProfileAsync(orgId, customer, cancellationToken);
    }

    private async Task<CustomerProfileDto> BuildProfileAsync(
        Guid orgId,
        Customer customer,
        CancellationToken cancellationToken)
    {
        var tags = await _tags.ListByCustomerAsync(orgId, customer.Id, cancellationToken);
        var consent = await _consent.GetForCustomerAsync(orgId, customer.Id, cancellationToken);
        return CustomerProfileDto.From(customer, tags, consent?.ConsentStatus ?? "unknown");
    }
}
