using System.ComponentModel.DataAnnotations;
using Aveline.Api.Modules.CustomerConcierge.Models;
using Aveline.Api.Modules.CustomerConcierge.Repositories;

namespace Aveline.Api.Modules.CustomerConcierge.DTOs;

/// <summary>A concise customer match returned from the read-only lookup path.</summary>
public sealed record CustomerMatchDto(
    Guid CustomerId,
    string? FullName,
    string PhoneNumber,
    string Status,
    DateTime? LastVisitAt)
{
    public static CustomerMatchDto From(Customer c) => new(
        c.Id, c.FullName, c.PhoneNumber, c.Status, c.LastVisitAt);
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

/// <summary>A customer's consent status.</summary>
public sealed record CustomerConsentDto(Guid Id, string ConsentStatus)
{
    public static CustomerConsentDto From(CustomerConsent c) => new(c.Id, c.ConsentStatus);
}

/// <summary>The customer profile assembled for an interaction brief or lookup.</summary>
public sealed record CustomerProfileDto(
    Guid CustomerId,
    string PhoneNumber,
    string? Email,
    string? FullName,
    string Status,
    decimal TotalSpent,
    int VisitCount,
    IReadOnlyList<CustomerPreferenceDto> Preferences,
    IReadOnlyList<string> Tags,
    string ConsentStatus)
{
    public static CustomerProfileDto From(Customer c, IReadOnlyList<string> tags, string consentStatus) => new(
        c.Id,
        c.PhoneNumber,
        c.Email,
        c.FullName,
        c.Status,
        c.TotalSpent,
        c.VisitCount,
        c.Preferences.Select(CustomerPreferenceDto.From).ToList(),
        tags,
        consentStatus);
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

    public bool IsExplicit { get; init; }

    public decimal Confidence { get; init; } = 0.50m;

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
    DateTime CreatedAt)
{
    public static CustomerMemoryDto From(CustomerMemory m) => new(
        m.Id, m.CustomerId, m.Content, m.Category, m.Source, m.IsExplicit, m.Confidence, m.CreatedAt);
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
}

/// <summary>A single semantic-search hit with its cosine similarity.</summary>
public sealed record MemorySearchResultDto(
    Guid Id,
    Guid CustomerId,
    string Content,
    string Category,
    decimal Confidence,
    double Similarity)
{
    public static MemorySearchResultDto From(CustomerMemorySearchResult r) => new(
        r.Id, r.CustomerId, r.Content, r.Category, r.Confidence, r.Similarity);
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
public sealed record InteractionBriefDto(
    Guid CustomerId,
    string CustomerName,
    string Status,
    string? PreferenceSummary,
    string? UpcomingEvents,
    string? Tags)
{
    public static InteractionBriefDto From(
        CustomerProfileDto profile,
        IReadOnlyList<CustomerEventDto> events) => new(
        profile.CustomerId,
        profile.FullName ?? "Unknown customer",
        profile.Status,
        profile.Preferences.Count == 0 ? null
            : string.Join("; ", profile.Preferences.Select(p => $"{p.PreferenceKey}: {p.PreferenceValue}")),
        events.Count == 0 ? null
            : string.Join("; ", events.Select(e => $"{e.EventType} on {e.EventDate:yyyy-MM-dd}")),
        profile.Tags.Count == 0 ? null : string.Join(", ", profile.Tags));
}
