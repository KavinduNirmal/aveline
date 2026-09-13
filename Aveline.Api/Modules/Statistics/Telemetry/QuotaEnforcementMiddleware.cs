using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aveline.Api.Modules.Statistics.Telemetry;

/// <summary>
/// Enforces the organisation/key request quota before the endpoint runs (BR-6.5). It is a
/// no-op unless <c>Quotas:EnforcementEnabled</c> is <c>true</c>, and an exhausted meter
/// returns <c>429</c> with <c>{ message, quota: { metricKey, limit, used, resetsAt } }</c>.
/// Registered after telemetry so the 429 is itself counted (FR-6.8).
/// </summary>
public sealed class QuotaEnforcementMiddleware(
    RequestDelegate next,
    IOptions<Services.QuotaOptions> quotaOptions,
    ILogger<QuotaEnforcementMiddleware> logger)
{
    private readonly bool _enabled = quotaOptions.Value.EnforcementEnabled;

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_enabled)
        {
            await next(context);
            return;
        }

        var (organizationId, apiKeyId, _) = RequestPrincipal.Resolve(context.User);
        // A JWT carries the org in the route, not necessarily as a claim; the API-key scheme
        // supplies it as a claim. Prefer the claim when present, otherwise the route value.
        organizationId ??= RouteOrganizationId(context);
        if (organizationId is null)
        {
            await next(context);
            return;
        }

        var quota = context.RequestServices.GetRequiredService<Services.IQuotaService>();
        var evaluation = await quota.EvaluateAsync(
            organizationId.Value, apiKeyId, context.RequestAborted);

        if (evaluation.Exhausted is { } exhausted)
        {
            logger.LogInformation(
                "API quota exhausted. metricKey={MetricKey} organizationId={OrganizationId} limit={Limit} used={Used}",
                exhausted.MetricKey, organizationId, exhausted.Limit, exhausted.Used);

            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            await context.Response.WriteAsJsonAsync(
                new
                {
                    message = $"The {exhausted.MetricKey} quota for this organisation is exhausted.",
                    quota = new
                    {
                        metricKey = exhausted.MetricKey,
                        limit = exhausted.Limit,
                        used = exhausted.Used,
                        resetsAt = exhausted.ResetsAt,
                    },
                },
                context.RequestAborted);
            return;
        }

        await next(context);
    }

    private static Guid? RouteOrganizationId(HttpContext context)
        => context.Request.RouteValues.TryGetValue("organizationId", out var value)
           && Guid.TryParse(value?.ToString(), out var parsed)
            ? parsed
            : null;
}
