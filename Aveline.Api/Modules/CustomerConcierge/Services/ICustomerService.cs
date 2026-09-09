using Aveline.Api.Modules.CustomerConcierge.DTOs;

namespace Aveline.Api.Modules.CustomerConcierge.Services;

/// <summary>Customer profile identification and retrieval.</summary>
public interface ICustomerService
{
    /// <summary>
    /// Returns the customer for <paramref name="phoneNumber"/> in the org, creating a new
    /// <c>status = new</c> profile (plus a <c>pending</c> consent row) when none exists.
    /// </summary>
    Task<CustomerProfileDto> IdentifyOrCreateAsync(
        Guid orgId,
        string phoneNumber,
        string? fullName = null,
        CancellationToken cancellationToken = default);

    /// <summary>Returns a customer's full profile, or null when not found.</summary>
    Task<CustomerProfileDto?> GetProfileAsync(
        Guid orgId,
        Guid customerId,
        CancellationToken cancellationToken = default);
}
