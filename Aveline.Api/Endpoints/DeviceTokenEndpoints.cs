using System.Security.Claims;
using Aveline.Api.Modules.Notifications.Models;
using Aveline.Api.Modules.Notifications.Repositories;
using Aveline.Api.Modules.Shared.Repositories;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Aveline.Api.Endpoints;

/// <summary>
/// Authenticated device-token registration endpoints used by the Flutter app to register
/// and unregister its FCM push token. Routed under <c>/users/me/devices</c>.
/// </summary>
public static class DeviceTokenEndpoints
{
    public static IEndpointRouteBuilder MapDeviceTokenEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/users/me/devices");

        group.MapPost("", async (
            ClaimsPrincipal user,
            RegisterDeviceTokenRequest request,
            IUserRepository users,
            IDeviceTokenRepository deviceTokens,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Token))
            {
                return Results.BadRequest(new { message = "A device token is required." });
            }

            var clerkId = user.FindFirstValue(ClaimTypes.NameIdentifier)
                          ?? user.FindFirstValue("sub");
            if (string.IsNullOrEmpty(clerkId))
            {
                return Results.Unauthorized();
            }

            var dbUser = await users.GetByClerkIdAsync(clerkId, ct);
            if (dbUser is null)
            {
                return Results.NotFound(new { message = "User record does not exist in Aveline database." });
            }

            await deviceTokens.UpsertAsync(new UserDeviceToken
            {
                UserId = dbUser.Id,
                Token = request.Token,
                Platform = request.Platform,
            }, ct);

            return Results.Ok();
        }).RequireAuthorization();

        group.MapDelete("/{token}", async (
            ClaimsPrincipal user,
            string token,
            IUserRepository users,
            IDeviceTokenRepository deviceTokens,
            CancellationToken ct) =>
        {
            var clerkId = user.FindFirstValue(ClaimTypes.NameIdentifier)
                          ?? user.FindFirstValue("sub");
            if (string.IsNullOrEmpty(clerkId))
            {
                return Results.Unauthorized();
            }

            var dbUser = await users.GetByClerkIdAsync(clerkId, ct);
            if (dbUser is null)
            {
                return Results.NotFound(new { message = "User record does not exist in Aveline database." });
            }

            await deviceTokens.DeactivateAsync(dbUser.Id, token, ct);
            return Results.NoContent();
        }).RequireAuthorization();

        return endpoints;
    }
}

/// <summary>Request body for registering a device token.</summary>
public sealed record RegisterDeviceTokenRequest(string Token, DevicePlatform Platform);
