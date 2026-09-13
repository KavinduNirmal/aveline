using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;

namespace Aveline.Api.Common.Exceptions;

/// <summary>
/// Global exception handler (M-7). Every unhandled exception becomes a stable
/// <c>{ status, message, traceId }</c> 500 body. The exception message, type name and
/// stack trace are deliberately never serialised: the detail is logged server-side and
/// correlated to the client through <c>traceId</c> instead (BR-7.3 for health applies the
/// same principle here).
/// </summary>
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
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
            httpContext.Request.Path,
            traceId);

        if (httpContext.Response.HasStarted)
        {
            // Too late to rewrite the response; let the server close the connection.
            return false;
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
