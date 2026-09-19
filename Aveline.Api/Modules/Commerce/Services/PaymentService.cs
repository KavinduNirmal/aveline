using Aveline.Api.Modules.Commerce.DTOs;
using Aveline.Api.Modules.Commerce.Models;
using Aveline.Api.Modules.Commerce.Repositories;

namespace Aveline.Api.Modules.Commerce.Services;

public class PaymentService : IPaymentService
{
    private readonly IPaymentRepository _paymentRepository;
    private readonly IOrderRepository _orderRepository;

    public PaymentService(IPaymentRepository paymentRepository, IOrderRepository orderRepository)
    {
        _paymentRepository = paymentRepository ?? throw new ArgumentNullException(nameof(paymentRepository));
        _orderRepository = orderRepository ?? throw new ArgumentNullException(nameof(orderRepository));
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
        return MapToDto(payment);
    }

    public async Task<PaymentResponseDto> RefundPaymentAsync(
        Guid organizationId,
        Guid paymentId,
        string? reason,
        CancellationToken ct = default)
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
