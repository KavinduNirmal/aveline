using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpsPolicy;

namespace Aveline.Api.Configurations;

/// <summary>
/// Baseline security hardening headers applied to every response. The API serves JSON
/// and binary payloads only (no HTML, no Swagger UI), so the policy is deliberately
/// maximal: nothing may be loaded, framed or submitted from a response origin (M-13).
/// HSTS is applied separately by <c>UseHsts()</c> in <c>Program.cs</c>.
/// </summary>
public static class SecurityConfiguration
{
    /// <summary>
    /// Deny-by-default CSP for an API surface: no scripts, styles, frames or form
    /// targets. This is safe because no endpoint returns an HTML document.
    /// </summary>
    public const string ContentSecurityPolicy =
        "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";

    /// <summary>
    /// Switch off the browser capabilities an HTTP API never needs. A future endpoint
    /// that requires one of these must opt in explicitly rather than inherit it.
    /// </summary>
    public const string PermissionsPolicy =
        "camera=(), microphone=(), geolocation=(), payment=(), usb=()";

    /// <summary>
    /// Writes the five baseline hardening headers onto <paramref name="response"/>.
    /// Public because <c>GlobalExceptionHandler</c> must re-apply them: the framework
    /// clears the response, headers included, before it invokes an exception handler.
    /// </summary>
    public static void ApplyHardeningHeaders(HttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        response.Headers["X-Content-Type-Options"] = "nosniff";
        response.Headers["X-Frame-Options"] = "DENY";
        response.Headers["Referrer-Policy"] = "no-referrer";
        response.Headers["Content-Security-Policy"] = ContentSecurityPolicy;
        response.Headers["Permissions-Policy"] = PermissionsPolicy;
    }

    /// <summary>
    /// Writes the HSTS header for an HTTPS response using the same policy
    /// <c>UseHsts()</c> would have applied, so a handled 500 carries it too. The
    /// framework's localhost exclusions are honoured by the caller.
    /// </summary>
    public static void ApplyHstsHeader(HttpResponse response, HstsOptions options)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(options);

        if (options.MaxAge <= TimeSpan.Zero)
        {
            return;
        }

        var value = new StringBuilder("max-age=").Append((long)options.MaxAge.TotalSeconds);
        if (options.IncludeSubDomains)
        {
            value.Append("; includeSubDomains");
        }

        if (options.Preload)
        {
            value.Append("; preload");
        }

        response.Headers.StrictTransportSecurity = value.ToString();
    }

    public static IApplicationBuilder UseAvelineSecurityHeaders(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            ApplyHardeningHeaders(context.Response);
            await next();
        });
    }
}
