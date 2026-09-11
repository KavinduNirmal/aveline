using System.Diagnostics;
using System.Security.Claims;
using Aveline.Api.Modules.ApiAccess.Authentication;
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
    private const string OrganizationClaim = "org_id";
    private const string UserClaim = "user_id";

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

        var (organizationId, apiKeyId, userId) = ResolvePrincipal(context.User);
        var request = context.Request;

        var sample = new ApiRequestSample
        {
            OccurredAt = occurredAt,
            OrganizationId = organizationId,
            ApiKeyId = apiKeyId,
            UserId = userId,
            RouteTemplate = RouteTemplateResolver.Resolve(context),
            HttpMethod = request.Method,
            StatusCode = statusCode,
            DurationMs = durationMs,
            RequestBytes = ClampBytes(request.ContentLength),
            ResponseBytes = ClampBytes(context.Response.ContentLength),
            RequestId = context.TraceIdentifier,
            TraceId = ParseGuid(Activity.Current?.TraceId.ToString()),
            ClientIpHash = MetricDimensionHasher.HashIp(
                context.Connection.RemoteIpAddress?.ToString(), _options.IpHashSalt),
            UserAgentHash = MetricDimensionHasher.HashUserAgent(
                request.Headers.UserAgent.ToString()),
            ShouldPersistRaw = TelemetrySampling.ShouldPersistRaw(
                statusCode, durationMs, _options.SlowRequestMs, _options.SuccessSampleRate,
                Random.Shared.NextDouble()),
        };

        channel.TryEnqueue(sample);
    }

    private static (Guid? OrganizationId, Guid? ApiKeyId, Guid? UserId) ResolvePrincipal(
        ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true)
        {
            return (null, null, null);
        }

        var organizationId =
            ParseGuid(principal.FindFirst(ApiKeyClaimTypes.OrganizationId)?.Value)
            ?? ParseGuid(principal.FindFirst(OrganizationClaim)?.Value);

        var apiKeyId = ParseGuid(principal.FindFirst(ApiKeyClaimTypes.ApiKeyId)?.Value);

        var userId =
            ParseGuid(principal.FindFirst(UserClaim)?.Value)
            ?? ParseGuid(principal.FindFirst(ClaimTypes.NameIdentifier)?.Value)
            ?? ParseGuid(principal.FindFirst("sub")?.Value);

        return (organizationId, apiKeyId, userId);
    }

    private static int ClampBytes(long? value)
        => value is null or < 0 ? 0 : (int)Math.Min(value.Value, int.MaxValue);

    private static Guid? ParseGuid(string? value)
        => Guid.TryParse(value, out var parsed) ? parsed : null;
}
