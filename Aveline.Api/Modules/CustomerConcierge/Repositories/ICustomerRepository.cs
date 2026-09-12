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

    /// <summary>
    /// Returns up to <paramref name="limit"/> active customers in the org matching an optional
    /// case-insensitive name fragment, phone number (compared in exact and E.164 form), and/or
    /// email. At least one of name / phone / email must be set.
    /// </summary>
    Task<IReadOnlyList<Customer>> ListMatchesAsync(
        Guid orgId,
        string? name,
        string? phoneNumber,
        int limit,
        CancellationToken cancellationToken = default,
        string? email = null);

    /// <summary>Creates a new customer with <c>status = new</c>.</summary>
    Task<Customer> AddAsync(Customer customer, CancellationToken cancellationToken = default);

    /// <summary>Persists changes (bumps <see cref="Customer.UpdatedAt"/>).</summary>
    Task SaveAsync(Customer customer, CancellationToken cancellationToken = default);
}
