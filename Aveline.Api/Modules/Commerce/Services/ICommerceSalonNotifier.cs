namespace Aveline.Api.Modules.Commerce.Services;

/// <summary>
/// Notifies the client's Salon conversation thread in real time when an order is approved,
/// payment is requested, or rejected.
/// </summary>
public interface ICommerceSalonNotifier
{
    /// <summary>
    /// Sends an approval confirmation message from Lina into the client's Salon thread.
    /// </summary>
    Task NotifyOrderApprovedAsync(
        Guid organizationId,
        Guid orderId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a payment request message with payment block from Lina into the client's Salon thread.
    /// </summary>
    Task NotifyPaymentRequestedAsync(
        Guid organizationId,
        Guid orderId,
        string? checkoutUrl = null,
        decimal? amount = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends an order rejection message from Lina into the client's Salon thread.
    /// </summary>
    Task NotifyOrderRejectedAsync(
        Guid organizationId,
        Guid orderId,
        string? reason = null,
        CancellationToken cancellationToken = default);
}
