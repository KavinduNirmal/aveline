using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
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
                    userId, context.Response.StatusCode, context.Request.Method, context.Request.Path);
            }
        });
    }
}
