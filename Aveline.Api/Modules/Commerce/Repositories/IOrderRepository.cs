using Aveline.Api.Modules.Commerce.DTOs;
using Aveline.Api.Modules.Commerce.Models;

namespace Aveline.Api.Modules.Commerce.Repositories;

public interface IOrderRepository
{
    Task<Order?> GetByIdAsync(Guid id, Guid organizationId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Order>> GetAllAsync(Guid organizationId, CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<Order> Items, int TotalCount)> GetPagedAsync(Guid organizationId, OrderQueryParametersDto query, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Order>> GetByCustomerIdAsync(Guid customerId, Guid organizationId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Order>> GetByStatusAsync(string status, Guid organizationId, CancellationToken cancellationToken = default);
    Task<Order> CreateAsync(Order order, CancellationToken cancellationToken = default);
    Task<Order> UpdateAsync(Order order, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, Guid organizationId, CancellationToken cancellationToken = default);
}
