using System.Diagnostics;
using Aveline.Api.Configurations;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.Extensions.Options;

namespace Aveline.Api.Common.Exceptions;

/// <summary>
/// Global exception handler (M-7). Every unhandled exception becomes a stable
/// <c>{ status, message, traceId }</c> 500 body. The exception message, type name and
/// stack trace are deliberately never serialised: the detail is logged server-side and
/// correlated to the client through <c>traceId</c> instead (BR-7.3 for health applies the
/// same principle here).
/// </summary>
public sealed class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger,
    IOptions<HstsOptions> hstsOptions) : IExceptionHandler
{
    /// <summary>Stable, non-revealing client-facing message.</summary>
    public const string PublicMessage = "An unexpected error occurred while processing the request.";

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var traceId = Activity.Current?.Id ?? httpContext.TraceIdentifier;

        logger.LogError(
            exception,
            "Unhandled exception. method={Method} path={Path} traceId={TraceId}",
            httpContext.Request.Method,
            RequestPathRedaction.SafePath(httpContext),
            traceId);

        if (httpContext.Response.HasStarted)
        {
            // Too late to rewrite the response; let the server close the connection.
            return false;
        }

        // The framework clears the response - status, body *and headers* - before it
        // invokes an IExceptionHandler, so the hardening headers the security middleware
        // already wrote are gone by now. Re-apply them here (M-13, reconciliation §3.6).
        SecurityConfiguration.ApplyHardeningHeaders(httpContext.Response);

        var hsts = hstsOptions.Value;
        if (httpContext.Request.IsHttps
            && !hsts.ExcludedHosts.Contains(httpContext.Request.Host.Host))
        {
            SecurityConfiguration.ApplyHstsHeader(httpContext.Response, hsts);
        }

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        // Only the stable envelope is written; exception data stays in the logs.
        await httpContext.Response.WriteAsJsonAsync(
            new
            {
                status = StatusCodes.Status500InternalServerError,
                message = PublicMessage,
                traceId,
            },
            cancellationToken);

        return true;
    }
}
