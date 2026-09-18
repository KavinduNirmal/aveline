using System.Security.Claims;
using Aveline.Api.Modules.Notifications.DTOs;
using Aveline.Api.Modules.Notifications.Repositories;
using Aveline.Api.Modules.Shared.Repositories;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Aveline.Api.Endpoints;

/// <summary>
/// Authenticated notification-inbox endpoints for the current user. Consumed by the React
/// dashboard and (later) the Flutter app. Notifications are created by the dispatcher; this
/// surface only lists, reads, marks-as-read, and dismisses the caller's own inbox items.
/// </summary>
public static class NotificationEndpoints
{
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/notifications")
            .WithTags("Notifications")
            .RequireAuthorization();

        group.MapGet("", ListAsync)
            .WithName("ListNotifications")
            .WithSummary("List the current user's notifications (paginated).")
            .Produces<NotificationPage>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapGet("/unread-count", GetUnreadCountAsync)
            .WithName("GetUnreadNotificationCount")
            .WithSummary("Get the current user's unread notification count.")
            .Produces<UnreadCountResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapGet("/{id:guid}", GetByIdAsync)
            .WithName("GetNotification")
            .WithSummary("Get a single notification by id.")
            .Produces<UserNotificationDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPatch("/{id:guid}/read", MarkReadAsync)
            .WithName("MarkNotificationRead")
            .WithSummary("Mark a single notification as read (idempotent).")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/read-all", MarkAllReadAsync)
            .WithName("MarkAllNotificationsRead")
            .WithSummary("Mark all of the current user's notifications as read.")
            .Produces<MarkAllReadResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapDelete("/{id:guid}", DismissAsync)
            .WithName("DismissNotification")
            .WithSummary("Dismiss (soft-delete) a single notification.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        ClaimsPrincipal user,
        IUserRepository users,
        IUserNotificationRepository notifications,
        [Microsoft.AspNetCore.Mvc.FromQuery] int page = 1,
        [Microsoft.AspNetCore.Mvc.FromQuery] int pageSize = 50,
        [Microsoft.AspNetCore.Mvc.FromQuery] bool unreadOnly = false,
        CancellationToken cancellationToken = default)
    {
        var userId = await ResolveUserIdAsync(user, users, cancellationToken);
        if (userId is null)
        {
            return Results.Unauthorized();
        }

        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var (items, total) = await notifications.ListAsync(userId.Value, page, pageSize, unreadOnly, cancellationToken);
        return Results.Ok(new NotificationPage(
            items.Select(UserNotificationDto.From).ToList(),
            total,
            page,
            pageSize));
    }

    private static async Task<IResult> GetUnreadCountAsync(
        ClaimsPrincipal user,
        IUserRepository users,
        IUserNotificationRepository notifications,
        CancellationToken cancellationToken = default)
    {
        var userId = await ResolveUserIdAsync(user, users, cancellationToken);
        if (userId is null)
        {
            return Results.Unauthorized();
        }

        var count = await notifications.GetUnreadCountAsync(userId.Value, cancellationToken);
        return Results.Ok(new UnreadCountResponse(count));
    }

    private static async Task<IResult> GetByIdAsync(
        Guid id,
        ClaimsPrincipal user,
        IUserRepository users,
        IUserNotificationRepository notifications,
        CancellationToken cancellationToken = default)
    {
        var userId = await ResolveUserIdAsync(user, users, cancellationToken);
        if (userId is null)
        {
            return Results.Unauthorized();
        }

        var item = await notifications.GetByIdAsync(id, userId.Value, cancellationToken);
        return item is null
            ? Results.NotFound(new { message = "Notification not found." })
            : Results.Ok(UserNotificationDto.From(item));
    }

    private static async Task<IResult> MarkReadAsync(
        Guid id,
        ClaimsPrincipal user,
        IUserRepository users,
        IUserNotificationRepository notifications,
        CancellationToken cancellationToken = default)
    {
        var userId = await ResolveUserIdAsync(user, users, cancellationToken);
        if (userId is null)
        {
            return Results.Unauthorized();
        }

        var item = await notifications.GetByIdAsync(id, userId.Value, cancellationToken);
        if (item is null)
        {
            return Results.NotFound(new { message = "Notification not found." });
        }

        await notifications.MarkReadAsync(id, userId.Value, cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> MarkAllReadAsync(
        ClaimsPrincipal user,
        IUserRepository users,
        IUserNotificationRepository notifications,
        CancellationToken cancellationToken = default)
    {
        var userId = await ResolveUserIdAsync(user, users, cancellationToken);
        if (userId is null)
        {
            return Results.Unauthorized();
        }

        var updated = await notifications.MarkAllReadAsync(userId.Value, cancellationToken);
        return Results.Ok(new MarkAllReadResponse(updated));
    }

    private static async Task<IResult> DismissAsync(
        Guid id,
        ClaimsPrincipal user,
        IUserRepository users,
        IUserNotificationRepository notifications,
        CancellationToken cancellationToken = default)
    {
        var userId = await ResolveUserIdAsync(user, users, cancellationToken);
        if (userId is null)
        {
            return Results.Unauthorized();
        }

        var item = await notifications.GetByIdAsync(id, userId.Value, cancellationToken);
        if (item is null)
        {
            return Results.NotFound(new { message = "Notification not found." });
        }

        await notifications.DismissAsync(id, userId.Value, cancellationToken);
        return Results.NoContent();
    }

    private static async Task<Guid?> ResolveUserIdAsync(
        ClaimsPrincipal user,
        IUserRepository users,
        CancellationToken cancellationToken)
    {
        var clerkId = user.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? user.FindFirstValue("sub");
        if (string.IsNullOrEmpty(clerkId))
        {
            return null;
        }

        var dbUser = await users.GetByClerkIdAsync(clerkId, cancellationToken);
        return dbUser?.Id;
    }
}

/// <summary>A paginated page of the current user's notifications.</summary>
public sealed record NotificationPage(
    IReadOnlyList<UserNotificationDto> Items,
    int Total,
    int Page,
    int PageSize);

/// <summary>Response for the unread-count endpoint.</summary>
public sealed record UnreadCountResponse(int Count);

/// <summary>Response for the mark-all-read endpoint.</summary>
public sealed record MarkAllReadResponse(int Updated);
