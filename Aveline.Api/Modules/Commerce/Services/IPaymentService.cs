using Aveline.Api.Modules.Commerce.DTOs;

namespace Aveline.Api.Modules.Commerce.Services;

public interface IPaymentService
{
    Task<PaymentResponseDto> GeneratePaymentRequestAsync(Guid organizationId, GeneratePaymentRequestDto dto, CancellationToken ct = default);
    Task<PaymentResponseDto> ConfirmPaymentAsync(Guid organizationId, Guid paymentId, ConfirmPaymentDto dto, CancellationToken ct = default);
    Task<PaymentResponseDto> RefundPaymentAsync(Guid organizationId, Guid paymentId, string? reason, CancellationToken ct = default);
    Task<PaymentResponseDto?> GetPaymentByIdAsync(Guid id, Guid organizationId, CancellationToken ct = default);
    Task<PagedResult<PaymentResponseDto>> ListPaymentsAsync(Guid organizationId, PaymentQueryParametersDto query, CancellationToken ct = default);
}
