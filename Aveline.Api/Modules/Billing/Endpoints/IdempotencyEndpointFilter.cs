using System.Security.Claims;
using System.Text;
using Aveline.Api.Common.Jobs;
using Aveline.Api.Modules.Billing.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Aveline.Api.Modules.Billing.Endpoints;

/// <summary>
/// Processes the <c>Idempotency-Key</c> header (FR-2.7, BR-2.8). A replay of the same
/// body returns the stored response with <c>Idempotency-Replayed: true</c>; a replay with
/// a different body returns 409 <c>idempotency-key-reuse</c>. A per-key distributed lease
/// serialises concurrent requests carrying the same key so the "exactly once" guarantee is
/// enforced rather than emergent from the ledger's own dedup. The lease **fails closed**:
/// if the lock store is unreachable the request is refused with 503 rather than allowed to
/// execute unprotected (disposition §2.1).
/// </summary>
public sealed class IdempotencyEndpointFilter(
    IIdempotencyService idempotencyService,
    IDistributedJobLock locks,
    ILogger<IdempotencyEndpointFilter> logger,
    IConfiguration? configuration = null) : IEndpointFilter
{
    public const string HeaderName = "Idempotency-Key";
    private const int MaxKeyLength = 128;

    /// <summary>Long enough for any endpoint invocation; short enough to recover from a crash.</summary>
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan LeasePollInterval = TimeSpan.FromMilliseconds(25);

    /// <summary>
    /// How long a duplicate waits for the in-flight original before giving up. Configurable
    /// (<c>Billing:IdempotencyLeaseWaitSeconds</c>) so the timeout path is testable.
    /// </summary>
    private readonly TimeSpan _leaseWait = TimeSpan.FromSeconds(
        Math.Max(0, configuration?.GetValue("Billing:IdempotencyLeaseWaitSeconds", 10) ?? 10));

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
        var httpMethod = http.Request.Method;
        var organizationId = ResolveOrganizationId(http);
        var actorUserId = ResolveActorUserId(http);

        // Serialise on the key: without this, two simultaneous requests both miss the lookup
        // below and both execute the endpoint. The loser of the race waits and then replays
        // the winner's stored response.
        IAsyncDisposable? lease;
        try
        {
            lease = await AcquireLeaseAsync(
                organizationId, endpoint, httpMethod, key, http.RequestAborted);
        }
        catch (IdempotencyLeaseUnavailableException exception)
        {
            // Fail closed rather than run an idempotent money operation unprotected.
            logger.LogError(
                exception,
                "Idempotency lease store unavailable for key {Key}; refusing the request.",
                key);
            return Results.Json(
                new
                {
                    code = "idempotency-unavailable",
                    message = "The idempotency guard is unavailable, so the request was not executed. Retry shortly.",
                },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        if (lease is null)
        {
            return Results.Conflict(new
            {
                code = "idempotency-key-in-flight",
                message = "Another request with the same Idempotency-Key is still in flight. Retry shortly.",
            });
        }

        await using (lease)
        {
            return await ExecuteAsync(
                context, next, organizationId, actorUserId, endpoint, httpMethod, key, requestHash);
        }
    }

    /// <summary>
    /// Runs the endpoint while the key's lease is held, replaying a stored response when one
    /// exists and storing the result of a fresh execution otherwise.
    /// </summary>
    private async Task<object?> ExecuteAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next,
        Guid? organizationId,
        Guid? actorUserId,
        string endpoint,
        string httpMethod,
        string key,
        string requestHash)
    {
        var http = context.HttpContext;

        IdempotencyReplay? replay;
        try
        {
            replay = await idempotencyService.TryReplayAsync(
                organizationId, endpoint, httpMethod, key, requestHash, DateTime.UtcNow,
                http.RequestAborted);
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

    /// <summary>
    /// Takes a short distributed lease on the (organization, endpoint, method, key) tuple so
    /// only one request executes it. The lock is the same atomic mutex the scheduled jobs
    /// use, so it is correct across instances with Redis and process-local otherwise.
    /// Returns <c>null</c> when the original holder is still running; throws
    /// <see cref="IdempotencyLeaseUnavailableException"/> when the store itself fails.
    /// </summary>
    private async Task<IAsyncDisposable?> AcquireLeaseAsync(
        Guid? organizationId, string endpoint, string httpMethod, string key, CancellationToken ct)
    {
        var leaseKey = $"idempotency:{organizationId?.ToString() ?? "global"}:{httpMethod}:{endpoint}:{key}";
        var deadline = DateTime.UtcNow + _leaseWait;

        while (true)
        {
            IAsyncDisposable? handle;
            try
            {
                handle = await locks.TryAcquireAsync(leaseKey, LeaseDuration, ct);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Never degrade to an unguarded execution: the caller is refused instead.
                throw new IdempotencyLeaseUnavailableException(exception);
            }

            if (handle is not null)
            {
                return handle;
            }

            if (DateTime.UtcNow >= deadline)
            {
                logger.LogWarning(
                    "Timed out waiting for the in-flight Idempotency-Key {Key} on {Method} {Endpoint}.",
                    key, httpMethod, endpoint);
                return null;
            }

            await Task.Delay(LeasePollInterval, ct);
        }
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
