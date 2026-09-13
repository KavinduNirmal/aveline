using System.Security.Claims;
using System.Text;
using Aveline.Api.Modules.Billing.Services;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.Billing.Endpoints;

/// <summary>
/// Processes the <c>Idempotency-Key</c> header (FR-2.7, BR-2.8). A replay of the same
/// body returns the stored response with <c>Idempotency-Replayed: true</c>; a replay with
/// a different body returns 409 <c>idempotency-key-reuse</c>.
/// </summary>
public sealed class IdempotencyEndpointFilter(
    IIdempotencyService idempotencyService,
    ILogger<IdempotencyEndpointFilter> logger) : IEndpointFilter
{
    public const string HeaderName = "Idempotency-Key";
    private const int MaxKeyLength = 128;

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var key = http.Request.Headers[HeaderName].ToString();

        if (string.IsNullOrWhiteSpace(key))
        {
            return Results.BadRequest(new
            {
                message = $"The {HeaderName} header is required.",
                code = "idempotency-key-required",
            });
        }

        if (key.Length > MaxKeyLength)
        {
            return Results.BadRequest(new { message = $"{HeaderName} must not exceed {MaxKeyLength} characters." });
        }

        // Buffer the request so the body can be hashed and still be bound by the endpoint.
        http.Request.EnableBuffering();
        string requestBody;
        using (var reader = new StreamReader(http.Request.Body, Encoding.UTF8, leaveOpen: true))
        {
            requestBody = await reader.ReadToEndAsync();
        }

        http.Request.Body.Position = 0;

        var requestHash = IdempotencyService.ComputeHash(requestBody);
        var endpoint = (http.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText
            ?? http.Request.Path.Value
            ?? string.Empty;
        var organizationId = ResolveOrganizationId(http);
        var actorUserId = ResolveActorUserId(http);

        IdempotencyReplay? replay;
        try
        {
            replay = await idempotencyService.TryReplayAsync(
                organizationId, endpoint, key, requestHash, DateTime.UtcNow, http.RequestAborted);
        }
        catch (IdempotencyKeyReuseException)
        {
            return Results.Conflict(new
            {
                code = "idempotency-key-reuse",
                message = "The Idempotency-Key was already used with a different request body.",
            });
        }

        // Only successful responses are replayed; a failed operation must be able to run
        // again once the caller has corrected the underlying condition.
        if (replay is not null && replay.Status < StatusCodes.Status400BadRequest)
        {
            http.Response.StatusCode = replay.Status;
            http.Response.ContentType = "application/json; charset=utf-8";
            http.Response.Headers["Idempotency-Replayed"] = "true";
            await http.Response.WriteAsync(replay.Body, http.RequestAborted);
            return Results.Empty;
        }

        var originalBody = http.Response.Body;
        await using var buffer = new MemoryStream();
        http.Response.Body = buffer;

        try
        {
            var result = await next(context);

            // Execute the endpoint's IResult ourselves so the body lands in the buffer and
            // can be replayed verbatim on a retry.
            if (result is IResult endpointResult)
            {
                await endpointResult.ExecuteAsync(http);
            }

            buffer.Position = 0;
            var responseBody = await new StreamReader(buffer, Encoding.UTF8).ReadToEndAsync();

            await PersistAsync(
                organizationId, actorUserId, endpoint, key, requestHash, http, responseBody);

            buffer.Position = 0;
            await buffer.CopyToAsync(originalBody, http.RequestAborted);
        }
        finally
        {
            http.Response.Body = originalBody;
        }

        return Results.Empty;
    }

    private async Task PersistAsync(
        Guid? organizationId, Guid? actorUserId, string endpoint, string key, string requestHash,
        HttpContext http, string responseBody)
    {
        // Never store a failure: replaying a 4xx/5xx after the caller has fixed the
        // condition would keep returning the stale error for the whole retention window.
        if (http.Response.StatusCode >= StatusCodes.Status400BadRequest)
        {
            return;
        }

        try
        {
            await idempotencyService.SaveAsync(
                organizationId,
                endpoint,
                http.Request.Method,
                key,
                requestHash,
                (short)http.Response.StatusCode,
                responseBody,
                DateTime.UtcNow,
                actorUserId,
                apiKeyId: null,
                http.RequestAborted);
        }
        catch (DbUpdateException exception)
        {
            // A concurrent first write already stored this key; the winner's response stands.
            logger.LogWarning(
                exception, "Idempotency record for key {Key} was stored concurrently.", key);
        }
    }

    private static Guid? ResolveOrganizationId(HttpContext http) =>
        http.Request.RouteValues.TryGetValue("organizationId", out var value)
        && Guid.TryParse(value?.ToString(), out var organizationId)
            ? organizationId
            : null;

    private static Guid? ResolveActorUserId(HttpContext http)
    {
        var subject = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? http.User.FindFirstValue("sub");
        return Guid.TryParse(subject, out var actorUserId) ? actorUserId : null;
    }
}
