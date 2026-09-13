using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Aveline.Api.Infrastructure.Integrations;
using Aveline.Api.Modules.Shared.DTOs;
using Aveline.Api.Modules.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Aveline.Api.Endpoints;

public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/users");

        group.MapGet("/me", async (ClaimsPrincipal user, IUserService userService, CancellationToken ct) =>
        {
            var clerkId = user.FindFirstValue(ClaimTypes.NameIdentifier)
                          ?? user.FindFirstValue("sub");

            if (string.IsNullOrEmpty(clerkId))
            {
                return Results.Unauthorized();
            }

            var userDto = await userService.GetByClerkIdAsync(clerkId, ct);
            if (userDto == null)
            {
                return Results.NotFound(new { message = "User record does not exist in Aveline database." });
            }

            return Results.Ok(userDto);
        }).RequireAuthorization();

        group.MapPost("/onboarding", async (            ClaimsPrincipal user,
            CompleteOnboardingRequest request,
            IUserService userService,
            CancellationToken ct) =>
        {
            var clerkId = user.FindFirstValue(ClaimTypes.NameIdentifier)
                          ?? user.FindFirstValue("sub");

            if (string.IsNullOrEmpty(clerkId))
            {
                return Results.Unauthorized();
            }

            var validationResults = new List<ValidationResult>();
            var validationContext = new ValidationContext(request);
            if (!Validator.TryValidateObject(request, validationContext, validationResults, true))
            {
                return Results.ValidationProblem(validationResults
                    .GroupBy(v => v.MemberNames.FirstOrDefault() ?? string.Empty)
                    .ToDictionary(g => g.Key, g => g.Select(v => v.ErrorMessage ?? "Invalid value").ToArray()));
            }

            try
            {
                var updatedUser = await userService.CompleteOnboardingAsync(clerkId, request, ct);
                return Results.Ok(updatedUser);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound(new { message = "User not found in Aveline database." });
            }
        }).RequireAuthorization();

        group.MapPatch("/me", async (
            ClaimsPrincipal user,
            UpdateUserProfileRequest request,
            IUserService userService,
            CancellationToken ct) =>
        {
            var clerkId = ResolveClerkId(user);
            if (clerkId is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                var updated = await userService.UpdateProfileAsync(clerkId, request, ct);
                return updated is null
                    ? Results.NotFound(new { message = "User record does not exist in Aveline database." })
                    : Results.Ok(updated);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        }).RequireAuthorization();

        group.MapDelete("/me", async (
            ClaimsPrincipal user,
            IUserService userService,
            CancellationToken ct) =>
        {
            var clerkId = ResolveClerkId(user);
            if (clerkId is null)
            {
                return Results.Unauthorized();
            }

            var deleted = await userService.DeleteAccountAsync(clerkId, ct);
            return deleted is null
                ? Results.NotFound(new { message = "User record does not exist in Aveline database." })
                : Results.Ok(new { message = "Account deleted.", accountState = deleted.AccountState.ToString() });
        }).RequireAuthorization();

        group.MapGet("/me/sessions", async (
            ClaimsPrincipal user,
            IClerkAdminClient clerkAdminClient,
            CancellationToken ct) =>
        {
            var clerkId = ResolveClerkId(user);
            if (clerkId is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                var sessions = await clerkAdminClient.ListSessionsAsync(clerkId, ct);
                return Results.Ok(sessions.Select(session => new
                {
                    session.Id,
                    session.Status,
                    session.CreatedAt,
                    session.LastActiveAt,
                    session.ExpireAt,
                }));
            }
            catch (HttpRequestException ex)
            {
                return Results.Json(
                    new { message = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
            }
        }).RequireAuthorization();

        group.MapPost("/me/sessions/revoke-all", async (
            ClaimsPrincipal user,
            IClerkAdminClient clerkAdminClient,
            CancellationToken ct) =>
        {
            var clerkId = ResolveClerkId(user);
            if (clerkId is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                var revoked = await clerkAdminClient.RevokeAllSessionsAsync(clerkId, ct);
                return Results.Ok(new { revoked });
            }
            catch (HttpRequestException ex)
            {
                return Results.Json(
                    new { message = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
            }
        }).RequireAuthorization();

        return endpoints;
    }

    private static string? ResolveClerkId(ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
}
