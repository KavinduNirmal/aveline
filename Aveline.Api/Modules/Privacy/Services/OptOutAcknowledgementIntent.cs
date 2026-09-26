namespace Aveline.Api.Modules.Privacy.Services;

/// <summary>
/// One durable opt-out-acknowledgement intent (plan §11 item 4.5). It carries identifiers only -
/// never the message body or the phone number in plaintext - because it is queued and logged.
/// </summary>
/// <param name="OrganizationId">The boutique whose consent was revoked.</param>
/// <param name="CustomerId">The customer whose consent was revoked.</param>
/// <param name="ToE164">The number to acknowledge, in E.164 form.</param>
/// <param name="OrganizationName">The boutique's display name, read at enqueue time.</param>
public sealed record OptOutAcknowledgementIntent(
    Guid OrganizationId, Guid CustomerId, string ToE164, string OrganizationName);
