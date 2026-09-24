using System.Text.Json;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Audit.Models;
using Aveline.Api.Modules.CustomerConcierge.Models;
using Aveline.Api.Modules.CustomerConcierge.Repositories;
using Aveline.Api.Modules.Integrations.Services;
using Aveline.Api.Modules.Organizations.Repositories;
using Aveline.Api.Modules.Privacy.Metrics;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.Privacy.Services;

/// <summary>
/// The §4.4 disclosure sequence, in order:
///
/// <list type="number">
///   <item>a conditional <c>UPDATE CustomerConsent SET DisclosureShownAt = now() WHERE ... AND
///     DisclosureShownAt IS NULL</c> is the concurrency control - zero rows affected means "already
///     disclosed", so the method returns without sending;</item>
///   <item>the message is built from the boutique's own name and the signed links;</item>
///   <item><see cref="IOutboundMessagingService"/> sends it under a stable idempotency key, so a
///     replay returns the prior provider id without a second Meta call;</item>
///   <item>on success the append-only <see cref="ConsentAuditEntry"/> records the disclosure with
///     identifiers only;</item>
///   <item>on failure the claim is released (<c>DisclosureShownAt = NULL</c>) so the next inbound
///     message retries.</item>
/// </list>
/// </summary>
/// <remarks>
/// <para>
/// <b>Scoped, and it holds the request's <see cref="AppDbContext"/>.</b> Any background sender must
/// create its own DI scope; resolving this from the root scope throws or captures a disposed
/// context (the trap Pr2 flagged in <c>IntegrationsModule</c>).
/// </para>
/// <para>
/// <b>Never logs the body or an unmasked number.</b> The only identifiers logged are the
/// organization id, the customer id and the disclosure version.
/// </para>
/// </remarks>
public sealed class DisclosureDispatchService : IDisclosureDispatchService
{
    /// <summary>The <c>ConsentAuditEntry.Source</c> value for a disclosure sent by this service.</summary>
    public const string AuditSource = "welcome_message";

    /// <summary>The <c>ConsentAuditEntry.ActorKind</c> value: no human or customer acted.</summary>
    public const string AuditActor = AuditActorKind.System;

    private readonly ICustomerConsentRepository _consent;
    private readonly IOrganizationRepository _organizations;
    private readonly AppDbContext _db;
    private readonly IOutboundMessagingService _outbound;
    private readonly IDisclosureBodyBuilder _bodyBuilder;
    private readonly IPrivacyLinkSigner _linkSigner;
    private readonly DisclosureMetrics _metrics;
    private readonly ILogger<DisclosureDispatchService> _logger;
    private readonly IPrivacyNotificationService? _notifications;

    public DisclosureDispatchService(
        ICustomerConsentRepository consent,
        IOrganizationRepository organizations,
        AppDbContext db,
        IOutboundMessagingService outbound,
        IDisclosureBodyBuilder bodyBuilder,
        IPrivacyLinkSigner linkSigner,
        DisclosureMetrics metrics,
        ILogger<DisclosureDispatchService> logger,
        IPrivacyNotificationService? notifications = null)
    {
        _consent = consent;
        _organizations = organizations;
        _db = db;
        _outbound = outbound;
        _bodyBuilder = bodyBuilder;
        _linkSigner = linkSigner;
        _metrics = metrics;
        _logger = logger;
        _notifications = notifications;
    }

    /// <inheritdoc />
    public async Task<DisclosureDispatchResult> DispatchAsync(
        Guid organizationId,
        Guid customerId,
        string toE164,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toE164);

        // Checked before the claim: a host without a signing key must not stamp the row, because a
        // stamped row with no link is a disclosure that can never be completed.
        if (!_linkSigner.IsConfigured)
        {
            _logger.LogWarning(
                "Disclosure skipped for organization {OrganizationId}: {ConfigKey} is not configured.",
                organizationId, PrivacyLinkSigner.ConfigKey);
            _metrics.RecordUnshown();
            return new DisclosureDispatchResult(
                DisclosureDispatchOutcome.SigningKeyMissing,
                Error: "The privacy link signing key is not configured.");
        }

        var existing = await _consent.GetForCustomerAsync(organizationId, customerId, cancellationToken);
        if (existing is null)
        {
            // Defensive: every identified customer should already have a row (Pr0 creates one on
            // identify and on walk-in). A legacy gap must not silently deny the disclosure, so the
            // pending row the consent model expects is created here. A concurrent creator wins the
            // unique index, which is a retry rather than an error.
            try
            {
                existing = await _consent.AddAsync(
                    new CustomerConsent
                    {
                        OrganizationId = organizationId,
                        CustomerId = customerId,
                        ConsentStatus = ConsentStatuses.Pending,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow,
                    },
                    cancellationToken);
            }
            catch (DbUpdateException)
            {
                _logger.LogInformation(
                    "Consent row for organization {OrganizationId} customer {CustomerId} was created "
                    + "concurrently; the next inbound message retries the disclosure.",
                    organizationId, customerId);
                return new DisclosureDispatchResult(DisclosureDispatchOutcome.AlreadyDisclosed);
            }
        }

        // Captured for the audit row before the claim: the disclosure does not change the status,
        // so the entry is "still pending/granted, and this customer has now been shown the notice".
        var status = ConsentStatuses.TryNormalize(existing.ConsentStatus) ?? ConsentStatuses.AbsentRow;
        var version = _bodyBuilder.CurrentVersion;
        var now = DateTime.UtcNow;

        // 1. The concurrency control. Only the caller that flips NULL to a timestamp may send.
        var claimed = await _consent.TryClaimDisclosureAsync(
            organizationId, customerId, version, now, cancellationToken);
        if (!claimed)
        {
            return new DisclosureDispatchResult(DisclosureDispatchOutcome.AlreadyDisclosed);
        }

        // 2. Resolve what the message says. A missing organization cannot produce a body, and the
        // claim must not be left set (which would suppress the retry forever).
        var organization = await _organizations.GetByIdAsync(organizationId, cancellationToken);
        if (organization is null)
        {
            await ReleaseAsync(organizationId, customerId, cancellationToken);
            _metrics.RecordUnshown();
            _logger.LogWarning(
                "Disclosure aborted for organization {OrganizationId}: the organization was not "
                + "found. The claim was released so a later inbound message retries.",
                organizationId);
            return new DisclosureDispatchResult(
                DisclosureDispatchOutcome.Failed, Error: "The organization was not found.");
        }

        var body = _bodyBuilder.Build(
            organization.Name,
            _linkSigner.BuildDataPolicyUrl(organization.Slug),
            _linkSigner.BuildOptOutUrl(organizationId));
        var idempotencyKey = IDisclosureDispatchService.BuildIdempotencyKey(
            organizationId, customerId, version);

        // 3. Send. The channel owns per-org credentials, retry/backoff and the outbound log row; it
        // returns failures rather than throwing, and it replays a prior success for the same key.
        OutboundMessageResult result;
        try
        {
            result = await _outbound.SendWhatsAppTextAsync(
                organizationId, toE164, body.Text, idempotencyKey, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The channel's contract is "never throws"; this guards a future implementation that
            // does. The claim is released so the customer is not left permanently undisclosed.
            await ReleaseAsync(organizationId, customerId, cancellationToken);
            _metrics.RecordUnshown();
            _logger.LogError(
                ex,
                "Disclosure send threw for organization {OrganizationId}; the claim was released.",
                organizationId);

            // Phase 6 item 6.2: a thrown send is a delivery failure the owner must see. The
            // exception's own message is deliberately not carried: a provider message can echo the
            // recipient number, and the notification payload is identifiers only.
            await NotifyDeliveryFailureAsync(
                organizationId, customerId, PrivacyDeliveryFailureReasons.Threw, "whatsapp", cancellationToken);

            return new DisclosureDispatchResult(DisclosureDispatchOutcome.Failed, Error: ex.Message);
        }

        // 5. On failure reset the stamp so the next inbound retries.
        if (!result.IsSuccess)
        {
            await ReleaseAsync(organizationId, customerId, cancellationToken);
            _metrics.RecordUnshown();
            _logger.LogWarning(
                "Disclosure not sent for organization {OrganizationId}; the claim was released so the "
                + "next inbound message retries. skipped={Skipped}",
                organizationId, result.Skipped);

            // "Not configured" is a boutique that has not connected WhatsApp - visible on the
            // integration screen and not an outage - so only a real provider refusal notifies.
            if (!result.Skipped)
            {
                await NotifyDeliveryFailureAsync(
                    organizationId, customerId, PrivacyDeliveryFailureReasons.Provider, "whatsapp",
                    cancellationToken);
            }

            return new DisclosureDispatchResult(
                result.Skipped ? DisclosureDispatchOutcome.NotConfigured : DisclosureDispatchOutcome.Failed,
                Error: result.Error);
        }

        // 4. The disclosure went out: record it, then report it.
        await WriteAuditEntryAsync(organizationId, customerId, status, version, cancellationToken);
        _metrics.RecordShown();
        _logger.LogInformation(
            "Disclosure sent for organization {OrganizationId} customer {CustomerId} version {Version}.",
            organizationId, customerId, version);

        return new DisclosureDispatchResult(DisclosureDispatchOutcome.Sent, result.ProviderMessageId);
    }

    /// <summary>
    /// Raises the compliance-visible delivery-failure notification (Phase 6 item 6.2). Identifiers
    /// only: the organization, the customer, the channel and a bounded reason - never the number,
    /// the OTP or the message body. Never throws; the caller's failure handling is already done.
    /// </summary>
    private async Task NotifyDeliveryFailureAsync(
        Guid organizationId,
        Guid customerId,
        string reason,
        string channelKey,
        CancellationToken cancellationToken)
    {
        if (_notifications is null)
        {
            return;
        }

        await _notifications.PrivacyDeliveryFailedAsync(
            organizationId,
            customerId,
            PrivacyDeliveryKinds.Disclosure,
            reason,
            new Dictionary<string, string?>
            {
                ["channel"] = channelKey,
                ["disclosureVersion"] = _bodyBuilder.CurrentVersion,
            },
            cancellationToken);
    }

    /// <summary>
    /// Releases a claim without throwing. A release failure must not mask the original send failure,
    /// and it must not fail the webhook: the worst case is that this customer is not retried until
    /// the row is fixed, which is logged loudly.
    /// </summary>
    private async Task ReleaseAsync(
        Guid organizationId, Guid customerId, CancellationToken cancellationToken)
    {
        try
        {
            await _consent.ReleaseDisclosureAsync(organizationId, customerId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                ex,
                "Failed to release the disclosure claim for organization {OrganizationId} customer "
                + "{CustomerId}; the customer may not be retried.",
                organizationId, customerId);
        }
    }

    /// <summary>
    /// Writes the append-only consent history row. <see cref="ConsentAuditEntry.EvidenceJson"/>
    /// carries identifiers only - the disclosure version and the channel - never the phone number,
    /// the message body or a signing secret.
    /// </summary>
    private async Task WriteAuditEntryAsync(
        Guid organizationId,
        Guid customerId,
        string status,
        string version,
        CancellationToken cancellationToken)
    {
        _db.ConsentAuditEntries.Add(new ConsentAuditEntry
        {
            OrganizationId = organizationId,
            CustomerId = customerId,
            Action = AuditAction.DisclosureShown,
            PreviousStatus = status,
            NewStatus = status,
            Source = AuditSource,
            ActorKind = AuditActor,
            EvidenceJson = JsonSerializer.Serialize(new
            {
                disclosureVersion = version,
                channel = "whatsapp",
            }),
            CreatedAt = DateTime.UtcNow,
        });

        await _db.SaveChangesAsync(cancellationToken);
    }
}
