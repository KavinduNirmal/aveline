using System.Text.Json.Serialization;

namespace Aveline.Api.Modules.Integrations.Models;

/// <summary>
/// Lifecycle state of a tenant integration. Mirrors the plan's state machine so the UI and
/// the health service can reflect real connection health (e.g. token expiry, failed test)
/// rather than deriving "connected" only from the presence of a masked preview.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum IntegrationStatus
{
    /// <summary>Credentials saved but not yet validated against the provider.</summary>
    Pending,

    /// <summary>Credentials validated and the integration is live.</summary>
    Connected,

    /// <summary>A validation/outbound attempt failed; see <c>LastError</c>.</summary>
    Error,

    /// <summary>The provider token has expired and a reconnect is required.</summary>
    Expired,

    /// <summary>The owner deliberately disconnected the integration.</summary>
    Disconnected,
}
