using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

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

    public static IApplicationBuilder UseAvelineSecurityHeaders(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["X-Frame-Options"] = "DENY";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["Content-Security-Policy"] = ContentSecurityPolicy;
            context.Response.Headers["Permissions-Policy"] = PermissionsPolicy;
            await next();
        });
    }
}
