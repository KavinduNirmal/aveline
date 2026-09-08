using System.Text.Json;

namespace Aveline.Api.Infrastructure.Eventing;

/// <summary>
/// Default <see cref="IEventSerializer"/> using <see cref="System.Text.Json"/> with a
/// snake_case naming policy so the wire format matches the Python Pydantic contract.
/// </summary>
public sealed class SystemTextJsonEventSerializer : IEventSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    public string Serialize(EventEnvelope envelope)
        => JsonSerializer.Serialize(envelope, Options);

    public EventEnvelope? Deserialize(string json)
    {
        try
        {
            var envelope = JsonSerializer.Deserialize<EventEnvelope>(json, Options);
            return string.IsNullOrWhiteSpace(envelope?.EventType) ? null : envelope;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
