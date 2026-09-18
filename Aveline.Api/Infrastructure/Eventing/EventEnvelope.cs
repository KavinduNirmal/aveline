namespace Aveline.Api.Infrastructure.Eventing;

using System.Text.Json.Serialization;

/// <summary>
/// The canonical event envelope exchanged over the Redis event bus (ADR-014).
///
/// The JSON contract is snake_case and is mirrored by the Python agent service's Pydantic
/// <c>EventEnvelope</c> model so both sides serialize identically:
/// <c>event_id</c>, <c>event_type</c>, <c>timestamp</c>, <c>org_id</c>, <c>trace_id</c>, <c>payload</c>.
/// </summary>
public sealed record EventEnvelope
{
    /// <summary>Unique identifier for this event instance.</summary>
    public Guid EventId { get; init; }

    /// <summary>Discriminator used to build the channel (e.g. <c>message.received</c>).</summary>
    public string EventType { get; init; } = string.Empty;

    /// <summary>UTC instant the event was produced.</summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>Organization the event belongs to, or <c>null</c> for system-wide events.</summary>
    [JsonPropertyName("org_id")]
    public Guid? OrganizationId { get; init; }

    /// <summary>Correlation id linking this event to a broader request/workflow.</summary>
    public Guid? TraceId { get; init; }

    /// <summary>Event-specific data. Serialized as an opaque JSON object.</summary>
    public object? Payload { get; init; }

    /// <summary>
    /// Creates a new envelope with generated <see cref="EventId"/> and <see cref="Timestamp"/>.
    /// </summary>
    public static EventEnvelope Create(
        string eventType,
        Guid? organizationId,
        object? payload,
        Guid? traceId = null,
        DateTimeOffset? timestamp = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);

        return new EventEnvelope
        {
            EventId = Guid.NewGuid(),
            EventType = eventType,
            Timestamp = timestamp ?? DateTimeOffset.UtcNow,
            OrganizationId = organizationId,
            TraceId = traceId,
            Payload = payload,
        };
    }
}
