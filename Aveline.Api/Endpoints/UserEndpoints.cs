using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
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

        group.MapPost("/onboarding", async (
            ClaimsPrincipal user,
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

        return endpoints;
    }
}
