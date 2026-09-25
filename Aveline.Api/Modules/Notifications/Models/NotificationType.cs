namespace Aveline.Api.Modules.Notifications.Models;

/// <summary>
/// The catalog of notification types Aveline can emit. Extensible: adding a new
/// type is a new enum value plus a recipient-resolver rule — no delivery code changes.
/// </summary>
public enum NotificationType
{
    /// <summary>A customer sends a WhatsApp message.</summary>
    NewMessage,

    /// <summary>The Commerce Agent pauses for human-in-the-loop approval.</summary>
    ApprovalNeeded,

    /// <summary>A customer payment is confirmed.</summary>
    PaymentConfirmed,

    /// <summary>A scheduled check flags a VIP customer as at-risk.</summary>
    VipAtRisk,

    /// <summary>A scheduled reminder for an upcoming event.</summary>
    EventReminder,

    /// <summary>The Visual Agent finds a new customer-to-item match.</summary>
    NewMatch,

    /// <summary>A connected integration's provider token has expired and needs reconnecting.</summary>
    IntegrationExpired,

    /// <summary>
    /// A subscription renewal charge did not settle, so the subscription moved to
    /// <c>PastDue</c> and a retry is scheduled (payment plan §9.4 F4, decision Q3).
    /// </summary>
    SubscriptionPastDue,

    /// <summary>
    /// A subscription stayed <c>PastDue</c> through the whole dunning window and is now
    /// <c>Expired</c>. Read-only access; no data is deleted (decision Q3).
    /// </summary>
    SubscriptionExpired,

    /// <summary>A critical system alert fired (FR-7.11, BR-7.11).</summary>
    SystemAlert,

    /// <summary>
    /// A customer revoked data-processing consent, or a staff member recorded the objection on the
    /// customer's behalf (privacy plan §9.1, Phase 6 item 6.1). The boutique must know: an opt-out
    /// changes what staff may do with that record.
    /// </summary>
    ConsentRevoked,

    /// <summary>
    /// A right-to-erasure request completed (privacy plan §7.3, Phase 6 item 6.1). A legal event,
    /// so it is addressed to the owner and no one else.
    /// </summary>
    DataDeleted,

    /// <summary>
    /// A transparency disclosure or an opt-out OTP could not be delivered (privacy plan §9.1,
    /// Phase 6 item 6.1). An integration outage here means customers are being messaged without a
    /// disclosure, or cannot complete an opt-out - a compliance-visible failure, not a cosmetic one.
    /// </summary>
    PrivacyDeliveryFailed,
}
