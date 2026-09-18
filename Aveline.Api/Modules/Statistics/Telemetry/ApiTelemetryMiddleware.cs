using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aveline.Api.Modules.Statistics.Telemetry;

/// <summary>
/// Stamps and enqueues one <see cref="ApiRequestSample"/> per request (FR-6.1). It does no
/// I/O, never fails a request, and adds well under 1 ms to p99 (FR-6.3).
/// </summary>
/// <remarks>
/// Registered after authentication/authorization so attribution is available, and before
/// endpoints. <c>OPTIONS</c> and <see cref="TelemetryOptions.ExcludedPaths"/> are skipped
/// (BR-6.2), as is everything when <see cref="TelemetryOptions.Enabled"/> is <c>false</c>.
/// </remarks>
public sealed class ApiTelemetryMiddleware(
    RequestDelegate next,
    TelemetryChannel channel,
    IOptions<TelemetryOptions> options,
    ILogger<ApiTelemetryMiddleware> logger)
{
    private readonly TelemetryOptions _options = options.Value;

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_options.Enabled || IsExcluded(context.Request))
        {
            await next(context);
            return;
        }

        var occurredAt = DateTime.UtcNow;
        var startedTicks = Stopwatch.GetTimestamp();

        try
        {
            await next(context);
        }
        finally
        {
            // Telemetry must never turn a working request into a failed one.
            try
            {
                Enqueue(context, occurredAt, startedTicks);
            }
            catch (Exception exception)
            {
                logger.LogDebug(exception, "Telemetry sample was not enqueued.");
            }
        }
    }

    private bool IsExcluded(HttpRequest request)
    {
        if (HttpMethods.IsOptions(request.Method))
        {
            return true;
        }

        foreach (var excluded in _options.ExcludedPaths)
        {
            if (!string.IsNullOrWhiteSpace(excluded)
                && request.Path.StartsWithSegments(excluded, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void Enqueue(HttpContext context, DateTime occurredAt, long startedTicks)
    {
        var elapsed = Stopwatch.GetElapsedTime(startedTicks);
        var durationMs = (int)Math.Clamp(elapsed.TotalMilliseconds, 0, int.MaxValue);
        var statusCode = (short)Math.Clamp(context.Response.StatusCode, 0, short.MaxValue);

        var (organizationId, apiKeyId, userId) = RequestPrincipal.Resolve(context.User);
        var errorCode = context.Items["TelemetryErrorCode"] as string
            ?? (context.Response.Headers.TryGetValue("X-Error-Code", out var codeHeader) ? codeHeader.ToString() : null)
            ?? (context.Items["ErrorCode"] as string)
            ?? (statusCode >= 400 ? $"http_{statusCode}" : null);

        var resourceType = context.Items["TelemetryResourceType"] as string;
        var resourceId = context.Items["TelemetryResourceId"] as string;

        var sample = new ApiRequestSample
        {
            OccurredAt = occurredAt,
            OrganizationId = organizationId,
            ApiKeyId = apiKeyId,
            UserId = userId,
            RouteTemplate = RouteTemplateResolver.Resolve(context),
            HttpMethod = context.Request.Method,
            StatusCode = statusCode,
            DurationMs = durationMs,
            RequestBytes = ClampBytes(context.Request.ContentLength),
            ResponseBytes = ClampBytes(context.Response.ContentLength),
            RequestId = context.TraceIdentifier,
            TraceId = Guid.TryParse(Activity.Current?.TraceId.ToString(), out var traceId) ? traceId : null,
            ClientIpHash = MetricDimensionHasher.HashIp(
                context.Connection.RemoteIpAddress?.ToString(), _options.IpHashSalt),
            UserAgentHash = MetricDimensionHasher.HashUserAgent(
                context.Request.Headers.UserAgent.ToString(), _options.IpHashSalt),
            ErrorCode = errorCode,
            ResourceType = resourceType,
            ResourceId = resourceId,
            ShouldPersistRaw = TelemetrySampling.ShouldPersistRaw(
                statusCode, durationMs, _options.SlowRequestMs, _options.SuccessSampleRate,
                Random.Shared.NextDouble()),
        };

        channel.TryEnqueue(sample);
    }

    private static int ClampBytes(long? value)
        => value is null or < 0 ? 0 : (int)Math.Min(value.Value, int.MaxValue);
}
