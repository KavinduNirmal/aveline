using System.Text.Json;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Audit.Models;
using Aveline.Api.Modules.CustomerConcierge.Models;
using Aveline.Api.Modules.Integrations.Services;
using Aveline.Api.Modules.Privacy.Metrics;
using Microsoft.Extensions.Caching.Distributed;

namespace Aveline.Api.Modules.Privacy.Services;

/// <summary>
/// Default <see cref="IOptOutAcknowledgementGate"/> over <see cref="IDistributedCache"/>.
/// </summary>
public sealed class DistributedOptOutAcknowledgementGate : IOptOutAcknowledgementGate
{
    /// <summary>How long a sent acknowledgement suppresses the next one.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromHours(24);

    private const string ClaimValue = "1";

    private readonly IDistributedCache _cache;
    private readonly ILogger<DistributedOptOutAcknowledgementGate> _logger;

    public DistributedOptOutAcknowledgementGate(
        IDistributedCache cache,
        ILogger<DistributedOptOutAcknowledgementGate> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// The gate key. It carries the organization and the phone fingerprint - never the number - so a
    /// cache dump does not disclose who opted out.
    /// </summary>
    public static string Key(Guid organizationId, string phoneE164)
        => $"optout:ack:{organizationId:D}:{PhoneFingerprint.Of(phoneE164)}";

    /// <inheritdoc />
    public async Task<bool> TryClaimAsync(
        Guid organizationId, string phoneE164, CancellationToken cancellationToken = default)
    {
        var key = Key(organizationId, phoneE164);

        try
        {
            // The read-check-write is not atomic across instances, so two simultaneous opt-outs can
            // in principle both claim. The outbound channel's stable idempotency key is the second
            // line of defence: the loser's send replays the winner's provider id instead of
            // messaging the customer twice.
            var existing = await _cache.GetStringAsync(key, cancellationToken);
            if (!string.IsNullOrEmpty(existing))
            {
                return false;
            }

            await _cache.SetStringAsync(
                key,
                ClaimValue,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = Window },
                cancellationToken);

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                ex, "The opt-out acknowledgement gate is unavailable; sending nothing (fail closed).");
            throw new OtpStoreUnavailableException(ex);
        }
    }

    /// <inheritdoc />
    public async Task ReleaseAsync(
        Guid organizationId, string phoneE164, CancellationToken cancellationToken = default)
    {
        try
        {
            await _cache.RemoveAsync(Key(organizationId, phoneE164), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A release failure means the customer waits for the window rather than being retried
            // now; loud, but not worth failing the opt-out the customer just completed.
            _logger.LogError(
                ex, "Failed to release the opt-out acknowledgement gate; the next attempt waits.");
        }
    }
}

/// <summary>
/// Default <see cref="IOptOutAcknowledgementService"/>. Gate first (exactly-once), then claim the
/// consent-audit row, then send through the existing outbound channel, then release on failure.
/// </summary>
/// <remarks>
/// <para>
/// <b>Scoped</b>, because it holds the request's <see cref="AppDbContext" />. The background worker
/// creates its own scope, exactly as it does for the disclosure.
/// </para>
/// <para>
/// <b>It does not go through the agent.</b> The body is built here from fixed copy, and the send uses
/// <see cref="IOutboundMessagingService.SendWhatsAppTextAsync"/> - never <c>SendTemplateAsync</c>,
/// which is gated on the unresolved Q-2 policy question.
/// </para>
/// <para>
/// <b>A revoked customer still receives this one message.</b> Q-9 records the reasoning: a single,
/// non-personalised confirmation is necessary to honour the request the customer just made, and
/// nothing follows it.
/// </para>
/// </remarks>
public sealed class OptOutAcknowledgementService : IOptOutAcknowledgementService
{
    /// <summary>The <c>ConsentAuditEntry.Source</c> value for the acknowledgement.</summary>
    public const string AuditSource = "opt_out_ack";

    private readonly IOptOutAcknowledgementGate _gate;
    private readonly IOptOutAcknowledgementBodyBuilder _bodyBuilder;
    private readonly IOutboundMessagingService _outbound;
    private readonly AppDbContext _db;
    private readonly PrivacyDeliveryMetrics _metrics;
    private readonly ILogger<OptOutAcknowledgementService> _logger;

    public OptOutAcknowledgementService(
        IOptOutAcknowledgementGate gate,
        IOptOutAcknowledgementBodyBuilder bodyBuilder,
        IOutboundMessagingService outbound,
        AppDbContext db,
        PrivacyDeliveryMetrics metrics,
        ILogger<OptOutAcknowledgementService> logger)
    {
        _gate = gate;
        _bodyBuilder = bodyBuilder;
        _outbound = outbound;
        _db = db;
        _metrics = metrics;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<OptOutAcknowledgementResult> SendOnceAsync(
        Guid organizationId,
        Guid customerId,
        string phoneE164,
        string organizationName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneE164);

        bool claimed;
        try
        {
            claimed = await _gate.TryClaimAsync(organizationId, phoneE164, cancellationToken);
        }
        catch (OtpStoreUnavailableException)
        {
            return new OptOutAcknowledgementResult(OptOutAcknowledgementOutcome.GateUnavailable);
        }

        if (!claimed)
        {
            return new OptOutAcknowledgementResult(OptOutAcknowledgementOutcome.AlreadyAcknowledged);
        }

        var body = _bodyBuilder.Build(organizationName);
        var idempotencyKey = BuildIdempotencyKey(organizationId, phoneE164);

        OutboundMessageResult result;
        try
        {
            result = await _outbound.SendWhatsAppTextAsync(
                organizationId, phoneE164, body, idempotencyKey, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await _gate.ReleaseAsync(organizationId, phoneE164, cancellationToken);
            _logger.LogError(
                ex, "The opt-out acknowledgement send threw for organization {OrganizationId}.",
                organizationId);
            return new OptOutAcknowledgementResult(OptOutAcknowledgementOutcome.Failed);
        }

        if (!result.IsSuccess)
        {
            await _gate.ReleaseAsync(organizationId, phoneE164, cancellationToken);
            _logger.LogWarning(
                "The opt-out acknowledgement was not sent for organization {OrganizationId}. skipped={Skipped}",
                organizationId, result.Skipped);
            return new OptOutAcknowledgementResult(
                result.Skipped
                    ? OptOutAcknowledgementOutcome.NotConfigured
                    : OptOutAcknowledgementOutcome.Failed);
        }

        await WriteAuditEntryAsync(organizationId, customerId, cancellationToken);
        _logger.LogInformation(
            "Opt-out acknowledgement sent for organization {OrganizationId} customer {CustomerId}.",
            organizationId, customerId);

        return new OptOutAcknowledgementResult(
            OptOutAcknowledgementOutcome.Sent, result.ProviderMessageId);
    }

    /// <summary>
    /// The stable idempotency key for one (boutique, number) acknowledgement. Stable for the life of
    /// the opt-out, so a replay inside the window cannot produce a second provider call even if two
    /// instances raced the gate.
    /// </summary>
    public static string BuildIdempotencyKey(Guid organizationId, string phoneE164)
        => $"optout-ack:{organizationId:D}:{PhoneFingerprint.Of(phoneE164)}";

    /// <summary>
    /// The consent-history row. <c>EvidenceJson</c> carries the acknowledgement version only - no
    /// phone, no body text.
    /// </summary>
    private async Task WriteAuditEntryAsync(
        Guid organizationId, Guid customerId, CancellationToken cancellationToken)
    {
        _db.ConsentAuditEntries.Add(new ConsentAuditEntry
        {
            OrganizationId = organizationId,
            CustomerId = customerId,
            Action = AuditAction.ConsentRevokedAcknowledged,
            PreviousStatus = ConsentStatuses.Revoked,
            NewStatus = ConsentStatuses.Revoked,
            Source = AuditSource,
            ActorKind = ConsentActorKinds.System,
            EvidenceJson = JsonSerializer.Serialize(new
            {
                acknowledgementVersion = _bodyBuilder.CurrentVersion,
                channel = "whatsapp",
            }),
            CreatedAt = DateTime.UtcNow,
        });

        await _db.SaveChangesAsync(cancellationToken);
    }
}
