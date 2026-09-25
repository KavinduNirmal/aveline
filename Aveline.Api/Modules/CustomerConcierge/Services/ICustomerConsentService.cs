using Aveline.Api.Modules.CustomerConcierge.DTOs;
using Aveline.Api.Modules.Privacy.Services;

namespace Aveline.Api.Modules.CustomerConcierge.Services;

/// <summary>Reads and updates a customer's data-processing consent.</summary>
public interface ICustomerConsentService
{
    /// <summary>Returns the consent status for an org + customer (an absent row reads as <c>pending</c>).</summary>
    Task<CustomerConsentDto> GetAsync(Guid orgId, Guid customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the consent status (pending | granted | revoked). Returns the updated consent.
    /// An unknown status throws <see cref="Models.InvalidConsentStatusException"/>.
    /// </summary>
    Task<CustomerConsentDto> UpdateAsync(
        Guid orgId,
        Guid customerId,
        string status,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the consent status and records <b>who</b> did it (plan §5.4, §9.2). The actor kind is
    /// what distinguishes a staff revocation from a customer OTP revocation in the audit, so a caller
    /// on an authenticated surface must use this overload rather than the anonymous one.
    /// </summary>
    Task<CustomerConsentDto> UpdateAsync(
        Guid orgId,
        Guid customerId,
        string status,
        ConsentActor? actor,
        CancellationToken cancellationToken = default);
}
