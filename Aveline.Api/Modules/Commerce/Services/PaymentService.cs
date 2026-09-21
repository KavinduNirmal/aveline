using Aveline.Api.Modules.Commerce.DTOs;
using Aveline.Api.Modules.Commerce.Models;
using Aveline.Api.Modules.Commerce.Repositories;

namespace Aveline.Api.Modules.Commerce.Services;

public class PaymentService : IPaymentService
{
    private readonly IPaymentRepository _paymentRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly IBoutiqueSaleLedgerService _ledger;

    public PaymentService(
        IPaymentRepository paymentRepository,
        IOrderRepository orderRepository,
        IBoutiqueSaleLedgerService ledger)
    {
        _paymentRepository = paymentRepository ?? throw new ArgumentNullException(nameof(paymentRepository));
        _orderRepository = orderRepository ?? throw new ArgumentNullException(nameof(orderRepository));
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
    }

    public async Task<PaymentResponseDto> GeneratePaymentRequestAsync(
        Guid organizationId,
        GeneratePaymentRequestDto dto,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        if (dto.Amount <= 0)
        {
            throw new ArgumentException("Payment amount must be greater than zero.", nameof(dto));
        }

        var order = await _orderRepository.GetByIdAsync(dto.OrderId, organizationId, ct);
        if (order is null)
        {
            throw new KeyNotFoundException($"Order '{dto.OrderId}' not found.");
        }

        var paymentId = Guid.NewGuid();
        var shortRef = paymentId.ToString("N")[..8];
        var checkoutUrl = $"https://pay.aveline.boutique/checkout/{shortRef}";

        var payment = new Payment
        {
            Id = paymentId,
            OrganizationId = organizationId,
            OrderId = dto.OrderId,
            Amount = Math.Round(dto.Amount, 2),
            PaymentType = string.IsNullOrWhiteSpace(dto.PaymentType) ? "full" : dto.PaymentType.Trim().ToLowerInvariant(),
            PaymentMethod = string.IsNullOrWhiteSpace(dto.PaymentMethod) ? "online" : dto.PaymentMethod.Trim().ToLowerInvariant(),
            Status = "pending",
            PaymentLink = checkoutUrl,
            ExpiresAt = DateTime.UtcNow.AddHours(24),
            CreatedAt = DateTime.UtcNow
        };

        order.Status = "payment_requested";
        order.UpdatedAt = DateTime.UtcNow;

        await _paymentRepository.AddAsync(payment, ct);
        await _orderRepository.UpdateAsync(order, ct);

        return MapToDto(payment);
    }

    public async Task<PaymentResponseDto> ConfirmPaymentAsync(
        Guid organizationId,
        Guid paymentId,
        ConfirmPaymentDto dto,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        if (string.IsNullOrWhiteSpace(dto.GatewayTransactionId))
        {
            throw new ArgumentException("Gateway transaction ID is required.", nameof(dto));
        }

        var payment = await _paymentRepository.GetByIdAsync(paymentId, organizationId, ct);
        if (payment is null)
        {
            throw new KeyNotFoundException($"Payment '{paymentId}' not found.");
        }

        if (payment.Status.Equals("confirmed", StringComparison.OrdinalIgnoreCase))
        {
            return MapToDto(payment);
        }

        if (!payment.Status.Equals("pending", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Payment '{paymentId}' cannot be confirmed because it has status '{payment.Status}'.");
        }

        payment.Status = "confirmed";
        payment.GatewayTransactionId = dto.GatewayTransactionId.Trim();
        payment.ConfirmedAt = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(dto.PaymentMethod))
        {
            payment.PaymentMethod = dto.PaymentMethod.Trim().ToLowerInvariant();
        }

        var order = payment.Order ?? await _orderRepository.GetByIdAsync(payment.OrderId, organizationId, ct);
        if (order is not null)
        {
            order.Status = "payment_confirmed";
            order.UpdatedAt = DateTime.UtcNow;
            await _orderRepository.UpdateAsync(order, ct);
        }

        await _paymentRepository.UpdateAsync(payment, ct);

        // The confirmation is the only durable signal that money arrived, so it is the one event
        // that may be recorded as `Verified`. `SourceRef` is the payment id, so a retried
        // confirmation cannot double-book — and the two early returns above mean an already
        // confirmed payment never reaches here at all.
        //
        // The actor is null: nothing in the order or payment flow resolves one (`OrdersController`
        // passes `createdBy: null`), so naming a user here would be a fabrication. `OrderPayment` is
        // therefore one of the sources the ledger permits an unattributed row on.
        await _ledger.RecordAsync(new RecordBoutiqueSaleCommand(
            OrganizationId: organizationId,
            Amount: payment.Amount,
            Reason: $"Payment of {payment.Amount:0.00} {payment.PaymentMethod} confirmed for order {payment.OrderId}.",
            Kind: BoutiqueSaleEntryKind.PaymentReceived,
            ChargeBasis: BoutiqueSaleChargeBasis.Verified,
            SourceKind: BoutiqueSaleSourceKind.OrderPayment,
            SourceRef: $"payment:{payment.Id}",
            // The register is ordered by occurrence, so it must be the confirmation time and not
            // the time this row happened to be written.
            OccurredAt: payment.ConfirmedAt.Value,
            RecordedByUserId: null,
            OrderId: payment.OrderId,
            PaymentId: payment.Id), ct);

        return MapToDto(payment);
    }

    public async Task<PaymentResponseDto> RefundPaymentAsync(
        Guid organizationId,
        Guid paymentId,
        string? reason,
        CancellationToken ct = default,
        Guid? refundedByUserId = null)
    {
        var payment = await _paymentRepository.GetByIdAsync(paymentId, organizationId, ct);
        if (payment is null)
        {
            throw new KeyNotFoundException($"Payment '{paymentId}' not found.");
        }

        if (!payment.Status.Equals("confirmed", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Payment '{paymentId}' cannot be refunded because it has status '{payment.Status}'. Only confirmed payments can be refunded.");
        }

        payment.Status = "refunded";
        await _paymentRepository.UpdateAsync(payment, ct);

        // The refund path previously accepted a `reason` and then discarded it: no row, no amount,
        // no timestamp. It now has a destination, which is the difference between a refund and an
        // unexplained balance change.
        //
        // The client's own words are used when they are long enough to satisfy the ledger's reason
        // rule, and a stated fallback is used when they are not — a refund is never lost merely
        // because the operator typed a short note.
        var note = (reason ?? string.Empty).Trim();
        var ledgerReason = note.Length >= BoutiqueSaleLedgerService.MinReasonLength
            ? note
            : $"Refund of {payment.Amount:0.00} for order {payment.OrderId}; no reason was recorded at the counter.";

        await _ledger.RecordAsync(new RecordBoutiqueSaleCommand(
            OrganizationId: organizationId,
            Amount: payment.Amount,
            // Stored positive; the sign comes from `Kind`, so a column sum cannot net it away.
            Reason: ledgerReason,
            Kind: BoutiqueSaleEntryKind.Refund,
            ChargeBasis: BoutiqueSaleChargeBasis.Verified,
            SourceKind: BoutiqueSaleSourceKind.Refund,
            // The payment can only be refunded once (the guard above refuses a second attempt), so
            // the payment id is a sufficient dedup identity.
            SourceRef: $"refund:{payment.Id}",
            OccurredAt: DateTime.UtcNow,
            // Unlike a confirmation, a refund is always issued by somebody: the route is guarded by
            // `payments:refund`. Recording who makes the journal answer "who sent this back?".
            RecordedByUserId: refundedByUserId is { } id && id != Guid.Empty ? id : null,
            OrderId: payment.OrderId,
            PaymentId: payment.Id), ct);

        return MapToDto(payment);
    }

    public async Task<PaymentResponseDto?> GetPaymentByIdAsync(
        Guid id,
        Guid organizationId,
        CancellationToken ct = default)
    {
        var payment = await _paymentRepository.GetByIdAsync(id, organizationId, ct);
        return payment is not null ? MapToDto(payment) : null;
    }

    public async Task<PaymentResponseDto?> GetPaymentByOrderIdAsync(
        Guid orderId,
        Guid organizationId,
        CancellationToken ct = default)
    {
        var payment = await _paymentRepository.GetByOrderIdAsync(orderId, organizationId, ct);
        return payment is not null ? MapToDto(payment) : null;
    }

    public async Task<PagedResult<PaymentResponseDto>> ListPaymentsAsync(
        Guid organizationId,
        PaymentQueryParametersDto query,
        CancellationToken ct = default)
    {
        var result = await _paymentRepository.ListAsync(organizationId, query, ct);
        return new PagedResult<PaymentResponseDto>
        {
            Items = result.Items.Select(MapToDto).ToList(),
            TotalCount = result.TotalCount,
            Page = result.Page,
            PageSize = result.PageSize
        };
    }

    private static PaymentResponseDto MapToDto(Payment payment)
    {
        return new PaymentResponseDto
        {
            Id = payment.Id,
            OrganizationId = payment.OrganizationId,
            OrderId = payment.OrderId,
            Amount = payment.Amount,
            PaymentType = payment.PaymentType,
            PaymentMethod = payment.PaymentMethod,
            Status = payment.Status,
            GatewayTransactionId = payment.GatewayTransactionId,
            PaymentLink = payment.PaymentLink,
            CreatedAt = payment.CreatedAt,
            ConfirmedAt = payment.ConfirmedAt,
            ExpiresAt = payment.ExpiresAt
        };
    }
}
