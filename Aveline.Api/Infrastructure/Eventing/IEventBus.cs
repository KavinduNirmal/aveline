namespace Aveline.Api.Infrastructure.Eventing;

/// <summary>
/// Transport-agnostic event bus (ADR-014). Producers publish events without knowing the
/// consumers; consumers register handlers for an event type. The default implementation is
/// backed by Redis Pub/Sub.
///
/// <para>
/// Publishing is fire-and-forget: if no subscriber is connected the message is dropped.
/// Critical transactional operations must not rely solely on the bus and should use the
/// internal HTTP path (ADR-009) as a fallback.
/// </para>
/// </summary>
public interface IEventBus
{
    /// <summary>
    /// Publishes an event to the org-scoped channel <c>aveline:&lt;org_id&gt;:&lt;event_type&gt;</c>.
    /// </summary>
    /// <param name="eventType">Event discriminator (e.g. <c>message.received</c>).</param>
    /// <param name="organizationId">Owning organization, or <c>null</c> for system-wide events.</param>
    /// <param name="payload">Event-specific data serialized as an opaque JSON object.</param>
    /// <param name="traceId">Optional correlation id linking the event to a broader workflow.</param>
    Task PublishAsync(
        string eventType,
        Guid? organizationId,
        object? payload,
        Guid? traceId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers a handler for an event type. The handler is invoked for every envelope
    /// received on the matching <c>aveline:*:&lt;event_type&gt;</c> pattern.
    /// </summary>
    Task SubscribeAsync(
        string eventType,
        Func<EventEnvelope, CancellationToken, Task> handler,
        CancellationToken cancellationToken = default);

    /// <summary>Removes a previously registered handler for an event type.</summary>
    Task UnsubscribeAsync(
        string eventType,
        Func<EventEnvelope, CancellationToken, Task> handler,
        CancellationToken cancellationToken = default);
}
