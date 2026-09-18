using Aveline.Api.Modules.CustomerConcierge.Models;

namespace Aveline.Api.Modules.CustomerConcierge.Repositories;

/// <summary>Data access for <see cref="CustomerTag"/> records. Tenant-scoped.</summary>
public interface ICustomerTagRepository
{
    /// <summary>Creates a tag (ignored if the same tag already exists for the customer).</summary>
    Task AddAsync(CustomerTag tag, CancellationToken cancellationToken = default);

    /// <summary>Returns a customer's tags.</summary>
    Task<IReadOnlyList<string>> ListByCustomerAsync(
        Guid orgId,
        Guid customerId,
        CancellationToken cancellationToken = default);
}
