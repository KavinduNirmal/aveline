using Aveline.Api.Modules.Notifications.Models;
using Aveline.Api.Modules.Notifications.Services;
using Aveline.Api.Modules.Privacy.Metrics;

namespace Aveline.Api.Modules.Privacy.Services;

/// <summary>The bounded <c>reason</c> values a delivery failure carries.</summary>
public static class PrivacyDeliveryFailureReasons
{
    /// <summary>An attempt was made and the provider refused it.</summary>
    public const string Provider = "provider";

    /// <summary>The channel is not configured for the organization; nothing was attempted.</summary>
    public const string NotConfigured = "not_configured";

    /// <summary>The outbound channel threw instead of returning a classified result.</summary>
    public const string Threw = "threw";
}

/// <summary>
/// The notification producers the privacy surface owns (privacy plan §11 Phase 6 item 6.2). It is a
/// service rather than a direct <see cref="INotificationDispatcher"/> dependency in each caller so
/// the three events share one target rule, one bounded payload vocabulary, and one place to assert
/// that no payload ever carries a phone number, an OTP or a message body.
/// </summary>
/// <remarks>
/// All three producers live in <c>Aveline.Api</c> on purpose: the events originate here (the
/// revocation core, the erasure service, the outbound sender) and the dispatcher is an in-process
/// DI call, so there is no bus a producer in the Python agent service could use.
/// </remarks>
public interface IPrivacyNotificationService
{
    /// <summary>
    /// A customer revoked consent, or a staff member recorded the objection for them.
    /// </summary>
    /// <param name="organizationId">The boutique whose consent row changed.</param>
    /// <param name="customerId">The customer id. An identifier, never a number or a name.</param>
    /// <param name="scope"><c>org</c> or <c>all</c> (the revocation's blast radius).</param>
    /// <param name="source">One of <see cref="ConsentSources"/>.</param>
    Task ConsentRevokedAsync(
        Guid organizationId,
        Guid customerId,
        string scope,
        string source,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// A right-to-erasure request completed. Addresses the owner only: it is a legal event.
    /// </summary>
    /// <param name="organizationId">The boutique whose records were erased.</param>
    /// <param name="requestId">The durable <c>DataSubjectRequest</c> id.</param>
    /// <param name="scope"><c>org</c> or <c>all</c>.</param>
    /// <param name="counts">Per-table row counts. Counts only, never content.</param>
    Task DataDeletedAsync(
        Guid organizationId,
        Guid requestId,
        string scope,
        IReadOnlyDictionary<string, int> counts,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// A disclosure or an OTP could not be delivered.
    /// </summary>
    /// <param name="organizationId">The boutique the message was for.</param>
    /// <param name="customerId">The customer id, when a customer row is known.</param>
    /// <param name="kind">One of <see cref="PrivacyDeliveryKinds"/>.</param>
    /// <param name="reason">One of <see cref="PrivacyDeliveryFailureReasons"/>.</param>
    /// <param name="data">Extra identifiers only - never a number, an OTP or a body.</param>
    Task PrivacyDeliveryFailedAsync(
        Guid organizationId,
        Guid? customerId,
        string kind,
        string reason,
        IReadOnlyDictionary<string, string?> data,
        CancellationToken cancellationToken = default);
}

/// <summary>Default <see cref="IPrivacyNotificationService"/> over <see cref="INotificationDispatcher"/>.</summary>
public sealed class PrivacyNotificationService : IPrivacyNotificationService
{
    /// <summary>The channels every privacy event uses. Push is a best-effort bonus on top of the inbox.</summary>
    private const NotificationChannel Channels = NotificationChannel.Realtime | NotificationChannel.Push;

    private readonly INotificationDispatcher _dispatcher;
    private readonly ILogger<PrivacyNotificationService> _logger;

    public PrivacyNotificationService(
        INotificationDispatcher dispatcher,
        ILogger<PrivacyNotificationService> logger)
    {
        _dispatcher = dispatcher;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task ConsentRevokedAsync(
        Guid organizationId,
        Guid customerId,
        string scope,
        string source,
        CancellationToken cancellationToken = default)
        => DispatchAsync(
            new Notification(
                Type: NotificationType.ConsentRevoked,
                Title: "A customer opted out",
                Body: "A customer withdrew consent to data processing. Their conversations are no "
                      + "longer processed by the agent; review any open work for this customer.",
                Target: new NotificationTarget(organizationId),
                Data: new Dictionary<string, string?>
                {
                    ["customerId"] = customerId.ToString("D"),
                    ["scope"] = scope,
                    ["source"] = source,
                },
                Channels: Channels),
            organizationId,
            NotificationType.ConsentRevoked,
            cancellationToken);

    /// <inheritdoc />
    public Task DataDeletedAsync(
        Guid organizationId,
        Guid requestId,
        string scope,
        IReadOnlyDictionary<string, int> counts,
        CancellationToken cancellationToken = default)
        => DispatchAsync(
            new Notification(
                Type: NotificationType.DataDeleted,
                Title: "A data-deletion request completed",
                Body: "A customer's right-to-erasure request was completed and their personal data "
                      + "was deleted. Retain this notice as the record of the request.",
                Target: new NotificationTarget(organizationId),
                Data: new Dictionary<string, string?>
                {
                    ["requestId"] = requestId.ToString("D"),
                    ["scope"] = scope,
                    // Per-table counts, serialized from the same dictionary the erasure returned.
                    // Counts only: no name, no number, no message content.
                    ["counts"] = string.Join(
                        ',',
                        counts.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                            .Select(pair => $"{pair.Key}:{pair.Value}")),
                },
                Channels: Channels),
            organizationId,
            NotificationType.DataDeleted,
            cancellationToken);

    /// <inheritdoc />
    public Task PrivacyDeliveryFailedAsync(
        Guid organizationId,
        Guid? customerId,
        string kind,
        string reason,
        IReadOnlyDictionary<string, string?> data,
        CancellationToken cancellationToken = default)
    {
        var payload = new Dictionary<string, string?>(data, StringComparer.Ordinal)
        {
            ["kind"] = kind,
            ["reason"] = reason,
            ["customerId"] = customerId?.ToString("D"),
        };

        return DispatchAsync(
            new Notification(
                Type: NotificationType.PrivacyDeliveryFailed,
                Title: "A privacy message could not be delivered",
                Body: "A transparency disclosure or an opt-out code could not be sent. Until the "
                      + "channel is restored, affected customers are messaged without a disclosure "
                      + "and cannot complete an opt-out.",
                Target: new NotificationTarget(organizationId),
                Data: payload,
                Channels: Channels),
            organizationId,
            NotificationType.PrivacyDeliveryFailed,
            cancellationToken);
    }

    private async Task DispatchAsync(
        Notification notification,
        Guid organizationId,
        NotificationType type,
        CancellationToken cancellationToken)
    {
        try
        {
            await _dispatcher.DispatchAsync(notification, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Best-effort by contract: the consent revocation, the erasure or the send outcome is
            // already durable, and a notification failure must not fail the operation the customer
            // asked for. The organization id is the only identifier logged.
            _logger.LogError(
                ex,
                "Failed to dispatch the {Type} notification for organization {OrganizationId}.",
                type,
                organizationId);
        }
    }
}
