using System.ComponentModel.DataAnnotations;
using Aveline.Api.Modules.CustomerConcierge.Common;
using Aveline.Api.Modules.CustomerConcierge.Models;
using Aveline.Api.Modules.CustomerConcierge.Repositories;

namespace Aveline.Api.Modules.CustomerConcierge.DTOs;

/// <summary>A concise customer match returned from the read-only lookup path.</summary>
public sealed record CustomerMatchDto(
    Guid CustomerId,
    string? FullName,
    string PhoneNumber,
    string Status,
    DateTime? LastVisitAt,
    string? Level = null)
{
    public static CustomerMatchDto From(Customer c) => new(
        c.Id, c.FullName, c.PhoneNumber, c.Status, c.LastVisitAt, c.Level);
}

/// <summary>Request to look up customers by name and/or phone. At least one is required.</summary>
public sealed record CustomerLookupRequest
{
    [Required]
    public Guid OrganizationId { get; init; }

    [MaxLength(200)]
    public string? Name { get; init; }

    [MaxLength(50)]
    public string? PhoneNumber { get; init; }

    [MaxLength(200)]
    [EmailAddress]
    public string? Email { get; init; }
}

/// <summary>Response to a customer lookup. <see cref="IsExact"/> is true only when one match.</summary>
public sealed record CustomerLookupResponse(
    IReadOnlyList<CustomerMatchDto> Matches,
    bool IsExact,
    int Total)
{
    public static CustomerLookupResponse Empty => new([], false, 0);
}

/// <summary>A customer preference as returned to callers.</summary>
public sealed record CustomerPreferenceDto(
    Guid Id,
    string PreferenceKey,
    string PreferenceValue,
    bool IsExplicit,
    decimal Confidence)
{
    public static CustomerPreferenceDto From(CustomerPreference p) => new(
        p.Id, p.PreferenceKey, p.PreferenceValue, p.IsExplicit, p.Confidence);
}

/// <summary>A single tag on a customer.</summary>
public sealed record CustomerTagDto(string Tag);

/// <summary>
/// A customer's consent state. The property names serialise to camelCase, and the Python agent
/// reads <c>consentStatus</c> from this payload (<c>agent-service/app/agents/customer_memory/nodes.py</c>);
/// do not rename or re-case them.
/// </summary>
public sealed record CustomerConsentDto(
    Guid Id,
    string ConsentStatus,
    DateTime? ConsentGrantedAt,
    DateTime? ConsentRevokedAt,
    DateTime? DisclosureShownAt)
{
    public static CustomerConsentDto From(CustomerConsent c) => new(
        c.Id, c.ConsentStatus, c.ConsentGrantedAt, c.ConsentRevokedAt, c.DisclosureShownAt);

    /// <summary>
    /// The answer for a customer who has no consent row yet. One value for both call sites
    /// (defect D-2): the consent endpoint and the customer profile must agree.
    /// </summary>
    public static CustomerConsentDto Absent => new(
        Guid.Empty, ConsentStatuses.AbsentRow, null, null, null);
}

/// <summary>The customer profile assembled for an interaction brief or lookup.</summary>
public sealed record CustomerProfileDto(
    Guid CustomerId,
    string PhoneNumber,
    string? Email,
    string? FullName,
    string? Description,
    string Status,
    decimal TotalSpent,
    int VisitCount,
    IReadOnlyList<CustomerPreferenceDto> Preferences,
    IReadOnlyList<string> Tags,
    string ConsentStatus,
    string? Level = null)
{
    public static CustomerProfileDto From(Customer c, IReadOnlyList<string> tags, string consentStatus) => new(
        c.Id,
        c.PhoneNumber,
        c.Email,
        c.FullName,
        c.Description,
        c.Status,
        c.TotalSpent,
        c.VisitCount,
        c.Preferences.Select(CustomerPreferenceDto.From).ToList(),
        tags,
        consentStatus,
        c.Level);
}

/// <summary>Request to look up (or create) a customer by phone number.</summary>
public sealed record IdentifyCustomerRequest
{
    [Required]
    public Guid OrganizationId { get; init; }

    [Required, MaxLength(50)]
    public string PhoneNumber { get; init; } = string.Empty;

    public string? FullName { get; init; }
}

/// <summary>Request to save a new semantic memory for a customer.</summary>
public sealed record SaveMemoryRequest
{
    [Required]
    public Guid OrganizationId { get; init; }

    [Required, MaxLength(2000)]
    public string Content { get; init; } = string.Empty;

    [MaxLength(32)]
    public string Category { get; init; } = "fact";

    [MaxLength(32)]
    public string Source { get; init; } = "conversation";

    /// <summary>
    /// True when the customer stated it directly; false when it was inferred (gap A1).
    /// </summary>
    /// <remarks>
    /// Defaults to false so an omitted field is honest about what it is: a note nobody has vouched
    /// for. The agent's own extraction sets it explicitly on the statements the customer made.
    /// </remarks>
    public bool IsExplicit { get; init; }

    /// <summary>0.00 - 1.00 confidence in the memory.</summary>
    [Range(0.0, 1.0)]
    public decimal Confidence { get; init; } = 0.50m;

    /// <summary>
    /// Free-form metadata (JSONB), e.g. the originating interaction id. Defaults to <c>{}</c>.
    /// </summary>
    [MaxLength(4000)]
    public string? MetadataJson { get; init; }

    /// <summary>
    /// When the note stops being true (gap A4). Null for a note that stays true.
    /// </summary>
    public DateTime? ExpiresAt { get; init; }

    /// <summary>Optional precomputed embedding. When omitted, the content is embedded server-side.</summary>
    public float[]? Embedding { get; init; }
}

/// <summary>A saved memory as returned to callers.</summary>
public sealed record CustomerMemoryDto(
    Guid Id,
    Guid CustomerId,
    string Content,
    string Category,
    string Source,
    bool IsExplicit,
    decimal Confidence,
    string MetadataJson,
    DateTime? ExpiresAt,
    DateTime CreatedAt,
    DateTime UpdatedAt)
{
    public static CustomerMemoryDto From(CustomerMemory m) => new(
        m.Id, m.CustomerId, m.Content, m.Category, m.Source, m.IsExplicit, m.Confidence,
        m.MetadataJson, m.ExpiresAt, m.CreatedAt, m.UpdatedAt);
}

/// <summary>Request to record a stated preference for a customer (gap C4).</summary>
/// <remarks>
/// A memory and a preference are different records and the agent writes both: the memory is the
/// searchable, dated statement, and this is the canonical key/value the interaction brief's
/// summary is assembled from. Writing only the memory left the brief's own summary empty while the
/// fact sat in the store.
/// </remarks>
public sealed record SavePreferenceRequest
{
    [Required]
    public Guid OrganizationId { get; init; }

    [Required, MaxLength(64)]
    public string PreferenceKey { get; init; } = string.Empty;

    [Required, MaxLength(255)]
    public string PreferenceValue { get; init; } = string.Empty;

    [MaxLength(32)]
    public string Source { get; init; } = "conversation";

    public bool IsExplicit { get; init; }

    [Range(0.0, 1.0)]
    public decimal Confidence { get; init; } = 0.50m;
}

/// <summary>Request to correct the statement of one memory (gap A5).</summary>
/// <remarks>
/// Content is the only editable field. The category, source and confidence describe how the note
/// came to exist and what it is worth, and rewriting those from a correction form would let staff
/// launder an inference into a stated fact; withdrawing the note and stating it fresh is the honest
/// way to change them.
/// </remarks>
public sealed record CorrectMemoryRequest
{
    [Required, MaxLength(2000)]
    public string Content { get; init; } = string.Empty;
}

/// <summary>Request to semantically search a customer's memories by free-text query.</summary>
public sealed record MemorySearchRequest
{
    [Required]
    public Guid OrganizationId { get; init; }

    [Required]
    public Guid CustomerId { get; init; }

    [Required, MaxLength(500)]
    public string Query { get; init; } = string.Empty;

    public int TopK { get; init; } = 5;

    /// <summary>
    /// hybrid (default) | lexical | vector. The single-leg modes exist for retrieval evaluation;
    /// the agent always asks for the default.
    /// </summary>
    /// <remarks>
    /// The same vocabulary as the handbook search (ADR-025). Hybrid runs a pgvector cosine leg and a
    /// PostgreSQL full-text leg over the customer's live notes and fuses them (Reciprocal Rank
    /// Fusion); <c>lexical</c> and <c>vector</c> run one leg so a retrieval eval can report vector,
    /// lexical and hybrid recall separately. An absent value means <c>hybrid</c>; a value this store
    /// cannot run is refused rather than silently defaulted.
    /// </remarks>
    [MaxLength(16)]
    public string Mode { get; init; } = MemorySearchModes.Hybrid;

    /// <summary>
    /// The cosine-similarity floor a hit must clear to be returned (gap B1).
    /// </summary>
    /// <remarks>
    /// Defaults to <c>0</c> - the previous behaviour - because the two callers want opposite
    /// things: the agent retrieves context and wants nothing when nothing is relevant, while a
    /// debugging or review surface may want to see the nearest rows regardless. The floor mirrors
    /// the handbook search's <c>minSimilarity</c>, which is the same idea on the same store. It
    /// bounds the dense leg only: a lexical hit has no cosine to floor, so a post-fusion floor would
    /// delete exactly the lexical-only hits the hybrid exists to surface.
    /// </remarks>
    [Range(0.0, 1.0)]
    public double MinSimilarity { get; init; }
}

/// <summary>
/// A single memory-search hit, carrying both legs' ranks as well as the fused score so an eval can
/// report vector, lexical and hybrid recall separately instead of one opaque number (ADR-025).
/// </summary>
public sealed record MemorySearchResultDto(
    Guid Id,
    Guid CustomerId,
    string Content,
    string Category,
    string Source,
    decimal Confidence,
    bool IsExplicit,
    double? Similarity,
    long? VectorRank,
    long? LexicalRank,
    double Score)
{
    public static MemorySearchResultDto From(CustomerMemorySearchResult r) => new(
        r.Id, r.CustomerId, r.Content, r.Category, r.Source, r.Confidence, r.IsExplicit,
        r.Similarity, r.VectorRank, r.LexicalRank, r.Score);
}

/// <summary>A customer event.</summary>
public sealed record CustomerEventDto(
    Guid Id,
    string EventType,
    DateTime EventDate,
    string? Description,
    bool IsActive)
{
    public static CustomerEventDto From(CustomerEvent e) => new(
        e.Id, e.EventType, e.EventDate, e.Description, e.IsActive);
}

/// <summary>Request to add a customer event.</summary>
public sealed record AddEventRequest
{
    [Required]
    public Guid OrganizationId { get; init; }

    [Required, MaxLength(32)]
    public string EventType { get; init; } = "other";

    public DateTime EventDate { get; init; }

    [MaxLength(1000)]
    public string? Description { get; init; }
}

/// <summary>An interaction record as returned to callers.</summary>
public sealed record CustomerInteractionDto(
    Guid Id,
    Guid CustomerId,
    string Channel,
    string Direction,
    string? MessageContent,
    DateTime CreatedAt)
{
    public static CustomerInteractionDto From(CustomerInteraction i) => new(
        i.Id, i.CustomerId, i.Channel, i.Direction, i.MessageContent, i.CreatedAt);
}

/// <summary>Request to record a customer interaction.</summary>
public sealed record RecordInteractionRequest
{
    [Required]
    public Guid OrganizationId { get; init; }

    [Required, MaxLength(32)]
    public string Channel { get; init; } = "whatsapp";

    [Required, MaxLength(16)]
    public string Direction { get; init; } = "inbound";

    [MaxLength(4000)]
    public string? MessageContent { get; init; }

    public string? ParsedIntentJson { get; init; }

    public Guid? StaffMemberId { get; init; }
}

/// <summary>A customer's resolved lifecycle status (Issue #169).</summary>
public sealed record CustomerStatusDto(Guid CustomerId, string Status);

/// <summary>Request to recompute/override a customer's loyalty status.</summary>
public sealed record RecomputeStatusRequest
{
    [Required]
    public Guid OrganizationId { get; init; }

    /// <summary>
    /// Optional owner override. When supplied the status is set as-is; when omitted the status is
    /// recomputed from <c>TotalSpent</c>/<c>VisitCount</c>/<c>LastVisitAt</c>.
    /// </summary>
    [MaxLength(16)]
    public string? Status { get; init; }
}

/// <summary>Request to update a customer's consent.</summary>
public sealed record UpdateConsentRequest
{
    [Required]
    public Guid OrganizationId { get; init; }

    [Required, MaxLength(16)]
    public string ConsentStatus { get; init; } = "pending";
}

/// <summary>
/// A concise, staff-facing interaction brief summarizing who the customer is, their known
/// preferences and upcoming events, so an associate is prepared for the interaction.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Description"/> is the customer-level prose field the brief exists to lead with. It is
/// deliberately separate from <see cref="PreferenceSummary"/> (which is assembled from the
/// preference rows) and from the memories (per-fact statements the agent extracts): an associate
/// reading the brief before making contact wants to know who this person is, and a list of facts is
/// not that.
/// </para>
/// <para>
/// <see cref="Tags"/> is a list, not a joined string. It was previously serialised as
/// <c>"vip, silk"</c>, and the agent read it only when it was a list, so the tags the backend
/// actually held never reached a staff answer (the contract mismatch recorded in the memory-gap
/// analysis). The shape now matches the profile DTO this is built from.
/// </para>
/// </remarks>
public sealed record InteractionBriefDto(
    Guid CustomerId,
    string CustomerName,
    string? Description,
    string Status,
    string? PreferenceSummary,
    string? UpcomingEvents,
    IReadOnlyList<string> Tags)
{
    public static InteractionBriefDto From(
        CustomerProfileDto profile,
        IReadOnlyList<CustomerEventDto> events) => new(
        profile.CustomerId,
        profile.FullName ?? "Unknown customer",
        profile.Description,
        profile.Status,
        // The same rules as `CustomerTenantService.GetBriefAsync`: the nickname is an internal key
        // mirrored from `Customer.Nickname`, not a stated preference, and an empty value is an
        // absence rather than a fact. Rendering either put `nickname:` and `key:` on the brief.
        PreferenceSummaryOrNull(profile.Preferences),
        // Only events that have not already happened. The repository orders by date but filters on
        // `IsActive` alone, so a past event is included and the field name lied (gap B-brief).
        UpcomingEventsOrNull(events),
        profile.Tags);

    private static string? PreferenceSummaryOrNull(IReadOnlyList<CustomerPreferenceDto> preferences)
    {
        var summarised = preferences
            .Where(preference => preference.PreferenceKey != CustomerPreferenceKeys.Nickname)
            .Where(preference => !string.IsNullOrWhiteSpace(preference.PreferenceValue))
            .Select(preference => $"{preference.PreferenceKey}: {preference.PreferenceValue}")
            .ToList();
        return summarised.Count == 0 ? null : string.Join("; ", summarised);
    }

    private static string? UpcomingEventsOrNull(IReadOnlyList<CustomerEventDto> events)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var upcoming = events
            .Where(e => DateOnly.FromDateTime(e.EventDate) >= today)
            .Select(e => $"{e.EventType} on {e.EventDate:yyyy-MM-dd}")
            .ToList();
        return upcoming.Count == 0 ? null : string.Join("; ", upcoming);
    }
}
