using System.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace Aveline.Api.Common.Middleware;

/// <summary>
/// Assigns and echoes the request correlation id (FR-6.10 / FR-6.11, defect D-11).
/// The id is the join key between API statistics, agent runs and audit entries, so it
/// is generated when absent, validated when supplied, echoed on every response and
/// pushed into the logging scope for the duration of the request.
/// </summary>
public sealed class CorrelationIdMiddleware
{
    /// <summary>Request and response header carrying the correlation id.</summary>
    public const string RequestIdHeader = "X-Request-Id";

    /// <summary>Response header carrying the W3C trace id when a trace is active.</summary>
    public const string TraceIdHeader = "X-Trace-Id";

    /// <summary>Key under which the id is stored on <see cref="HttpContext.Items"/>.</summary>
    public const string RequestIdItemKey = "Aveline.RequestId";

    /// <summary>Maximum accepted length of a client-supplied id.</summary>
    public const int MaxRequestIdLength = 128;

    private static readonly char[] AllowedSeparators = ['.', '_', ':', '-'];

    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>
    /// Returns whether <paramref name="value"/> is 1-128 characters drawn only from
    /// <c>[A-Za-z0-9._:-]</c>. Anything else is rejected so a hostile client cannot
    /// inject control characters into logs or invent unbounded-cardinality ids.
    /// </summary>
    public static bool IsValidRequestId(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaxRequestIdLength)
        {
            return false;
        }

        foreach (var character in value)
        {
            var allowed = character is >= 'A' and <= 'Z'
                or >= 'a' and <= 'z'
                or >= '0' and <= '9'
                || Array.IndexOf(AllowedSeparators, character) >= 0;

            if (!allowed)
            {
                return false;
            }
        }

        return true;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var supplied = context.Request.Headers[RequestIdHeader].ToString();

        string requestId;
        if (!string.IsNullOrWhiteSpace(supplied))
        {
            if (!IsValidRequestId(supplied))
            {
                _logger.LogWarning("Rejected an invalid {Header} value.", RequestIdHeader);
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(
                    new { message = $"Invalid {RequestIdHeader} header." });
                return;
            }

            requestId = supplied;
        }
        else
        {
            requestId = Guid.CreateVersion7().ToString();
        }

        context.Items[RequestIdItemKey] = requestId;
        context.Response.Headers[RequestIdHeader] = requestId;

        var traceId = Activity.Current?.Id;
        if (!string.IsNullOrEmpty(traceId))
        {
            context.Response.Headers[TraceIdHeader] = traceId;
        }

        using (_logger.BeginScope(new Dictionary<string, object> { ["requestId"] = requestId }))
        {
            await _next(context);
        }
    }
}

/// <summary>
/// Accessors for the correlation id established by <see cref="CorrelationIdMiddleware"/>.
/// Falls back to the inbound request header so callers that run before the middleware
/// (or in unit tests) still resolve a value.
/// </summary>
public static class CorrelationIdHttpContextExtensions
{
    public static string? GetRequestId(this HttpContext context)
    {
        if (context.Items.TryGetValue(CorrelationIdMiddleware.RequestIdItemKey, out var value)
            && value is string stored
            && stored.Length > 0)
        {
            return stored;
        }

        var header = context.Request.Headers[CorrelationIdMiddleware.RequestIdHeader].ToString();
        return string.IsNullOrEmpty(header) ? null : header;
    }
}
