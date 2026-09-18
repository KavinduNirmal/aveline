using Aveline.Api.Modules.CustomerConcierge.DTOs;
using Aveline.Api.Modules.CustomerConcierge.Models;
using Aveline.Api.Modules.CustomerConcierge.Repositories;

namespace Aveline.Api.Modules.CustomerConcierge.Services;

public class CustomerInteractionService : ICustomerInteractionService
{
    private readonly ICustomerInteractionRepository _interactions;

    public CustomerInteractionService(ICustomerInteractionRepository interactions) => _interactions = interactions;

    public async Task<CustomerInteractionDto> RecordAsync(
        Guid orgId,
        Guid customerId,
        string channel,
        string direction,
        string? messageContent,
        string? parsedIntentJson = null,
        Guid? staffMemberId = null,
        CancellationToken cancellationToken = default)
    {
        var interaction = await _interactions.AddAsync(new CustomerInteraction
        {
            OrganizationId = orgId,
            CustomerId = customerId,
            Channel = channel,
            Direction = direction,
            MessageContent = messageContent,
            ParsedIntentJson = parsedIntentJson ?? "{}",
            StaffMemberId = staffMemberId,
        }, cancellationToken);

        return CustomerInteractionDto.From(interaction);
    }
}
