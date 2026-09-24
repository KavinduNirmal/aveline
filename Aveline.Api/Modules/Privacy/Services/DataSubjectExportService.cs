using System.Text.Json;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Conversations.Models;
using Aveline.Api.Modules.CustomerConcierge.Models;
using Aveline.Api.Modules.Privacy.Models;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.Privacy.Services;

/// <summary>
/// One assembled export: the document, the counts the audit and the request log record, and the
/// customer it belongs to. Returning the counts alongside the document keeps "what is in the
/// document" and "what the request log says was exported" the same fact.
/// </summary>
/// <param name="Document">The JSON document returned inline to the customer (plan §7.2).</param>
/// <param name="Counts">Per-collection row counts. <b>Counts only, never content.</b></param>
/// <param name="CustomerId">The customer the document was assembled for.</param>
public sealed record DataSubjectExportDocument(
    JsonElement Document,
    IReadOnlyDictionary<string, int> Counts,
    Guid CustomerId);

/// <summary>
/// Assembles a data subject's full record for an OTP-verified download (plan §7.2, §11 item 5.1).
/// </summary>
/// <remarks>
/// <b>Every query carries an explicit <c>OrganizationId</c> predicate.</b> There is no EF global
/// tenant filter (R-17), so a query that forgets it silently returns another boutique's rows. The
/// customer read additionally uses <c>IgnoreQueryFilters()</c>: a soft-deleted row still holds the
/// subject's PII, and a subject access request must return what is held about them.
/// </remarks>
public interface IDataSubjectExportService
{
    /// <summary>
    /// Builds the document for the customer with <paramref name="phoneE164"/> in
    /// <paramref name="organizationId"/>, or <c>null</c> when no such row exists (the endpoint
    /// answers <c>404</c>).
    /// </summary>
    Task<DataSubjectExportDocument?> BuildAsync(
        Guid organizationId,
        string phoneE164,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class DataSubjectExportService : IDataSubjectExportService
{
    // The download is a wire document: camelCase like every other response, and no indentation so a
    // large export is not bloated by whitespace. Dictionary keys (the `counts` map) are not affected.
    private static readonly JsonSerializerOptions DocumentJson = new(JsonSerializerDefaults.Web);

    private readonly AppDbContext _db;
    private readonly TimeProvider _clock;

    public DataSubjectExportService(AppDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<DataSubjectExportDocument?> BuildAsync(
        Guid organizationId,
        string phoneE164,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneE164);

        // Soft-deleted rows are still the subject's data, so the export reaches them (R-16).
        var customer = await _db.Customers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                c => c.OrganizationId == organizationId && c.PhoneNumber == phoneE164,
                cancellationToken);
        if (customer is null)
        {
            return null;
        }

        var customerId = customer.Id;

        var consent = await _db.CustomerConsents
            .FirstOrDefaultAsync(
                c => c.OrganizationId == organizationId && c.CustomerId == customerId,
                cancellationToken);

        var consentHistory = await _db.ConsentAuditEntries
            .Where(e => e.OrganizationId == organizationId && e.CustomerId == customerId)
            .OrderBy(e => e.CreatedAt)
            .ToListAsync(cancellationToken);

        var memories = await _db.CustomerMemories
            .IgnoreQueryFilters()
            .Where(m => m.OrganizationId == organizationId && m.CustomerId == customerId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(cancellationToken);

        var preferences = await _db.CustomerPreferences
            .Where(p => p.OrganizationId == organizationId && p.CustomerId == customerId)
            .OrderBy(p => p.PreferenceKey)
            .ToListAsync(cancellationToken);

        var events = await _db.CustomerEvents
            .Where(e => e.OrganizationId == organizationId && e.CustomerId == customerId)
            .OrderBy(e => e.EventDate)
            .ToListAsync(cancellationToken);

        var interactions = await _db.CustomerInteractions
            .Where(i => i.OrganizationId == organizationId && i.CustomerId == customerId)
            .OrderBy(i => i.CreatedAt)
            .ToListAsync(cancellationToken);

        var tags = await _db.CustomerTags
            .Where(t => t.OrganizationId == organizationId && t.CustomerId == customerId)
            .OrderBy(t => t.Tag)
            .ToListAsync(cancellationToken);

        var matches = await _db.CustomerMatches
            .Where(m => m.OrgId == organizationId && m.CustomerId == customerId)
            .OrderBy(m => m.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var sourcingRequests = await _db.SourcingRequests
            .Where(s => s.OrgId == organizationId && s.CustomerId == customerId)
            .OrderBy(s => s.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        // The channel thread can exist before identification, keyed only by the external reference
        // (the number), so both bindings are part of "this customer's conversation".
        var conversations = await _db.Conversations
            .Where(c => c.OrganizationId == organizationId
                        && (c.CustomerId == customerId || c.ExternalRef == phoneE164))
            .OrderBy(c => c.CreatedAt)
            .ToListAsync(cancellationToken);

        var conversationIds = conversations.Select(c => c.Id).ToList();

        var messages = conversationIds.Count == 0
            ? new List<Message>()
            : await _db.Messages
                .Where(m => conversationIds.Contains(m.ConversationId))
                .OrderBy(m => m.CreatedAt)
                .ToListAsync(cancellationToken);

        var attachments = conversationIds.Count == 0
            ? new List<MessageAttachment>()
            : await _db.MessageAttachments
                .Where(a => a.OrganizationId == organizationId && conversationIds.Contains(a.ConversationId))
                .OrderBy(a => a.CreatedAtUtc)
                .ToListAsync(cancellationToken);

        var counts = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [ErasureCountKeys.Customers] = 1,
            [ErasureCountKeys.Memories] = memories.Count,
            [ErasureCountKeys.Preferences] = preferences.Count,
            [ErasureCountKeys.Events] = events.Count,
            [ErasureCountKeys.Interactions] = interactions.Count,
            [ErasureCountKeys.Tags] = tags.Count,
            [ErasureCountKeys.Matches] = matches.Count,
            [ErasureCountKeys.Conversations] = conversations.Count,
            [ErasureCountKeys.Messages] = messages.Count,
            [ErasureCountKeys.Attachments] = attachments.Count,
            [ErasureCountKeys.SourcingRequests] = sourcingRequests.Count,
            [ErasureCountKeys.Consents] = consent is null ? 0 : 1,
            [ErasureCountKeys.ConsentAuditEntries] = consentHistory.Count,
        };

        var document = new
        {
            generatedAtUtc = _clock.GetUtcNow().UtcDateTime,
            subject = new
            {
                organizationId,
                customerId,
            },
            counts,
            customer = new
            {
                customer.Id,
                customer.OrganizationId,
                customer.PhoneNumber,
                customer.Email,
                customer.FullName,
                customer.Status,
                customer.Level,
                customer.TotalSpent,
                customer.VisitCount,
                customer.LastVisitAt,
                customer.CreatedAt,
                customer.UpdatedAt,
                customer.DeletedAt,
            },
            consent = consent is null
                ? null
                : new
                {
                    consent.ConsentStatus,
                    consent.ConsentGrantedAt,
                    consent.ConsentRevokedAt,
                    consent.ConsentSource,
                    consent.DisclosureShownAt,
                    consent.DisclosureVersion,
                    consent.CreatedAt,
                    consent.UpdatedAt,
                },
            consentHistory = consentHistory.Select(e => new
            {
                e.Id,
                e.Action,
                e.PreviousStatus,
                e.NewStatus,
                e.Source,
                e.ActorKind,
                e.CreatedAt,
                evidence = ParseJson(e.EvidenceJson),
            }),
            memories = memories.Select(m => new
            {
                m.Id,
                m.Content,
                m.Category,
                m.Source,
                m.IsExplicit,
                m.Confidence,
                metadata = ParseJson(m.MetadataJson),
                m.CreatedAt,
                m.UpdatedAt,
                m.DeletedAt,
            }),
            preferences = preferences.Select(p => new
            {
                p.Id,
                p.PreferenceKey,
                p.PreferenceValue,
                p.IsExplicit,
                p.Confidence,
                p.Source,
                p.CreatedAt,
                p.UpdatedAt,
            }),
            events = events.Select(e => new
            {
                e.Id,
                e.EventType,
                e.EventDate,
                e.Description,
                e.IsActive,
                e.ReminderSentAt,
                e.CreatedAt,
                e.UpdatedAt,
            }),
            interactions = interactions.Select(i => new
            {
                i.Id,
                i.Channel,
                i.Direction,
                i.MessageContent,
                parsedIntent = ParseJson(i.ParsedIntentJson),
                i.CreatedAt,
            }),
            tags = tags.Select(t => new { t.Id, t.Tag, t.CreatedAt }),
            matches = matches.Select(m => new
            {
                m.Id,
                m.ItemId,
                m.MatchConfidence,
                m.MatchReason,
                m.EmployeeActed,
                m.CreatedAtUtc,
            }),
            sourcingRequests = sourcingRequests.Select(s => new
            {
                s.Id,
                s.ItemDescription,
                s.ReferenceImageUrl,
                s.Status,
                s.CreatedAtUtc,
                s.UpdatedAtUtc,
            }),
            conversations = conversations.Select(c => new
            {
                c.Id,
                c.ThreadId,
                c.ExternalRef,
                c.Kind,
                c.Status,
                c.CreatedAt,
                c.LastMessageAt,
            }),
            messages = messages.Select(m => new
            {
                m.Id,
                m.ConversationId,
                authorKind = m.AuthorKind.ToString(),
                kind = m.Kind.ToString(),
                blocks = ParseJson(m.ContentBlocksJson),
                m.CreatedAt,
            }),
            attachments = attachments.Select(a => new
            {
                a.Id,
                a.ConversationId,
                a.MessageId,
                a.ContentType,
                a.FileName,
                a.SizeBytes,
                a.Url,
                a.CreatedAtUtc,
            }),
        };

        return new DataSubjectExportDocument(
            JsonSerializer.SerializeToElement(document, DocumentJson),
            counts,
            customerId);
    }

    /// <summary>
    /// Parses a stored JSON column into an element. A missing or malformed value becomes an empty
    /// object: the document must stay valid JSON even if one stored blob is not.
    /// </summary>
    private static JsonElement ParseJson(string? json)
    {
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                return JsonSerializer.Deserialize<JsonElement>(json);
            }
            catch (JsonException)
            {
                // Fall through to the empty object.
            }
        }

        return JsonSerializer.SerializeToElement(new Dictionary<string, object?>());
    }
}
