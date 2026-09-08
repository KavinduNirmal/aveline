namespace Aveline.Api.Infrastructure.Eventing;

/// <summary>
/// Serializes <see cref="EventEnvelope"/> instances to/from the shared snake_case JSON
/// contract used by both the API and the Python agent service.
/// </summary>
public interface IEventSerializer
{
    /// <summary>Serializes an envelope to its JSON wire representation.</summary>
    string Serialize(EventEnvelope envelope);

    /// <summary>
    /// Deserializes a JSON message into an envelope, or returns <c>null</c> when the
    /// message is malformed or does not carry a valid <c>event_type</c>.
    /// </summary>
    EventEnvelope? Deserialize(string json);
}
