using Aveline.Api.Modules.CustomerConcierge.Models;

namespace Aveline.Api.Modules.CustomerConcierge.Repositories;

/// <summary>
/// Data access for <see cref="Customer"/> records. Tenant-scoped and soft-delete aware
/// (the global query filter excludes soft-deleted rows).
/// </summary>
public interface ICustomerRepository
{
    /// <summary>Returns a customer by id within the org, or null.</summary>
    Task<Customer?> GetAsync(Guid orgId, Guid id, CancellationToken cancellationToken = default);

    /// <summary>Returns the active customer with the given phone number in the org, or null.</summary>
    Task<Customer?> GetByPhoneAsync(Guid orgId, string phoneNumber, CancellationToken cancellationToken = default);

    /// <summary>Creates a new customer with <c>status = new</c>.</summary>
    Task<Customer> AddAsync(Customer customer, CancellationToken cancellationToken = default);

    /// <summary>Persists changes (bumps <see cref="Customer.UpdatedAt"/>).</summary>
    Task SaveAsync(Customer customer, CancellationToken cancellationToken = default);
}
