using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Aveline.Api.Modules.Statistics.Telemetry;

/// <summary>
/// Resolves the routed endpoint's template so metrics stay low-cardinality and no path
/// ids leak into statistics (FR-6.2). The raw path is never used.
/// </summary>
public static class RouteTemplateResolver
{
    /// <summary>Used for requests that never matched a route.</summary>
    public const string Unmatched = "(unmatched)";

    public static string Resolve(HttpContext context) => Resolve(context.GetEndpoint());

    public static string Resolve(Endpoint? endpoint)
    {
        if (endpoint is RouteEndpoint { RoutePattern.RawText: { } raw }
            && !string.IsNullOrWhiteSpace(raw))
        {
            var trimmed = raw.Trim();
            return trimmed.StartsWith('/') ? trimmed : "/" + trimmed;
        }

        return Unmatched;
    }
}
