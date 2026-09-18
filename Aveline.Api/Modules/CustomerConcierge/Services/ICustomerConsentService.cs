using Aveline.Api.Modules.CustomerConcierge.DTOs;

namespace Aveline.Api.Modules.CustomerConcierge.Services;

/// <summary>Reads and updates a customer's data-processing consent.</summary>
public interface ICustomerConsentService
{
    /// <summary>Returns the consent status for an org + customer (defaults to <c>pending</c>).</summary>
    Task<CustomerConsentDto> GetAsync(Guid orgId, Guid customerId, CancellationToken cancellationToken = default);

    /// <summary>Sets the consent status (granted | revoked). Returns the updated consent.</summary>
    Task<CustomerConsentDto> UpdateAsync(
        Guid orgId,
        Guid customerId,
        string status,
        CancellationToken cancellationToken = default);
}
