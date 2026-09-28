using System.Text.Json;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Audit.Models;
using Aveline.Api.Modules.Audit.Services;
using Aveline.Api.Modules.CustomerConcierge.Models;
using Aveline.Api.Modules.CustomerConcierge.Repositories;

namespace Aveline.Api.Modules.Privacy.Services;

/// <summary>
/// The one place consent is revoked (plan §5.4). It exists so the customer OTP path and the staff
/// path cannot drift: both call <see cref="RevokeAsync"/>, and the only difference is the
/// <see cref="ConsentActor"/> and the <see cref="ConsentRevocationScope"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Identity by phone for a global opt-out</b> (§5.4 Option 1). <c>scope = all</c> finds every
/// customer row whose stored number equals the normalized phone - in any organisation - and revokes
/// each. There is no cross-org identity table yet, so the phone <i>is</i> the identity. The
/// consequence is recorded in the plan: a household landline would be over-matched, and org B's
/// records change because of an action taken at org A.
/// </para>
/// <para>
/// <b>Two audit rows per revocation.</b> The append-only <see cref="ConsentAuditEntry"/> is the
/// consent history (it survives with the customer, per Q-4), and the <see cref="AuditLogEntry"/> is
/// the operator-facing trail. Both carry identifiers only: no phone number, no OTP, no message
/// content.
/// </para>
/// <para>
/// <b>A missing consent row is created, not skipped.</b> "This customer never answered" must become
/// "this customer objected", or the next inbound message would be processed. It is the same D-5
/// shape the staff consent service uses.
/// </para>
/// </remarks>
public sealed class ConsentRevoker : IConsentRevoker
{
    private readonly ICustomerRepository _customers;
    private readonly ICustomerConsentRepository _consent;
    private readonly IPhoneSubjectLocator _subjects;
    private readonly IAuditService _audit;
    private readonly AppDbContext _db;
    private readonly TimeProvider _clock;
    private readonly ILogger<ConsentRevoker> _logger;
    private readonly IPrivacyNotificationService? _notifications;

    public ConsentRevoker(
        ICustomerRepository customers,
        ICustomerConsentRepository consent,
        IPhoneSubjectLocator subjects,
        IAuditService audit,
        AppDbContext db,
        TimeProvider clock,
        ILogger<ConsentRevoker> logger,
        IPrivacyNotificationService? notifications = null)
    {
        _customers = customers;
        _consent = consent;
        _subjects = subjects;
        _audit = audit;
        _db = db;
        _clock = clock;
        _logger = logger;
        _notifications = notifications;
    }

    /// <inheritdoc />
    public async Task<ConsentRevocationResult> RevokeAsync(
        ConsentRevocationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var targets = await ResolveTargetsAsync(request, cancellationToken);
        var effectiveAt = _clock.GetUtcNow().UtcDateTime;
        var affected = new List<Guid>();

        foreach (var (organizationId, customerId) in targets)
        {
            var row = await _consent.GetForCustomerAsync(organizationId, customerId, cancellationToken);
            var previous = ConsentStatuses.TryNormalize(row?.ConsentStatus) ?? ConsentStatuses.AbsentRow;

            if (row is null)
            {
                // D-5's insert branch: a customer who never answered still gets an objection row, or
                // the gate would keep processing them.
                row = await _consent.AddAsync(
                    new CustomerConsent
                    {
                        OrganizationId = organizationId,
                        CustomerId = customerId,
                        ConsentStatus = ConsentStatuses.Revoked,
                        ConsentRevokedAt = effectiveAt,
                        UpdatedAt = effectiveAt,
                        ConsentSource = request.Actor.Source,
                    },
                    cancellationToken);
            }
            else
            {
                row.ConsentStatus = ConsentStatuses.Revoked;
                row.ConsentRevokedAt = effectiveAt;
                row.ConsentSource = request.Actor.Source;
                row.UpdatedAt = effectiveAt;

                if (row.GlobalSubjectId is null && request.Scope == ConsentRevocationScope.All)
                {
                    // Best-effort annotation of the identity the phone defines. Nullable on purpose:
                    // there is no backfill migration and no uniqueness constraint, so this never
                    // becomes a second source of truth for "who is this".
                    row.GlobalSubjectId = GlobalSubject.ForPhone(request.PhoneE164);
                }

                await _consent.SaveAsync(row, cancellationToken);
            }

            await WriteConsentAuditAsync(
                organizationId, customerId, previous, request, effectiveAt, cancellationToken);

            await _audit.RecordAsync(
                new AuditEntryRequest(
                    Action: AuditAction.ConsentRevoked,
                    EntityType: "CustomerConsent",
                    EntityId: customerId.ToString("D"),
                    OrganizationId: organizationId,
                    ActorKind: request.Actor.Kind,
                    ActorUserId: request.Actor.UserId,
                    ActorRef: request.Actor.ActorRef,
                    Before: new { consentStatus = previous },
                    After: new { consentStatus = ConsentStatuses.Revoked, scope = ConsentRevocationResult.ScopeName(request.Scope) },
                    Reason: request.Reason,
                    IpHash: request.Actor.IpHash,
                    UserAgent: request.Actor.UserAgent),
                cancellationToken);

            affected.Add(organizationId);

            // Phase 6 item 6.2: the boutique must know a customer opted out - it changes what staff
            // may do with the record. Best-effort and after the durable writes above, so a
            // notification failure can never roll back or fail the revocation itself.
            if (_notifications is not null)
            {
                await _notifications.ConsentRevokedAsync(
                    organizationId,
                    customerId,
                    ConsentRevocationResult.ScopeName(request.Scope),
                    request.Actor.Source,
                    cancellationToken);
            }
        }

        _logger.LogInformation(
            "Consent revoked. scope={Scope} rows={Rows} organizations={Organizations} actorKind={ActorKind}",
            ConsentRevocationResult.ScopeName(request.Scope),
            targets.Count,
            affected.Distinct().Count(),
            request.Actor.Kind);

        return new ConsentRevocationResult(
            targets.Count,
            affected.Distinct().ToList(),
            effectiveAt,
            targets.Select(t => new PhoneSubject(t.OrganizationId, t.CustomerId)).ToList());
    }

    /// <summary>
    /// Resolves the (organisation, customer) pairs the scope names. An org-scoped request touches
    /// exactly one row and never crosses the tenant boundary (R-17); an all-scoped request is the
    /// only path that reads more than one organisation, and it does so through the dedicated
    /// locator.
    /// </summary>
    private async Task<IReadOnlyList<(Guid OrganizationId, Guid CustomerId)>> ResolveTargetsAsync(
        ConsentRevocationRequest request, CancellationToken cancellationToken)
    {
        if (request.Scope == ConsentRevocationScope.All)
        {
            if (string.IsNullOrWhiteSpace(request.PhoneE164))
            {
                // Defence in depth: "all" without a phone would be "every customer in the estate".
                throw new InvalidOperationException(
                    "A global revocation requires the normalized phone number it applies to.");
            }

            var subjects = await _subjects.FindAllByPhoneAsync(request.PhoneE164, cancellationToken);
            return subjects.Select(s => (s.OrganizationId, s.CustomerId)).ToList();
        }

        if (request.CustomerId is { } customerId)
        {
            return [(request.OrganizationId, customerId)];
        }

        // The customer id was not supplied (the anonymous start path only knows a phone), so the
        // org-scoped row is resolved inside this one organisation.
        if (string.IsNullOrWhiteSpace(request.PhoneE164))
        {
            return [];
        }

        var customer = await _customers.GetByPhoneAsync(
            request.OrganizationId, request.PhoneE164, cancellationToken);

        return customer is null ? [] : [(request.OrganizationId, customer.Id)];
    }

    /// <summary>
    /// The append-only consent-history row. <c>EvidenceJson</c> carries identifiers only: the scope
    /// and the source. Never the phone, never an OTP.
    /// </summary>
    private async Task WriteConsentAuditAsync(
        Guid organizationId,
        Guid customerId,
        string previous,
        ConsentRevocationRequest request,
        DateTime effectiveAt,
        CancellationToken cancellationToken)
    {
        _db.ConsentAuditEntries.Add(new ConsentAuditEntry
        {
            OrganizationId = organizationId,
            CustomerId = customerId,
            Action = AuditAction.ConsentRevoked,
            PreviousStatus = previous,
            NewStatus = ConsentStatuses.Revoked,
            Source = request.Actor.Source,
            ActorKind = request.Actor.Kind,
            ActorUserId = request.Actor.UserId,
            ActorRef = request.Actor.ActorRef,
            EvidenceJson = JsonSerializer.Serialize(new
            {
                scope = ConsentRevocationResult.ScopeName(request.Scope),
                source = request.Actor.Source,
            }),
            IpHash = request.Actor.IpHash,
            UserAgent = request.Actor.UserAgent,
            CreatedAt = effectiveAt,
        });

        await _db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// The value written to <c>CustomerConsent.GlobalSubjectId</c> for a global opt-out. It is derived
/// from the phone fingerprint rather than stored in a table, so the column is an annotation and not
/// a second identity model (plan §5.4 Option 1; Option 2 is the eventual replacement).
/// </summary>
public static class GlobalSubject
{
    /// <summary>
    /// A deterministic id for the phone, or <c>null</c> when no phone is known. Derived from the
    /// first 16 bytes of the fingerprint so it is a stable UUID across instances.
    /// </summary>
    public static Guid? ForPhone(string? phoneE164)
    {
        if (string.IsNullOrWhiteSpace(phoneE164))
        {
            return null;
        }

        var fingerprint = PhoneFingerprint.Of(phoneE164);
        var hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(fingerprint));

        return new Guid(hash.AsSpan(0, 16));
    }
}
