using Aveline.Api.Modules.SystemHealth.HealthChecks;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace Aveline.Api.Modules.SystemHealth.Endpoints;

/// <summary>
/// Liveness, readiness and metrics-adjacent health endpoints. Health is deliberately
/// anonymous: an orchestrator cannot present credentials (FR-7.1, permission table §8.5).
/// </summary>
public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapSystemHealthEndpoints(this IEndpointRouteBuilder app)
    {
        // A healthy process with a dead database is still live (BR-7.1).
        app.MapGet("/health/live", () => Results.Ok(new { status = "Healthy" }))
            .AllowAnonymous();

        var options = new HealthCheckOptions
        {
            ResponseWriter = HealthCheckResponseWriter.WriteAsync,
        };

        app.MapHealthChecks("/health", options).AllowAnonymous();
        app.MapHealthChecks("/health/ready", options).AllowAnonymous();

        return app;
    }
}
