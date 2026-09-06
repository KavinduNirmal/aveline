using System.Security.Claims;
using Aveline.Api.Modules.Shared.Models;
using Aveline.Api.Modules.Shared.Services;
using Microsoft.AspNetCore.Http;

namespace Aveline.Api.Common.Middleware;

/// <summary>
/// Enforces the account lifecycle state (replaces the boolean onboarding gate).
/// Only <see cref="AccountState.Active"/> accounts reach business endpoints;
/// <see cref="AccountState.OnboardingPending"/> accounts are limited to profile,
/// organization, and invitation endpoints; <see cref="AccountState.Suspended"/>
/// accounts get no authenticated API access.
/// </summary>
public class OnboardingMiddleware
{
    private readonly RequestDelegate _next;

    private static readonly string[] AllowedPathsForOnboardingPending = new[]
    {
        "/api/v1/users/me",
        "/api/v1/users/onboarding",
        "/api/v1/auth/claims",
        "/api/v1/admin",
        "/api/v1/orgs",
        "/api/v1/orgs/my",
        "/api/v1/invitations",
        "/api/v1/onboarding",
        "/openapi",
    };

    public OnboardingMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IUserService userService)
    {
        // Internal service calls (e.g. /internal/usage) do not have Clerk user accounts
        if (context.Request.Path.StartsWithSegments("/internal") ||
            context.User.IsInRole("InternalService"))
        {
            await _next(context);
            return;
        }

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

        context.Response.OnStarting(() =>
        {
            context.Response.Headers["X-Completed-Onboarding"] = userStatus.HasCompletedOnboarding ? "true" : "false";
            context.Response.Headers["X-Account-State"] = userStatus.AccountState.ToString();
            return Task.CompletedTask;
        });

        switch (userStatus.AccountState)
        {
            case AccountState.Active:
                await _next(context);
                return;

            case AccountState.Suspended:
                await WriteProblemAsync(
                    context,
                    "https://aveline.app/errors/account-suspended",
                    "Account Suspended",
                    StatusCodes.Status403Forbidden,
                    "This account is suspended and cannot access the API.");
                return;

            default:
                // OnboardingPending: profile + organization/invitation endpoints only.
                var path = context.Request.Path.Value?.TrimEnd('/') ?? string.Empty;
                var isAllowed = AllowedPathsForOnboardingPending.Any(p =>
                    path.Equals(p, StringComparison.OrdinalIgnoreCase)
                    || path.StartsWith(p + "/", StringComparison.OrdinalIgnoreCase));

                if (!isAllowed)
                {
                    await WriteProblemAsync(
                        context,
                        "https://aveline.app/errors/onboarding-required",
                        "Onboarding Required",
                        StatusCodes.Status403Forbidden,
                        "Your account is not active yet. Complete your profile and join an organization before accessing this resource.");
                    return;
                }

                await _next(context);
                return;
        }
    }

    private static Task WriteProblemAsync(
        HttpContext context,
        string type,
        string title,
        int status,
        string detail)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        return context.Response.WriteAsJsonAsync(new
        {
            type,
            title,
            status,
            detail,
        });
    }
}

public static class OnboardingMiddlewareExtensions
{
    public static IApplicationBuilder UseAvelineOnboarding(this IApplicationBuilder app)
    {
        return app.UseMiddleware<OnboardingMiddleware>();
    }
}
