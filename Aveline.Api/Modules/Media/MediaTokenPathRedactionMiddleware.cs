using Aveline.Api.Configurations;
using Microsoft.AspNetCore.Http;

namespace Aveline.Api.Modules.Media;

/// <summary>
/// Replaces a credential-bearing media path with its safe placeholder <em>after</em> routing has
/// selected the endpoint, so a refused token can never reach a log line.
/// </summary>
/// <remarks>
/// <para>
/// <c>GET /api/v1/media/{token}</c> carries a bearer credential in its path, which is the frozen
/// design (strategy §3.5). The primary containment is now central: the 401/403 authorization audit
/// (<see cref="LoggingConfiguration.UseAvelineAuthAudit"/>), the global exception handler and the
/// OpenTelemetry span exporter all resolve the path through <see cref="RequestPathRedaction"/>.
/// </para>
/// <para>
/// This middleware remains as pipeline-level containment for any <em>future</em> reader of
/// <see cref="HttpContext.Request.Path"/> that does not know to ask the redaction rules first: the
/// path is rewritten to the placeholder before the endpoint runs, so whatever observes it afterwards
/// observes the template. It sources its rule from <see cref="RequestPathRedaction"/> rather than
/// naming the route itself, so there is exactly one list of credential-bearing paths.
/// </para>
/// <para>
/// It runs after <c>UseRouting</c> (the minimal-hosting pipeline adds it first), so the endpoint and
/// its route values are already resolved: the <c>{token}</c> parameter still binds, and only what is
/// subsequently <em>observed</em> changes.
/// </para>
/// </remarks>
public sealed class MediaTokenPathRedactionMiddleware
{
    private readonly RequestDelegate _next;

    public MediaTokenPathRedactionMiddleware(RequestDelegate next)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
    }

    /// <summary>Redacts the path for a credential-bearing route, then continues the pipeline.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (RequestPathRedaction.TryRedactValue(context.Request.Path.Value, out var redacted))
        {
            context.Request.Path = redacted;
        }

        await _next(context);
    }
}
