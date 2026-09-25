using System.Text.Json;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Audit.Models;
using Aveline.Api.Modules.Audit.Services;
using Aveline.Api.Modules.CustomerConcierge.DTOs;
using Aveline.Api.Modules.CustomerConcierge.Models;
using Aveline.Api.Modules.CustomerConcierge.Repositories;
using Aveline.Api.Modules.Notifications.Models;
using Aveline.Api.Modules.Notifications.Services;
using Aveline.Api.Modules.Privacy.Services;

namespace Aveline.Api.Modules.CustomerConcierge.Services;

/// <summary>
/// The staff/agent consent writer (plan §3.1, §5.4). It is the only place that sets a consent status
/// from an authenticated surface, so the customer OTP path and the staff path share one state
/// machine while remaining distinguishable in the audit by <see cref="ConsentActor.Kind"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every update writes two audit rows</b> when the full stack is composed: the append-only
/// <see cref="ConsentAuditEntry"/> (the consent history) and the generic
/// <see cref="AuditLogEntry"/> (the operator trail). Both carry identifiers only.
/// </para>
/// <para>
/// <b>The audit dependencies are optional at construction.</b> Two unit-test call sites build this
/// service with only a repository; a nullable-dependency shape lets them keep doing that without
/// making the null-check a production concern, because the DI container always supplies the real
/// implementations. Defaulting to <see cref="NullAuditService"/> is a test convenience, not a
/// production posture.
/// </para>
/// </remarks>
public class CustomerConsentService : ICustomerConsentService
{
    private readonly ICustomerConsentRepository _consent;
    private readonly AppDbContext? _db;
    private readonly IAuditService _audit;
    private readonly TimeProvider _clock;
    private readonly INotificationDispatcher? _notifications;

    /// <summary>The constructor the unit tests use: state changes only, no audit stack.</summary>
    public CustomerConsentService(ICustomerConsentRepository consent)
        : this(consent, db: null, NullAuditService.Instance, TimeProvider.System)
    {
    }

    /// <summary>The constructor the DI container uses (AuditModule + AvelineDatabase are registered).</summary>
    public CustomerConsentService(
        ICustomerConsentRepository consent,
        AppDbContext? db,
        IAuditService audit,
        TimeProvider clock,
        INotificationDispatcher? notifications = null)
    {
        _consent = consent;
        _db = db;
        _audit = audit;
        _clock = clock;
        _notifications = notifications;
    }

    public async Task<CustomerConsentDto> GetAsync(Guid orgId, Guid customerId, CancellationToken cancellationToken = default)
    {
        var row = await _consent.GetForCustomerAsync(orgId, customerId, cancellationToken);
        return row is null ? CustomerConsentDto.Absent : CustomerConsentDto.From(row);
    }

    public async Task<TenantCustomerConsentDto> GetTenantConsentAsync(Guid orgId, Guid customerId, CancellationToken cancellationToken = default)
    {
        var row = await _consent.GetForCustomerAsync(orgId, customerId, cancellationToken);
        return row is null
            ? new TenantCustomerConsentDto(Guid.Empty, "unknown", null, null)
            : new TenantCustomerConsentDto(row.Id, row.ConsentStatus, row.ConsentGrantedAt, row.ConsentRevokedAt);
    }

    public Task<CustomerConsentDto> UpdateAsync(
        Guid orgId,
        Guid customerId,
        string status,
        CancellationToken cancellationToken = default)
        => UpdateAsync(orgId, customerId, status, actor: null, cancellationToken);

    /// <inheritdoc />
    public async Task<CustomerConsentDto> UpdateAsync(
        Guid orgId,
        Guid customerId,
        string status,
        ConsentActor? actor,
        CancellationToken cancellationToken = default)
    {
        // D-3: `pending` is a real state (an objection that has not been answered), so it is
        // accepted like the other two; anything else is a typed client error.
        var normalized = ConsentStatuses.TryNormalize(status)
            ?? throw new InvalidConsentStatusException(status);

        var actorContext = actor ?? ConsentActor.System;
        var row = await _consent.GetForCustomerAsync(orgId, customerId, cancellationToken);
        var previous = ConsentStatuses.TryNormalize(row?.ConsentStatus) ?? ConsentStatuses.AbsentRow;
        var now = _clock.GetUtcNow().UtcDateTime;

        if (row is null)
        {
            // D-5: the insert branch stamps the same timestamps the update branch does, so the
            // first revocation of a customer who has never had a row is answerable in an audit.
            row = new CustomerConsent
            {
                OrganizationId = orgId,
                CustomerId = customerId,
                ConsentStatus = normalized,
                ConsentGrantedAt = normalized == ConsentStatuses.Granted ? now : null,
                ConsentRevokedAt = normalized == ConsentStatuses.Revoked ? now : null,
                ConsentSource = actorContext.Source,
                CreatedAt = now,
                UpdatedAt = now,
            };
            row = await _consent.AddAsync(row, cancellationToken);
        }
        else
        {
            row.ConsentStatus = normalized;
            row.ConsentGrantedAt = normalized == ConsentStatuses.Granted ? now : row.ConsentGrantedAt;
            // D-4: a re-grant (or a return to pending) clears the stale objection timestamp.
            row.ConsentRevokedAt = normalized == ConsentStatuses.Revoked ? now : null;
            row.ConsentSource = actorContext.Source;
            row.UpdatedAt = now;
            await _consent.SaveAsync(row, cancellationToken);
        }

        await WriteAuditAsync(orgId, customerId, previous, normalized, actorContext, now, cancellationToken);

        // Item 6.2: only an actual transition into `revoked` is an opt-out event. A repeated
        // `revoked` write is a no-op on the state machine and must not notify twice.
        if (_notifications is not null
            && normalized == ConsentStatuses.Revoked
            && previous != ConsentStatuses.Revoked)
        {
            await _notifications.DispatchAsync(
                new Notification(
                    Type: NotificationType.ConsentRevoked,
                    Title: "A customer opted out",
                    Body: "A customer's consent was recorded as revoked. Their conversations are no "
                          + "longer processed by the agent; review any open work for this customer.",
                    Target: new NotificationTarget(orgId),
                    Data: new Dictionary<string, string?>
                    {
                        ["customerId"] = customerId.ToString("D"),
                        ["scope"] = "org",
                        ["source"] = actorContext.Source,
                    },
                    Channels: NotificationChannel.Realtime | NotificationChannel.Push),
                cancellationToken);
        }

        return CustomerConsentDto.From(row);
    }

    /// <summary>
    /// Writes the consent-history row and the generic audit row. Both are best-effort in the sense
    /// that <see cref="IAuditService"/> logs-and-swallows its own failure, matching the rest of the
    /// codebase; the consent state change itself is already persisted.
    /// </summary>
    private async Task WriteAuditAsync(
        Guid orgId,
        Guid customerId,
        string previous,
        string current,
        ConsentActor actor,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (_db is not null)
        {
            _db.ConsentAuditEntries.Add(new ConsentAuditEntry
            {
                OrganizationId = orgId,
                CustomerId = customerId,
                Action = ActionFor(current),
                PreviousStatus = previous,
                NewStatus = current,
                Source = actor.Source,
                ActorKind = actor.Kind,
                ActorUserId = actor.UserId,
                ActorRef = actor.ActorRef,
                EvidenceJson = JsonSerializer.Serialize(new
                {
                    previousStatus = previous,
                    newStatus = current,
                    source = actor.Source,
                }),
                IpHash = actor.IpHash,
                UserAgent = actor.UserAgent,
                CreatedAt = now,
            });

            await _db.SaveChangesAsync(cancellationToken);
        }

        await _audit.RecordAsync(
            new AuditEntryRequest(
                Action: ActionFor(current),
                EntityType: "CustomerConsent",
                EntityId: customerId.ToString("D"),
                OrganizationId: orgId,
                ActorKind: actor.Kind,
                ActorUserId: actor.UserId,
                ActorRef: actor.ActorRef,
                Before: new { consentStatus = previous },
                After: new { consentStatus = current },
                IpHash: actor.IpHash,
                UserAgent: actor.UserAgent),
            cancellationToken);
    }

    private static string ActionFor(string current) => current switch
    {
        ConsentStatuses.Revoked => AuditAction.ConsentRevoked,
        ConsentStatuses.Granted => AuditAction.ConsentGranted,
        _ => AuditAction.ConsentRowCreated,
    };
}
