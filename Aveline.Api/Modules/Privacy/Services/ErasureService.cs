using System.Text.Json;
using Aveline.Api.Common.Jobs;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Audit.Models;
using Aveline.Api.Modules.Audit.Services;
using Aveline.Api.Modules.Conversations.Models;
using Aveline.Api.Modules.CustomerConcierge.Models;
using Aveline.Api.Modules.Privacy.Metrics;
using Aveline.Api.Modules.Privacy.Models;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.Privacy.Services;

/// <summary>The per-table count keys the erasure returns and stores (plan §7.2, §7.3).</summary>
/// <remarks>
/// Store <b>counts only, never content</b>: the result is written to <c>DataSubjectRequest.ResultJson</c>,
/// and a result that quoted the erased data would put PII back into the table the erasure is supposed
/// to leave clean.
/// </remarks>
public static class ErasureCountKeys
{
    public const string Customers = "customers";
    public const string Memories = "memories";
    public const string Preferences = "preferences";
    public const string Events = "events";
    public const string Interactions = "interactions";
    public const string Tags = "tags";
    public const string Matches = "matches";
    public const string Conversations = "conversations";
    public const string Messages = "messages";
    public const string Attachments = "attachments";
    public const string SourcingRequests = "sourcingRequests";
    public const string InboundMessageLogs = "inboundMessageLogs";
    public const string Consents = "consents";
    public const string ConsentAuditEntries = "consentAuditEntries";

    /// <summary>The full ordered set, so a stored result always has the same shape.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        Customers, Memories, Preferences, Events, Interactions, Tags, Matches, Conversations,
        Messages, Attachments, SourcingRequests, InboundMessageLogs, Consents, ConsentAuditEntries,
    ];
}

/// <summary>The blast radius of an erasure (plan §7.2).</summary>
public enum ErasureScope
{
    /// <summary>Exactly the organisation the request arrived for.</summary>
    Org,

    /// <summary>Every organisation whose records match the proven phone.</summary>
    All,
}

/// <summary>One erasure request.</summary>
/// <param name="OrganizationId">The organisation the request arrived for.</param>
/// <param name="PhoneE164">The number the OTP proved.</param>
/// <param name="Scope">Organisation or global.</param>
/// <param name="IdempotencyKey">The caller's key; unique per (organisation, kind).</param>
/// <param name="Actor">Who acted, for the audit trail.</param>
/// <param name="Reason">A bounded, non-personal reason.</param>
public sealed record ErasureRequest(
    Guid OrganizationId,
    string PhoneE164,
    ErasureScope Scope,
    string IdempotencyKey,
    ConsentActor Actor,
    string? Reason = null);

/// <summary>What one erasure did.</summary>
/// <param name="RequestId">The durable request id returned to the caller.</param>
/// <param name="CompletedAtUtc">The single instant stamped on the tombstone and the request.</param>
/// <param name="Counts">Per-table row counts, never content.</param>
/// <param name="Replayed">True when an earlier request with the same key produced this result.</param>
/// <param name="OrganizationsAffected">The distinct organisations the erasure touched.</param>
public sealed record ErasureResult(
    Guid RequestId,
    DateTime CompletedAtUtc,
    IReadOnlyDictionary<string, int> Counts,
    bool Replayed,
    IReadOnlyList<Guid> OrganizationsAffected);

/// <summary>Another erasure holds the lease for this (organisation, number). The route answers 409.</summary>
public sealed class ErasureInProgressException : Exception
{
    public ErasureInProgressException(string phoneHash)
        : base("An erasure for this number is already in progress.")
        => PhoneHash = phoneHash;

    /// <summary>The fingerprint (never the number) of the contended request.</summary>
    public string PhoneHash { get; }
}

/// <summary>
/// The §7.3 deletion scope, executed in one transaction, returning per-table counts (plan §11
/// item 5.3).
/// </summary>
public interface IErasureService
{
    /// <summary>
    /// Executes (or replays) an erasure. A repeated <see cref="ErasureRequest.IdempotencyKey"/>
    /// returns the stored <c>ResultJson</c> instead of deleting twice.
    /// </summary>
    /// <exception cref="ErasureInProgressException">Another request holds the lease.</exception>
    Task<ErasureResult> EraseAsync(ErasureRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the stored result of a request that already completed under
    /// <paramref name="idempotencyKey"/>, or <c>null</c> when there is none.
    /// </summary>
    /// <remarks>
    /// This is the half of the idempotency contract that makes a <b>network retry</b> work: the OTP
    /// is single-use, so a retried delete carries a spent code and would otherwise be refused before
    /// it ever reached the stored result (§7.5). The lookup changes nothing and returns counts only.
    /// </remarks>
    Task<ErasureResult?> FindCompletedAsync(
        Guid organizationId,
        string idempotencyKey,
        ErasureScope scope,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The one place customer data is destroyed (plan §7.3, DR-3, Q-3, Q-4).
/// </summary>
/// <remarks>
/// <para>
/// <b>The soft-delete filters are the trap.</b> <c>Customers</c> and <c>CustomerMemory</c> both
/// install <c>HasQueryFilter(DeletedAt == null)</c>, and an ordinary EF query or <c>ExecuteDelete()</c>
/// silently skips the hidden rows — which still hold the phone, the email, the name and the message
/// content. Every read of those two tables therefore uses <c>IgnoreQueryFilters()</c>.
/// </para>
/// <para>
/// <b>Q-3 (messages).</b> The erasure deletes the customer's own content — the <c>ClientMessage</c>
/// blocks and the attachment bytes — and keeps the thread skeleton with an anonymised
/// <c>from</c>, per the plan's recommendation. Staff and agent messages are the boutique's own
/// operational record and are retained; a designer who wants the whole thread gone must say so,
/// because that is a business decision rather than a privacy one.
/// </para>
/// <para>
/// <b>Q-4 (consent survives).</b> The erased number gets a tombstone keyed by its fingerprint with
/// the terminal <c>revoked</c> status, so the next inbound message is not re-processed as a fresh
/// <c>pending</c> customer (risk R-3).
/// </para>
/// </remarks>
public sealed class ErasureService : IErasureService
{
    /// <summary>The lease lifetime; long enough for a large account, short enough to self-heal.</summary>
    public static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(5);

    private readonly AppDbContext _db;
    private readonly IDistributedJobLock _jobLock;
    private readonly ICustomerCacheInvalidator _caches;
    private readonly IAuditService _audit;
    private readonly TimeProvider _clock;
    private readonly ILogger<ErasureService> _logger;
    private readonly IPrivacyNotificationService? _notifications;
    private readonly RightsMetrics? _metrics;

    public ErasureService(
        AppDbContext db,
        IDistributedJobLock jobLock,
        ICustomerCacheInvalidator caches,
        IAuditService audit,
        TimeProvider clock,
        ILogger<ErasureService> logger,
        IPrivacyNotificationService? notifications = null,
        RightsMetrics? metrics = null)
    {
        _db = db;
        _jobLock = jobLock;
        _caches = caches;
        _audit = audit;
        _clock = clock;
        _logger = logger;
        _notifications = notifications;
        _metrics = metrics;
    }

    /// <inheritdoc />
    public async Task<ErasureResult?> FindCompletedAsync(
        Guid organizationId,
        string idempotencyKey,
        ErasureScope scope,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return null;
        }

        var existing = await _db.DataSubjectRequests
            .FirstOrDefaultAsync(
                r => r.OrganizationId == organizationId
                     && r.Kind == DataSubjectRequestKinds.Delete
                     && r.IdempotencyKey == idempotencyKey,
                cancellationToken);

        return existing is { Status: DataSubjectRequestStatuses.Completed, ResultJson: not null }
            ? await BuildReplayResultAsync(existing, scope, cancellationToken)
            : null;
    }

    /// <inheritdoc />
    public async Task<ErasureResult> EraseAsync(
        ErasureRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PhoneE164);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.IdempotencyKey);

        var phoneHash = PhoneFingerprint.Of(request.PhoneE164);

        // §7.5: the key makes the *response* stable. A repeat returns the stored counts instead of
        // running the deletion again.
        var existing = await _db.DataSubjectRequests
            .FirstOrDefaultAsync(
                r => r.OrganizationId == request.OrganizationId
                     && r.Kind == DataSubjectRequestKinds.Delete
                     && r.IdempotencyKey == request.IdempotencyKey,
                cancellationToken);

        if (existing is { Status: DataSubjectRequestStatuses.Completed, ResultJson: not null })
        {
            return await BuildReplayResultAsync(existing, request.Scope, cancellationToken);
        }

        // One deletion at a time per (organisation, number). The lock is best-effort by contract;
        // the unique idempotency index is the hard guarantee.
        var leaseKey = $"privacy:erasure:{request.OrganizationId:D}:{phoneHash}";
        await using var lease = await _jobLock.TryAcquireAsync(leaseKey, LeaseDuration, cancellationToken);
        if (lease is null)
        {
            throw new ErasureInProgressException(phoneHash);
        }

        // `IgnoreQueryFilters` on the resolution read too: a soft-deleted row is still in scope
        // (R-16). Scope `all` deliberately has no organisation predicate - it is the one cross-tenant
        // read in this flow, exactly as a global opt-out is.
        var targetQuery = _db.Customers
            .IgnoreQueryFilters()
            .Where(c => c.PhoneNumber == request.PhoneE164);
        if (request.Scope == ErasureScope.Org)
        {
            targetQuery = targetQuery.Where(c => c.OrganizationId == request.OrganizationId);
        }

        var targetRows = await targetQuery
            .Select(c => new { c.Id, c.OrganizationId, c.FullName, c.Email })
            .ToListAsync(cancellationToken);

        var targets = targetRows
            .Select(r => new ErasureTarget(r.Id, r.OrganizationId, r.FullName, r.Email))
            .ToList();

        var now = _clock.GetUtcNow().UtcDateTime;

        // The in-memory provider has no transactions; Postgres is the store that matters, and the
        // whole deletion - the request row, every table in the §7.3 scope, and the tombstone - is one
        // unit there.
        await using var transaction = _db.Database.IsRelational()
            ? await _db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        if (existing is null)
        {
            existing = new DataSubjectRequest
            {
                OrganizationId = request.OrganizationId,
                Kind = DataSubjectRequestKinds.Delete,
                IdempotencyKey = request.IdempotencyKey,
                RequestedAt = now,
            };
            _db.DataSubjectRequests.Add(existing);
        }

        existing.Status = DataSubjectRequestStatuses.Verified;
        existing.VerifiedAt = now;
        existing.PhoneHash = phoneHash;
        existing.CustomerId = targets.Count == 1 ? targets[0].CustomerId : null;
        await _db.SaveChangesAsync(cancellationToken);

        await _audit.RecordAsync(
            new AuditEntryRequest(
                Action: AuditAction.DataDeletionRequested,
                EntityType: "DataSubjectRequest",
                EntityId: existing.Id.ToString("D"),
                OrganizationId: request.OrganizationId,
                ActorKind: request.Actor.Kind,
                ActorUserId: request.Actor.UserId,
                ActorRef: request.Actor.ActorRef,
                After: new { scope = ScopeName(request.Scope), customers = targets.Count },
                Reason: request.Reason,
                IpHash: request.Actor.IpHash,
                UserAgent: request.Actor.UserAgent),
            cancellationToken);

        var counts = NewCounts();

        foreach (var target in targets)
        {
            await EraseTargetAsync(target, request.PhoneE164, counts, cancellationToken);
        }

        var organizations = targets.Select(t => t.OrganizationId).Distinct().ToList();
        foreach (var organizationId in organizations)
        {
            var tombstone = await _db.PrivacyErasureTombstones.FirstOrDefaultAsync(
                t => t.OrganizationId == organizationId && t.PhoneHash == phoneHash,
                cancellationToken);

            if (tombstone is null)
            {
                _db.PrivacyErasureTombstones.Add(new PrivacyErasureTombstone
                {
                    OrganizationId = organizationId,
                    PhoneHash = phoneHash,
                    Status = ConsentStatuses.Revoked,
                    ErasedAt = now,
                });
            }
            else
            {
                tombstone.Status = ConsentStatuses.Revoked;
                tombstone.ErasedAt = now;
            }
        }

        // The request record is the surviving evidence; it keeps counts and the fingerprint only.
        existing.CustomerId = null;
        existing.Status = DataSubjectRequestStatuses.Completed;
        existing.CompletedAt = now;
        existing.ResultJson = JsonSerializer.Serialize(counts);

        await _db.SaveChangesAsync(cancellationToken);

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        // §7.3, item 5.6: after the commit, evict what the database no longer owns. Best-effort.
        foreach (var target in targets)
        {
            await _caches.InvalidateAsync(
                target.OrganizationId, target.CustomerId, request.PhoneE164,
                target.FullName, target.Email, cancellationToken);
        }

        await _audit.RecordAsync(
            new AuditEntryRequest(
                Action: AuditAction.DataDeletionCompleted,
                EntityType: "DataSubjectRequest",
                EntityId: existing.Id.ToString("D"),
                OrganizationId: request.OrganizationId,
                ActorKind: request.Actor.Kind,
                ActorUserId: request.Actor.UserId,
                ActorRef: request.Actor.ActorRef,
                After: counts,
                Reason: request.Reason,
                IpHash: request.Actor.IpHash,
                UserAgent: request.Actor.UserAgent),
            cancellationToken);

        _logger.LogInformation(
            "Erasure completed. requestId={RequestId} organizations={Organizations} customers={Customers}",
            existing.Id,
            organizations.Count,
            counts[ErasureCountKeys.Customers]);

        // Phase 6 item 6.3: how long an erasure took, measured from the request row's RequestedAt to
        // the instant stamped on the completion. Recorded once, on the first execution only - a
        // replay returns a stored result and did not complete again - and after the commit, so a
        // failed transaction never reports a duration.
        _metrics?.RecordTimeToComplete(now - existing.RequestedAt);

        // Phase 6 item 6.2: the completed deletion is a legal event the owner must be able to point
        // at. Fired after the commit and outside the replay path, so a retry cannot notify twice and
        // a notification failure cannot roll back a deletion. Payload is counts and ids only.
        if (_notifications is not null)
        {
            var scopeName = ScopeName(request.Scope);
            foreach (var organizationId in organizations)
            {
                await _notifications.DataDeletedAsync(
                    organizationId, existing.Id, scopeName, counts, cancellationToken);
            }
        }

        return new ErasureResult(existing.Id, now, counts, Replayed: false, organizations);
    }

    /// <summary>One customer's rows, in the §7.3 order.</summary>
    private async Task EraseTargetAsync(
        ErasureTarget target, string phoneE164, Dictionary<string, int> counts, CancellationToken cancellationToken)
    {
        var organizationId = target.OrganizationId;
        var customerId = target.CustomerId;

        // --- reads (all with an explicit OrganisationId predicate; R-17) -------------------------
        var memories = await _db.CustomerMemories
            .IgnoreQueryFilters()
            .Where(m => m.OrganizationId == organizationId && m.CustomerId == customerId)
            .ToListAsync(cancellationToken);

        var preferences = await _db.CustomerPreferences
            .Where(p => p.OrganizationId == organizationId && p.CustomerId == customerId)
            .ToListAsync(cancellationToken);

        var events = await _db.CustomerEvents
            .Where(e => e.OrganizationId == organizationId && e.CustomerId == customerId)
            .ToListAsync(cancellationToken);

        var interactions = await _db.CustomerInteractions
            .Where(i => i.OrganizationId == organizationId && i.CustomerId == customerId)
            .ToListAsync(cancellationToken);

        var tags = await _db.CustomerTags
            .Where(t => t.OrganizationId == organizationId && t.CustomerId == customerId)
            .ToListAsync(cancellationToken);

        var matches = await _db.CustomerMatches
            .Where(m => m.OrgId == organizationId && m.CustomerId == customerId)
            .ToListAsync(cancellationToken);

        var consents = await _db.CustomerConsents
            .Where(c => c.OrganizationId == organizationId && c.CustomerId == customerId)
            .ToListAsync(cancellationToken);

        var consentAudit = await _db.ConsentAuditEntries
            .Where(e => e.OrganizationId == organizationId && e.CustomerId == customerId)
            .ToListAsync(cancellationToken);

        // The thread can be bound by customer id or only by the channel reference (the number).
        var conversations = await _db.Conversations
            .Where(c => c.OrganizationId == organizationId
                        && (c.CustomerId == customerId || c.ExternalRef == phoneE164))
            .ToListAsync(cancellationToken);

        var conversationIds = conversations.Select(c => c.Id).ToList();

        var messages = conversationIds.Count == 0
            ? new List<Message>()
            : await _db.Messages
                .Where(m => conversationIds.Contains(m.ConversationId))
                .ToListAsync(cancellationToken);

        var attachments = conversationIds.Count == 0
            ? new List<MessageAttachment>()
            : await _db.MessageAttachments
                .Where(a => a.OrganizationId == organizationId && conversationIds.Contains(a.ConversationId))
                .ToListAsync(cancellationToken);

        var sourcingRequests = await _db.SourcingRequests
            .Where(s => s.OrgId == organizationId && s.CustomerId == customerId)
            .ToListAsync(cancellationToken);

        // --- messages and attachments (Q-3) ------------------------------------------------------
        var clientMessages = messages.Where(m => m.Kind == MessageKind.ClientMessage).ToList();
        var clientMessageIds = clientMessages.Select(m => m.Id).ToHashSet();
        foreach (var message in clientMessages)
        {
            // The customer's own words go; the skeleton - that a message arrived, and when - stays.
            message.ContentBlocksJson = RedactedClientMessageBlocks;
        }

        counts[ErasureCountKeys.Messages] += clientMessages.Count;

        var ownedAttachments = attachments
            .Where(a => a.MessageId is { } messageId && clientMessageIds.Contains(messageId))
            .ToList();
        _db.MessageAttachments.RemoveRange(ownedAttachments);
        counts[ErasureCountKeys.Attachments] += ownedAttachments.Count;

        // --- the thread anchor stays; its customer bindings go ------------------------------------
        foreach (var conversation in conversations)
        {
            conversation.ExternalRef = null;
            conversation.CustomerId = null;
        }

        counts[ErasureCountKeys.Conversations] += conversations.Count;

        // --- sourcing requests are a live business record: anonymise, never delete -----------------
        foreach (var sourcingRequest in sourcingRequests)
        {
            sourcingRequest.CustomerId = null;
        }

        counts[ErasureCountKeys.SourcingRequests] += sourcingRequests.Count;

        // --- inbound logs are the evidence the opt-out was honoured (DR-3): keep the row, drop the
        //     content, and drop the number from whichever side carries it ---------------------------
        var logs = await _db.InboundMessageLogs
            .Where(l => l.OrganizationId == organizationId && (l.From == phoneE164 || l.To == phoneE164))
            .ToListAsync(cancellationToken);

        foreach (var log in logs)
        {
            if (log.From == phoneE164)
            {
                log.From = null;
            }

            if (log.To == phoneE164)
            {
                log.To = null;
            }

            log.Content = null;
        }

        counts[ErasureCountKeys.InboundMessageLogs] += logs.Count;

        // --- the hard deletes (DR-3). Never a soft delete for an erasure request. ------------------
        _db.CustomerMemories.RemoveRange(memories);
        _db.CustomerPreferences.RemoveRange(preferences);
        _db.CustomerEvents.RemoveRange(events);
        _db.CustomerInteractions.RemoveRange(interactions);
        _db.CustomerTags.RemoveRange(tags);
        _db.CustomerMatches.RemoveRange(matches);
        _db.CustomerConsents.RemoveRange(consents);
        _db.ConsentAuditEntries.RemoveRange(consentAudit);

        var customerRows = await _db.Customers
            .IgnoreQueryFilters()
            .Where(c => c.OrganizationId == organizationId && c.Id == customerId)
            .ToListAsync(cancellationToken);
        _db.Customers.RemoveRange(customerRows);

        counts[ErasureCountKeys.Customers] += customerRows.Count;
        counts[ErasureCountKeys.Memories] += memories.Count;
        counts[ErasureCountKeys.Preferences] += preferences.Count;
        counts[ErasureCountKeys.Events] += events.Count;
        counts[ErasureCountKeys.Interactions] += interactions.Count;
        counts[ErasureCountKeys.Tags] += tags.Count;
        counts[ErasureCountKeys.Matches] += matches.Count;
        counts[ErasureCountKeys.Consents] += consents.Count;
        counts[ErasureCountKeys.ConsentAuditEntries] += consentAudit.Count;

        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// The skeleton a redacted customer message keeps. It proves the message existed and carries no
    /// words and no number.
    /// </summary>
    internal static string RedactedClientMessageBlocks { get; } =
        JsonSerializer.Serialize(new[] { new { type = "client_message", from = "[erased]", text = (string?)null } });

    /// <summary>
    /// Rebuilds the response an earlier request stored: same id, same instant, same counts, and the
    /// organisations whose tombstones carry the fingerprint.
    /// </summary>
    private async Task<ErasureResult> BuildReplayResultAsync(
        DataSubjectRequest existing, ErasureScope scope, CancellationToken cancellationToken)
    {
        var tombstoneQuery = _db.PrivacyErasureTombstones.Where(t => t.PhoneHash == existing.PhoneHash);
        if (scope == ErasureScope.Org)
        {
            tombstoneQuery = tombstoneQuery.Where(t => t.OrganizationId == existing.OrganizationId);
        }

        var organizations = await tombstoneQuery
            .Select(t => t.OrganizationId)
            .ToListAsync(cancellationToken);

        return new ErasureResult(
            existing.Id,
            existing.CompletedAt ?? existing.RequestedAt,
            DeserializeCounts(existing.ResultJson!),
            Replayed: true,
            organizations);
    }

    private static Dictionary<string, int> NewCounts()
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var key in ErasureCountKeys.All)
        {
            counts[key] = 0;
        }

        return counts;
    }

    private static IReadOnlyDictionary<string, int> DeserializeCounts(string json)
        => JsonSerializer.Deserialize<Dictionary<string, int>>(json)
           ?? new Dictionary<string, int>(StringComparer.Ordinal);

    internal static string ScopeName(ErasureScope scope)
        => scope == ErasureScope.All ? "all" : "org";

    /// <summary>The customer a scope resolved to, captured before the row is destroyed.</summary>
    private sealed record ErasureTarget(
        Guid CustomerId, Guid OrganizationId, string? FullName, string? Email);
}
