using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Aveline.Api.Modules.Organizations.DTOs;
using Aveline.Api.Modules.Organizations.Services;
using Aveline.Api.Modules.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Aveline.Api.Endpoints;

public static class OnboardingEndpoints
{
    public static IEndpointRouteBuilder MapOnboardingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Map under both /onboarding (matching the prompt table) and /v1/onboarding (consistent with /api/v1)
        var group = endpoints.MapGroup("/onboarding");

        group.MapGet("/status", async (
            ClaimsPrincipal principal,
            IUserService userService,
            IOnboardingService onboardingService,
            CancellationToken ct) =>
        {
            var userDto = await ResolveUserAsync(principal, userService, ct);
            if (userDto is null) return Results.Unauthorized();

            var status = await onboardingService.GetStatusAsync(userDto.Id, ct);
            return Results.Ok(status);
        }).RequireAuthorization();

        group.MapPost("/owner", async (
            SaveBoutiqueDetailsRequest request,
            ClaimsPrincipal principal,
            IUserService userService,
            IOnboardingService onboardingService,
            CancellationToken ct) =>
        {
            var userDto = await ResolveUserAsync(principal, userService, ct);
            if (userDto is null) return Results.Unauthorized();

            var validationResults = new List<ValidationResult>();
            if (!Validator.TryValidateObject(request, new ValidationContext(request), validationResults, true))
            {
                return Results.ValidationProblem(validationResults
                    .GroupBy(v => v.MemberNames.FirstOrDefault() ?? string.Empty)
                    .ToDictionary(g => g.Key, g => g.Select(v => v.ErrorMessage ?? "Invalid").ToArray()));
            }

            try
            {
                var orgDto = await onboardingService.SaveBoutiqueDetailsAsync(userDto.Id, request, ct);
                return Results.Ok(orgDto);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        }).RequireAuthorization();

        group.MapPost("/plan", async (
            SelectPlanRequest request,
            ClaimsPrincipal principal,
            IUserService userService,
            IOnboardingService onboardingService,
            CancellationToken ct) =>
        {
            var userDto = await ResolveUserAsync(principal, userService, ct);
            if (userDto is null) return Results.Unauthorized();

            try
            {
                var orgDto = await onboardingService.SelectPlanAsync(userDto.Id, request, ct);
                return Results.Ok(orgDto);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        }).RequireAuthorization();

        group.MapPost("/customize", async (
            SaveAiCustomizationRequest request,
            ClaimsPrincipal principal,
            IUserService userService,
            IOnboardingService onboardingService,
            CancellationToken ct) =>
        {
            var userDto = await ResolveUserAsync(principal, userService, ct);
            if (userDto is null) return Results.Unauthorized();

            try
            {
                var orgDto = await onboardingService.SaveAiCustomizationAsync(userDto.Id, request, ct);
                return Results.Ok(orgDto);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        }).RequireAuthorization();

        group.MapPost("/complete", async (
            ClaimsPrincipal principal,
            IUserService userService,
            IOnboardingService onboardingService,
            CancellationToken ct) =>
        {
            var userDto = await ResolveUserAsync(principal, userService, ct);
            if (userDto is null) return Results.Unauthorized();

            try
            {
                var response = await onboardingService.CompleteOnboardingAsync(userDto.Id, ct);
                return Results.Ok(response);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        }).RequireAuthorization();

        return endpoints;
    }

    private static async Task<Aveline.Api.Modules.Shared.DTOs.UserDto?> ResolveUserAsync(
        ClaimsPrincipal principal,
        IUserService userService,
        CancellationToken ct)
    {
        var clerkId = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? principal.FindFirstValue("sub");
        return string.IsNullOrEmpty(clerkId) ? null : await userService.GetByClerkIdAsync(clerkId, ct);
    }
}
