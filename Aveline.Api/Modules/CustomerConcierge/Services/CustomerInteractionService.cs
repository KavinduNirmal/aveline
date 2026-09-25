using Aveline.Api.Modules.CustomerConcierge.DTOs;
using Aveline.Api.Modules.CustomerConcierge.Models;
using Aveline.Api.Modules.CustomerConcierge.Repositories;

namespace Aveline.Api.Modules.CustomerConcierge.Services;

public class CustomerInteractionService : ICustomerInteractionService
{
    private readonly ICustomerInteractionRepository _interactions;
    private readonly IConsentGateService _consentGate;

    public CustomerInteractionService(
        ICustomerInteractionRepository interactions,
        IConsentGateService consentGate)
    {
        _interactions = interactions;
        _consentGate = consentGate;
    }

    public async Task<CustomerInteractionDto?> RecordAsync(
        Guid orgId,
        Guid customerId,
        string channel,
        string direction,
        string? messageContent,
        string? parsedIntentJson = null,
        Guid? staffMemberId = null,
        CancellationToken cancellationToken = default)
    {
        // §5.5: the interaction log is customer personal data. A revoked customer gets no new row,
        // and an unreadable consent store fails closed rather than writing one.
        var decision = await _consentGate.CheckAsync(orgId, customerId, cancellationToken);
        if (!decision.ShouldProcess)
        {
            return null;
        }

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
