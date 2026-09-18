namespace Aveline.Api.Infrastructure.Eventing;

/// <summary>
/// Marker interface for event payloads published over the event bus.
/// Implementations are plain DTOs; the concrete event type name is supplied at
/// publish time and used to build the Redis channel (see <see cref="EventChannel"/>).
/// </summary>
public interface IEvent;
