using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Commerce.DTOs;
using Aveline.Api.Modules.Commerce.Models;
using Aveline.Api.Modules.Commerce.Repositories;
using Aveline.Api.Modules.Payments;
using Aveline.Api.Modules.Payments.Domain;
using Aveline.Api.Modules.Payments.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Aveline.Api.Modules.Commerce.Services;

/// <summary>
/// The Commerce order checkout (plan §9.7, Phase 9). The route and DTO surface is unchanged so the
/// shipped clients do not move; what changed is who is believed.
/// </summary>
/// <remarks>
/// <para>
/// <b>Before Phase 9.</b> <c>GeneratePaymentRequestAsync</c> returned
/// <c>$"https://pay.aveline.boutique/checkout/{shortRef}"</c> — a URL no provider had ever issued
/// and no gateway could settle — and <c>ConfirmPaymentAsync</c> accepted any non-empty
/// <c>GatewayTransactionId</c> from the request body as proof that money arrived. The project's own
/// review records both: "**Not true as gateway integration** — the checkout URL is
/// <c>https://pay.aveline.boutique/checkout/{shortRef}</c> … <c>ConfirmPaymentAsync</c> accepts any
/// non-empty <c>GatewayTransactionId</c> with no signature/callback validation, and
/// <c>GatewayTransactionId</c> has a **non-unique** index"
/// (<c>docs/reports/PR-290-slice3-review.md:269</c>).
/// </para>
/// <para>
/// <b>After Phase 9.</b> Generation creates a <see cref="PaymentPurpose.CommerceOrder"/> intent
/// through <see cref="IPaymentIntentService"/> and returns the adapter's own URL (null when the
/// adapter settles in place). Confirmation polls that intent and maps the provider's verdict onto
/// the existing status column, so the caller's transaction id has no settlement power at all. The
/// row still records the provider's transaction reference, but only from the provider's answer.
/// </para>
/// <para>
/// <b>Two honest paths, and one rollback.</b> A provider-backed generation writes the adapter's URL
/// and a confirmation reads the intent. A generation with <b>no provider behind it</b> is the
/// counter payment: the shop takes the money at the desk and an operator records the reference, which
/// is the <c>manual</c> adapter's shape and the only payment whose reference a human supplies. Both
/// live in <see cref="CounterGeneratePaymentRequestAsync"/> and
/// <see cref="CounterConfirmPaymentAsync"/>.
/// </para>
/// <para>
/// <b>The one-release rollback (plan §8.4 S8).</b> With
/// <c>Payments:Commerce:UseProviderIntents=false</c> generation takes the pre-Phase-9 shape: a
/// fabricated URL, which is the one thing the counter path must not do. That is why the fabricated
/// URL exists in exactly one place, <see cref="FabricatedLegacyCheckoutUrl"/>, which nothing on the
/// provider path calls; delete the method, its call site and the setting together one release after
/// Phase 9 ships.
/// </para>
/// <para>
/// <b>Two ledgers, not one.</b> A Commerce confirmation writes the boutique's own takings register
/// (<c>BoutiqueSaleEntries</c>, "the shop's own takings, in their own table: a different economy
/// from the platform's <c>IncomeLedgerEntries</c>", <c>CommerceModule</c>). The platform's income
/// receipt for a <c>CommerceOrder</c> intent is the settlement path's business, and that path
/// currently stores the event unprocessed rather than acting on it
/// (<c>PaymentSettlementService</c>: "Settlement for purpose CommerceOrder is not implemented in
/// this phase"); Phase 9 does not change that, and this class does not write the platform ledger.
/// </para>
/// </remarks>
public class PaymentService : IPaymentService
{
    private readonly IPaymentRepository _paymentRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly IBoutiqueSaleLedgerService _ledger;
    private readonly IPaymentIntentService _intents;
    private readonly IOptions<PaymentsOptions> _options;
    private readonly AppDbContext _db;

    public PaymentService(
        IPaymentRepository paymentRepository,
        IOrderRepository orderRepository,
        IBoutiqueSaleLedgerService ledger,
        IPaymentIntentService intents,
        IOptions<PaymentsOptions> options,
        AppDbContext db)
    {
        _paymentRepository = paymentRepository ?? throw new ArgumentNullException(nameof(paymentRepository));
        _orderRepository = orderRepository ?? throw new ArgumentNullException(nameof(orderRepository));
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        _intents = intents ?? throw new ArgumentNullException(nameof(intents));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    private bool UseProviderIntents => _options.Value.Commerce.UseProviderIntents;

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

        if (!UseProviderIntents)
        {
            return await CounterGeneratePaymentRequestAsync(organizationId, dto, order, ct);
        }

        var amount = Math.Round(dto.Amount, 2);

        // The intent is the charge. `CommerceOrder` is what makes the purpose visible to the
        // settlement path and to reconciliation (plan §6.2, §9.7).
        //
        // The idempotency key is deliberately null. It would be wrong to mint one from the order id:
        // an order can legitimately need more than one charge (the first attempt fails, or a deposit
        // is taken and then the balance), while `FindReplayAsync` refuses a reused key for a
        // *different* amount rather than returning the first intent. The route carries no
        // `Idempotency-Key` header and this phase does not add one, so no key is passed, which is
        // exactly what the pre-Phase-9 path did: a retried request creates another provider charge.
        var intent = await _intents.CreateAsync(new CreatePaymentIntentCommand(
            OrganizationId: organizationId,
            Purpose: PaymentPurpose.CommerceOrder,
            Amount: Money.Lkr(amount),
            Description: $"Commerce order {order.Id:D} checkout ({DescribePaymentType(dto)}).",
            SkuCode: null,
            BlossomQuantity: null,
            IdempotencyKey: null,
            CreatedByUserId: null,
            ExpiresAt: null), ct);

        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            OrderId = dto.OrderId,
            Amount = amount,
            PaymentType = string.IsNullOrWhiteSpace(dto.PaymentType) ? "full" : dto.PaymentType.Trim().ToLowerInvariant(),
            PaymentMethod = string.IsNullOrWhiteSpace(dto.PaymentMethod) ? "online" : dto.PaymentMethod.Trim().ToLowerInvariant(),
            // At creation the row records only what the *creation call* established: a charge exists
            // and is `pending` from the shop's book's point of view. A provider that reports
            // `Succeeded` at creation is still `pending` here, because "confirmed" on this row means
            // "Aveline recorded the takings entry", and that happens on the confirmation poll.
            // `GatewayTransactionId` below carries the provider's own reference either way.
            Status = CommercePaymentStatus.FromProviderStatusAtCreation(intent.Status),
            // The provider's URL, or null when the provider has no hosted page (plan §9.7).
            PaymentLink = intent.CheckoutUrl,
            PaymentIntentId = intent.PaymentIntentId,
            GatewayTransactionId = intent.ProviderIntentId,
            ExpiresAt = intent.ExpiresAt ?? DateTime.UtcNow.AddHours(24),
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

        var payment = await _paymentRepository.GetByIdAsync(paymentId, organizationId, ct);
        if (payment is null)
        {
            throw new KeyNotFoundException($"Payment '{paymentId}' not found.");
        }

        if (UseProviderIntents && payment.PaymentIntentId is { } intentId)
        {
            return await ConfirmFromProviderAsync(organizationId, payment, intentId, dto, ct);
        }

        return await CounterConfirmPaymentAsync(organizationId, payment, dto, ct);
    }

    /// <summary>
    /// Marks a confirmed order payment returned and appends the boutique refund row.
    /// </summary>
    /// <remarks>
    /// <b>Unchanged by Phase 9, and deliberately not the provider path.</b> This remains the counter
    /// refund: it writes the shop's book and flips the column, exactly as before. A refund of a
    /// provider-collected charge goes through the provider-neutral intent route
    /// (<c>POST …/payment-intents/{id}/refund</c>), which asks the provider first (decision D8) and
    /// requires a live verified receipt. The plan's §9.7 table lists only generation, confirmation
    /// and the status/index changes for this phase, so this method is intentionally left alone; a
    /// provider-backed payment refunded here is therefore recorded locally without the provider being
    /// told, which is the same limitation the review recorded and remains for a later phase.
    /// </remarks>
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

        if (!payment.Status.Equals(CommercePaymentStatus.Confirmed, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Payment '{paymentId}' cannot be refunded because it has status '{payment.Status}'. Only confirmed payments can be refunded.");
        }

        payment.Status = CommercePaymentStatus.Refunded;
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

    /// <summary>
    /// The Phase 9 confirmation: a poll of the intent the charge was created through.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The route contract is intact — same path, same <c>PaymentResponseDto</c> body, still
    /// idempotent — but the caller's <c>GatewayTransactionId</c> is never consulted. The provider is
    /// the only authority on settlement (plan §9.7), so a 200 with <c>status: "pending"</c> is the
    /// honest answer when the provider has not settled, and a caller cannot upgrade it by claiming a
    /// transaction reference.
    /// </para>
    /// <para>
    /// <c>IPaymentIntentService.GetAsync</c> is the poll: it re-reads the provider's checkout URL
    /// and serves the intent's persisted status, which the webhook/settlement path is what moves
    /// (see <c>PaymentIntentService.ToViewAsync</c>). Polling the provider for a live status and
    /// persisting it is deliberately not done here — that would make the shop's confirmation the
    /// settlement writer, which is the coupling Phase 9 removes.
    /// </para>
    /// </remarks>
    private async Task<PaymentResponseDto> ConfirmFromProviderAsync(
        Guid organizationId,
        Payment payment,
        Guid paymentIntentId,
        ConfirmPaymentDto dto,
        CancellationToken ct)
    {
        var intent = await _intents.GetAsync(organizationId, paymentIntentId, ct);
        var mapped = CommercePaymentStatus.FromProviderStatus(intent.Status);

        // Already confirmed: this is a repeat poll of a settled charge, so the row, the order and
        // the takings entry all stand. Returning here is what keeps the route idempotent; the
        // takings entry can only exist because a previous call wrote it.
        if (payment.Status.Equals(CommercePaymentStatus.Confirmed, StringComparison.OrdinalIgnoreCase))
        {
            return MapToDto(payment);
        }

        if (payment.Status.Equals(CommercePaymentStatus.Refunded, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Payment '{payment.Id}' cannot be confirmed because it has status '{payment.Status}'.");
        }

        if (mapped != CommercePaymentStatus.Confirmed)
        {
            // Record what the provider currently reports (a failed charge is a fact worth keeping)
            // and tell the caller the truth rather than a fabricated settlement.
            if (!payment.Status.Equals(mapped, StringComparison.OrdinalIgnoreCase))
            {
                payment.Status = mapped;
                if (!string.IsNullOrWhiteSpace(dto.PaymentMethod))
                {
                    payment.PaymentMethod = dto.PaymentMethod.Trim().ToLowerInvariant();
                }

                await _paymentRepository.UpdateAsync(payment, ct);
            }

            return MapToDto(payment);
        }

        payment.Status = CommercePaymentStatus.Confirmed;
        // From the provider's answer, never the request body. An adapter that reports no provider
        // intent id leaves the column null rather than storing the caller's claim.
        payment.GatewayTransactionId = string.IsNullOrWhiteSpace(intent.ProviderIntentId)
            ? payment.GatewayTransactionId
            : intent.ProviderIntentId;
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

        await RecordOrderPaymentAsync(organizationId, payment, ct);

        return MapToDto(payment);
    }

    /// <summary>
    /// Appends the boutique takings row for a confirmed order payment, once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The confirmation is the only durable signal that money arrived, and after Phase 9 that signal
    /// is the provider's own verdict read through the intent — not the caller's word. The boutique
    /// takings register is the shop's own book, a different table and a different economy from the
    /// platform's <c>IncomeLedgerEntries</c> (<c>CommerceModule</c>), so this is not a second booking
    /// of the same money.
    /// </para>
    /// <para>
    /// <b>The existence check is on the payment, not on the ledger.</b> A provider that settles at
    /// creation marks the payment <c>confirmed</c> before this route is ever reached, and this class
    /// is the only writer of the order-payment row; without the check that path would read
    /// "confirmed" and write no takings entry at all. The check is by <c>PaymentId</c>, which is the
    /// column the ledger already carries for exactly this join.
    /// </para>
    /// <para>
    /// The actor is null: nothing in the order or payment flow resolves one (<c>OrdersController</c>
    /// passes <c>createdBy: null</c>), so naming a user here would be a fabrication. <c>OrderPayment</c>
    /// is therefore one of the sources the ledger permits an unattributed row on.
    /// </para>
    /// </remarks>
    private async Task RecordOrderPaymentAsync(
        Guid organizationId, Payment payment, CancellationToken ct)
    {
        var alreadyRecorded = await _db.BoutiqueSaleEntries
            .AsNoTracking()
            .AnyAsync(entry => entry.OrganizationId == organizationId
                && entry.PaymentId == payment.Id
                && entry.Kind == BoutiqueSaleEntryKind.PaymentReceived, ct);

        if (alreadyRecorded)
        {
            return;
        }

        var confirmedAt = payment.ConfirmedAt ?? DateTime.UtcNow;

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
            OccurredAt: confirmedAt,
            RecordedByUserId: null,
            OrderId: payment.OrderId,
            PaymentId: payment.Id), ct);
    }

    // ------------------------------------------------------- the counter path and the rollback
    //
    // `CounterGeneratePaymentRequestAsync` and `CounterConfirmPaymentAsync` are live: a payment with
    // no provider intent is a counter payment, taken at the desk and recorded by an operator, which
    // is the `manual` adapter's shape. `FabricatedLegacyCheckoutUrl` is the one-release rollback
    // (plan §8.4 S8): with `Payments:Commerce:UseProviderIntents=false` generation returns it, and it
    // is the only place in the codebase where a checkout URL is minted from a payment id. Delete the
    // method, its single call site, the setting and the rollback branch one release after Phase 9.

    /// <summary>
    /// The pre-Phase-9 generation: a URL Aveline invented for a payment id, with no provider charge
    /// behind it. Called **only** from the rollback branch, and deleted one release after Phase 9.
    /// </summary>
    private async Task<PaymentResponseDto> CounterGeneratePaymentRequestAsync(
        Guid organizationId,
        GeneratePaymentRequestDto dto,
        Order order,
        CancellationToken ct)
    {
        var paymentId = Guid.NewGuid();
        var shortRef = paymentId.ToString("N")[..8];

        var payment = new Payment
        {
            Id = paymentId,
            OrganizationId = organizationId,
            OrderId = dto.OrderId,
            Amount = Math.Round(dto.Amount, 2),
            PaymentType = string.IsNullOrWhiteSpace(dto.PaymentType) ? "full" : dto.PaymentType.Trim().ToLowerInvariant(),
            PaymentMethod = string.IsNullOrWhiteSpace(dto.PaymentMethod) ? "online" : dto.PaymentMethod.Trim().ToLowerInvariant(),
            Status = CommercePaymentStatus.Pending,
            PaymentLink = FabricatedLegacyCheckoutUrl(shortRef),
            ExpiresAt = DateTime.UtcNow.AddHours(24),
            CreatedAt = DateTime.UtcNow
        };

        order.Status = "payment_requested";
        order.UpdatedAt = DateTime.UtcNow;

        await _paymentRepository.AddAsync(payment, ct);
        await _orderRepository.UpdateAsync(order, ct);

        return MapToDto(payment);
    }

    /// <summary>
    /// The one place a checkout URL is ever constructed from a value Aveline chose, and it exists
    /// only for the one-release rollback (plan §8.4 S8). The provider path never calls it.
    /// </summary>
    private static string FabricatedLegacyCheckoutUrl(string shortRef) =>
        $"https://pay.aveline.boutique/checkout/{shortRef}";

    /// <summary>
    /// The counter confirmation: the caller's <c>GatewayTransactionId</c> records the operator's
    /// reference for a payment with no provider behind it. Also the rollback's confirmation, because
    /// with the switch off there is no intent to poll.
    /// </summary>
    /// <remarks>
    /// This is the path <c>docs/reports/PR-290-slice3-review.md:269</c> describes ("accepts any
    /// non-empty <c>GatewayTransactionId</c>"). It remains trustworthy for exactly one reason: a
    /// payment reaches it only when it has no provider intent, so the reference is a human's note
    /// about a payment taken at the desk, not a claim about a gateway charge. A provider-backed
    /// payment never reaches it while the switch is on.
    /// </remarks>
    private async Task<PaymentResponseDto> CounterConfirmPaymentAsync(
        Guid organizationId,
        Payment payment,
        ConfirmPaymentDto dto,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.GatewayTransactionId))
        {
            throw new ArgumentException("Gateway transaction ID is required.", nameof(dto));
        }

        if (payment.Status.Equals(CommercePaymentStatus.Confirmed, StringComparison.OrdinalIgnoreCase))
        {
            return MapToDto(payment);
        }

        if (!payment.Status.Equals(CommercePaymentStatus.Pending, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Payment '{payment.Id}' cannot be confirmed because it has status '{payment.Status}'.");
        }

        payment.Status = CommercePaymentStatus.Confirmed;
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

        await RecordOrderPaymentAsync(organizationId, payment, ct);

        return MapToDto(payment);
    }

    private static string DescribePaymentType(GeneratePaymentRequestDto dto) =>
        string.IsNullOrWhiteSpace(dto.PaymentType) ? "full" : dto.PaymentType.Trim().ToLowerInvariant();

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
            PaymentIntentId = payment.PaymentIntentId,
            CreatedAt = payment.CreatedAt,
            ConfirmedAt = payment.ConfirmedAt,
            ExpiresAt = payment.ExpiresAt
        };
    }
}
