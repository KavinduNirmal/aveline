using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aveline.Api.Configurations;

/// <summary>
/// Structured logging for authentication and authorization events.
/// </summary>
public static class LoggingConfiguration
{
    /// <summary>Log category for authorization audit events (401/403 responses).</summary>
    public const string AuditLogCategory = "Aveline.Api.Authorization";

    /// <summary>
    /// Enables JSON console output when <c>Logging:UseJsonConsole</c> is true, so logs
    /// can be shipped verbatim to ELK, Application Insights, etc.
    /// </summary>
    public static IServiceCollection AddAvelineLogging(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        if (configuration.GetValue("Logging:UseJsonConsole", false))
        {
            services.AddLogging(builder => builder.AddJsonConsole());
        }

        return services;
    }

    /// <summary>
    /// Logs every 401/403 response with the caller identity and request context.
    /// Must run after <c>UseAuthentication</c> so <see cref="HttpContext.User"/> is set.
    /// </summary>
    /// <remarks>
    /// The path is resolved through <see cref="RequestPathRedaction"/>, never read raw: a refusal on
    /// <c>GET /api/v1/media/{token}</c> would otherwise write a live bearer credential to the log
    /// (migration plan §7.7 — "token never logged").
    /// </remarks>
    public static IApplicationBuilder UseAvelineAuthAudit(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            await next();

            if (context.Response.StatusCode == StatusCodes.Status401Unauthorized
                || context.Response.StatusCode == StatusCodes.Status403Forbidden)
            {
                var logger = context.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger(AuditLogCategory);

                var userId = context.User?.FindFirstValue(ClaimTypes.NameIdentifier)
                             ?? context.User?.FindFirstValue("sub");

                logger.LogWarning(
                    "Access denied. userId={UserId} statusCode={StatusCode} method={Method} path={Path}",
                    userId,
                    context.Response.StatusCode,
                    context.Request.Method,
                    RequestPathRedaction.SafePath(context));
            }
        });
    }
}

/// <summary>
/// The one place that decides what a credential-bearing request path looks like in a log line, an
/// exception record or an exported span.
/// </summary>
/// <remarks>
/// <para>
/// <c>GET /api/v1/media/{token}</c> carries a bearer credential in its path, which is the frozen
/// design (strategy §3.5). A token in a log file or a trace backend is the same exposure, so the
/// migration plan's failure-mode table (§7.7, "token never logged") is enforced here rather than by
/// a per-route middleware: every sink asks this type for the safe form.
/// </para>
/// <para>
/// Two rules, in order. When routing has resolved an endpoint, the endpoint's route template is the
/// safe form — a template is credential-free by construction, so <em>any</em> future route that puts
/// a secret in a path parameter is covered the moment it is mapped, without being registered here.
/// When no endpoint resolved (a refusal or a failure before routing, or a media-shaped path that
/// matched nothing) the registered credential families are matched by prefix and replaced with their
/// placeholder. Everything else is returned untouched, so ordinary audit lines keep the literal
/// path that gives them their diagnostic value.
/// </para>
/// </remarks>
public static class RequestPathRedaction
{
    /// <summary>A path family whose segments carry a credential, and its safe placeholder.</summary>
    private readonly record struct CredentialPath(string Prefix, string Placeholder);

    /// <summary>
    /// Every credential-bearing path family. Add a row when a new route carries a secret in its
    /// path: the audit middleware, the global exception handler and the span exporter all read it.
    /// </summary>
    private static readonly CredentialPath[] CredentialPaths =
    [
        // GET /api/v1/media/{token} — the token IS the credential (strategy §3.5).
        new("/api/v1/media", "/api/v1/media/{token}"),
    ];

    /// <summary>
    /// The path to record for <paramref name="context"/>: its resolved route template when routing
    /// has an endpoint, otherwise the registered redaction of a credential-shaped path.
    /// </summary>
    public static string SafePath(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.GetEndpoint() is RouteEndpoint { RoutePattern.RawText: { Length: > 0 } template })
        {
            return template;
        }

        return TryRedactValue(context.Request.Path.Value, out var redacted)
            ? redacted
            : context.Request.Path.Value ?? string.Empty;
    }

    /// <summary>
    /// Replaces a credential-bearing portion of any value (a path, an absolute URL, a span attribute
    /// or a span name) with its placeholder. Returns <c>false</c> when the value carries no
    /// credential, so callers can leave ordinary values untouched.
    /// </summary>
    public static bool TryRedactValue(string? value, out string redacted)
    {
        redacted = value ?? string.Empty;
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        foreach (var (prefix, placeholder) in CredentialPaths)
        {
            var start = value.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                continue;
            }

            // Only a path-segment boundary counts: `/api/v1/media-feed` is a different route.
            var boundary = start + prefix.Length;
            if (boundary < value.Length && value[boundary] is not ('/' or '?' or '#'))
            {
                continue;
            }

            redacted = string.Concat(value.AsSpan(0, start), placeholder);
            return true;
        }

        return false;
    }
}
