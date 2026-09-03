using System.Security.Claims;
using Aveline.Api.Modules.Shared.Services;
using Microsoft.AspNetCore.Http;

namespace Aveline.Api.Common.Middleware;

public class OnboardingMiddleware
{
    private readonly RequestDelegate _next;

    private static readonly string[] AllowedPaths = new[]
    {
        "/api/v1/users/me",
        "/api/v1/users/onboarding",
        "/api/v1/auth/claims",
        "/openapi",
    };

    public OnboardingMiddleware(RequestDelegate _next)
    {
        this._next = _next;
    }

    public async Task InvokeAsync(HttpContext context, IUserService userService)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            await _next(context);
            return;
        }

        var clerkId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? context.User.FindFirstValue("sub");

        if (string.IsNullOrEmpty(clerkId))
        {
            await _next(context);
            return;
        }

        var userStatus = await userService.GetOrSynchronizeUserAsync(clerkId, context.User, context.RequestAborted);

        // Attach user info to HttpContext items for downstream use
        context.Items["CurrentUser"] = userStatus;

        // Set response header
        context.Response.OnStarting(() =>
        {
            context.Response.Headers["X-Completed-Onboarding"] = userStatus.HasCompletedOnboarding ? "true" : "false";
            return Task.CompletedTask;
        });

        if (!userStatus.HasCompletedOnboarding)
        {
            var path = context.Request.Path.Value?.TrimEnd('/') ?? string.Empty;
            var isAllowed = AllowedPaths.Any(p => path.Equals(p, StringComparison.OrdinalIgnoreCase)
                                               || path.StartsWith(p + "/", StringComparison.OrdinalIgnoreCase));

            if (!isAllowed)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/problem+json";
                await context.Response.WriteAsJsonAsync(new
                {
                    type = "https://aveline.app/errors/onboarding-required",
                    title = "Onboarding Required",
                    status = StatusCodes.Status403Forbidden,
                    detail = "You must complete profile onboarding before accessing this resource."
                });
                return;
            }
        }

        await _next(context);
    }
}

public static class OnboardingMiddlewareExtensions
{
    public static IApplicationBuilder UseAvelineOnboarding(this IApplicationBuilder app)
    {
        return app.UseMiddleware<OnboardingMiddleware>();
    }
}
