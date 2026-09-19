using Aveline.Api.Modules.Commerce.DTOs;
using Aveline.Api.Modules.Commerce.Models;

namespace Aveline.Api.Modules.Commerce.Repositories;

public interface IPaymentRepository
{
    Task<Payment?> GetByIdAsync(Guid id, Guid organizationId, CancellationToken ct = default);
    Task<Payment?> GetByOrderIdAsync(Guid orderId, Guid organizationId, CancellationToken ct = default);
    Task<PagedResult<Payment>> ListAsync(Guid organizationId, PaymentQueryParametersDto query, CancellationToken ct = default);
    Task<Payment> AddAsync(Payment payment, CancellationToken ct = default);
    Task<Payment> UpdateAsync(Payment payment, CancellationToken ct = default);
}
