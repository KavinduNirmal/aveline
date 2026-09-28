using Aveline.Api.Common.MultiTenancy;
using Aveline.Api.Modules.Organizations.Models;

namespace Aveline.Api.Modules.CustomerConcierge.Models;

/// <summary>
/// A single semantic memory about a customer (preference, event, complaint, fact, sentiment).
/// Tenant-scoped.
///
/// <para>
/// Two search columns are NOT part of the EF model (the in-memory test provider cannot map the
/// pgvector or tsvector types): the pgvector <c>embedding vector(1536)</c> column with its HNSW
/// cosine index (the dense leg), and the generated <c>SearchVector tsvector</c> column with a
/// partial GIN index (the lexical leg). Both are created by migrations via raw SQL and accessed by
/// <c>CustomerMemoryRepository</c> through raw SQL (the hybrid search). See ADR-017 and ADR-025.
/// </para>
/// </summary>
public class CustomerMemory : ITenantEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; set; }

    public Guid CustomerId { get; set; }

    /// <summary>The human-readable memory statement.</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// The normalised form of <see cref="Content"/> that decides whether two statements are the
    /// same note (gap A3).
    /// </summary>
    /// <remarks>
    /// Set by <c>CustomerMemoryService</c> from <see cref="Content"/> on every write and never by a
    /// caller: it is derived data, and the whole point of storing it is that the database can carry
    /// a unique index over it, so two concurrent writes cannot both insert the same note. The
    /// normalisation itself lives in <c>Repositories.MemoryContentKey</c>, and mirrors the agent's
    /// <c>normalise_memory_content</c> so the read-path collapse and the write-path index agree.
    /// </remarks>
    public string ContentKey { get; set; } = string.Empty;

    /// <summary>preference | event | complaint | fact | sentiment</summary>
    public string Category { get; set; } = "fact";

    /// <summary>conversation | staff_note | purchase | inferred</summary>
    public string Source { get; set; } = "conversation";

    /// <summary>True when the customer stated it directly; false when inferred.</summary>
    public bool IsExplicit { get; set; }

    /// <summary>0.00 - 1.00 confidence in the memory.</summary>
    public decimal Confidence { get; set; } = 0.50m;

    /// <summary>Free-form metadata (JSONB), e.g. the originating interaction id.</summary>
    public string MetadataJson { get; set; } = "{}";

    /// <summary>
    /// When the note stops being true, or null for a note that stays true (gap A4).
    /// </summary>
    /// <remarks>
    /// A dated event memory ("has a wedding on 2026-12-01") is stale the day after; a stated
    /// preference is not. The column is what lets the query filter age a note out without deleting
    /// the row an associate might still want to see in the history.
    /// </remarks>
    public DateTime? ExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the note was withdrawn. Read through the configured query filter, so a soft-deleted
    /// memory never reaches a caller (gap A6).
    /// </summary>
    public DateTime? DeletedAt { get; set; }

    public Customer? Customer { get; set; }

    public Organization? Organization { get; set; }
}
