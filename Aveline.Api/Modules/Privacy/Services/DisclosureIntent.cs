namespace Aveline.Api.Modules.Privacy.Services;

/// <summary>
/// One durable disclosure intent: "this customer wrote from this number, and they may still need the
/// first-contact disclosure" (plan §4.4, §15 Q-1). It carries identifiers only - never the message
/// body - because it is queued, logged and potentially persisted in the future.
/// </summary>
/// <param name="OrganizationId">The boutique the message arrived for.</param>
/// <param name="CustomerId">The identified customer whose consent row carries the one-time stamp.</param>
/// <param name="ToE164">The number to send to, in E.164 form.</param>
public sealed record DisclosureIntent(Guid OrganizationId, Guid CustomerId, string ToE164);
