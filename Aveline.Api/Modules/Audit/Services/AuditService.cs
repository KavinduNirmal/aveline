using System.Diagnostics;
using Aveline.Api.Common.Middleware;
using Aveline.Api.Modules.Audit.Models;
using Aveline.Api.Modules.Audit.Repositories;

namespace Aveline.Api.Modules.Audit.Services;

/// <summary>
/// Default <see cref="IAuditService"/>. Enriches the entry with the ambient request
/// correlation id, trace id and user agent, then persists it through the append-only
/// repository. Failures are logged at Error and swallowed so an audit outage cannot
/// take down an unrelated user request.
/// </summary>
public sealed class AuditService(
    IAuditRepository repository,
    IAuditRedactor redactor,
    IHttpContextAccessor httpContextAccessor,
    ILogger<AuditService> logger) : IAuditService
{
    private const int MaxUserAgentLength = 300;

    public async Task RecordAsync(AuditEntryRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var http = httpContextAccessor.HttpContext;

            var entry = new AuditLogEntry
            {
                OrganizationId = request.OrganizationId,
                ActorKind = request.ActorKind,
                ActorUserId = request.ActorUserId,
                ActorApiKeyId = request.ActorApiKeyId,
                ActorRef = request.ActorRef,
                Action = request.Action,
                EntityType = request.EntityType,
                EntityId = request.EntityId,
                BeforeJson = redactor.Redact(request.Before),
                AfterJson = redactor.Redact(request.After),
                Reason = request.Reason,
                RequestId = request.RequestId ?? http?.GetRequestId(),
                TraceId = request.TraceId ?? ResolveTraceId(),
                IpHash = request.IpHash,
                UserAgent = Truncate(request.UserAgent ?? http?.Request.Headers.UserAgent.ToString()),
                CreatedAt = DateTime.UtcNow,
            };

            await repository.AddAsync(entry, cancellationToken);
        }
        catch (Exception exception)
        {
            // An unaudited non-critical action is recoverable; a failed request is not.
            logger.LogError(
                exception,
                "Failed to write audit entry. action={Action} entityType={EntityType} entityId={EntityId}",
                request.Action,
                request.EntityType,
                request.EntityId);
        }
    }

    private static Guid? ResolveTraceId()
    {
        var activity = Activity.Current;
        if (activity is null || activity.TraceId == default)
        {
            return null;
        }

        // W3C trace ids are 32 hex characters; Guid.ParseExact("N") reads them 1:1.
        return Guid.TryParseExact(activity.TraceId.ToHexString(), "N", out var traceId)
            ? traceId
            : null;
    }

    private static string? Truncate(string? value)
        => string.IsNullOrEmpty(value) || value.Length <= MaxUserAgentLength
            ? value
            : value[..MaxUserAgentLength];
}
