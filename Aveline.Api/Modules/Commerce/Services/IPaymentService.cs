using Aveline.Api.Modules.Commerce.DTOs;

namespace Aveline.Api.Modules.Commerce.Services;

public interface IPaymentService
{
    Task<PaymentResponseDto> GeneratePaymentRequestAsync(Guid organizationId, GeneratePaymentRequestDto dto, CancellationToken ct = default);
    Task<PaymentResponseDto> ConfirmPaymentAsync(Guid organizationId, Guid paymentId, ConfirmPaymentDto dto, CancellationToken ct = default);
    /// <summary>
    /// Refunds a confirmed payment and records the refund on the boutique's takings ledger.
    /// </summary>
    /// <param name="reason">
    /// The operator's own words. They become the ledger entry's reason; when they are too short to
    /// satisfy the ledger's rule a stated fallback is used, so a refund is never lost because
    /// somebody typed a short note.
    /// </param>
    /// <param name="refundedByUserId">
    /// Who issued the refund. **This is the one payment verb that always has a person behind it** —
    /// the route is guarded by `payments:refund` — so the journal can and must attribute it. A blank
    /// value is recorded as unattributed rather than as a fabricated user.
    /// </param>
    Task<PaymentResponseDto> RefundPaymentAsync(Guid organizationId, Guid paymentId, string? reason, CancellationToken ct = default, Guid? refundedByUserId = null);
    Task<PaymentResponseDto?> GetPaymentByIdAsync(Guid id, Guid organizationId, CancellationToken ct = default);
    Task<PaymentResponseDto?> GetPaymentByOrderIdAsync(Guid orderId, Guid organizationId, CancellationToken ct = default);
    Task<PagedResult<PaymentResponseDto>> ListPaymentsAsync(Guid organizationId, PaymentQueryParametersDto query, CancellationToken ct = default);
}
