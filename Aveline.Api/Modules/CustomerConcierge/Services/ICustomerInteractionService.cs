using Aveline.Api.Modules.CustomerConcierge.DTOs;

namespace Aveline.Api.Modules.CustomerConcierge.Services;

/// <summary>Records customer interactions (inbound/outbound messages).</summary>
public interface ICustomerInteractionService
{
    /// <summary>Records an interaction for a customer and returns it.</summary>
    Task<CustomerInteractionDto> RecordAsync(
        Guid orgId,
        Guid customerId,
        string channel,
        string direction,
        string? messageContent,
        string? parsedIntentJson = null,
        Guid? staffMemberId = null,
        CancellationToken cancellationToken = default);
}
