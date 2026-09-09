using Aveline.Api.Modules.CustomerConcierge.Models;

namespace Aveline.Api.Modules.CustomerConcierge.Repositories;

/// <summary>Data access for <see cref="CustomerInteraction"/> records. Tenant-scoped.</summary>
public interface ICustomerInteractionRepository
{
    /// <summary>Creates an interaction record.</summary>
    Task<CustomerInteraction> AddAsync(CustomerInteraction interaction, CancellationToken cancellationToken = default);

    /// <summary>Returns a customer's interactions, newest first.</summary>
    Task<IReadOnlyList<CustomerInteraction>> ListByCustomerAsync(
        Guid orgId,
        Guid customerId,
        CancellationToken cancellationToken = default);
}
