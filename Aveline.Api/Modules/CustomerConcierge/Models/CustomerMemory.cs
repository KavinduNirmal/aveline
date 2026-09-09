using Aveline.Api.Common.MultiTenancy;
using Aveline.Api.Modules.Organizations.Models;

namespace Aveline.Api.Modules.CustomerConcierge.Models;

/// <summary>
/// A single semantic memory about a customer (preference, event, complaint, fact, sentiment).
/// Tenant-scoped.
///
/// <para>
/// The pgvector <c>embedding vector(1536)</c> column and its HNSW cosine index are NOT part of
/// the EF model (the in-memory test provider cannot map the pgvector type). They are created by
/// the migration via raw SQL and accessed by <c>CustomerMemoryRepository</c> through raw SQL
/// (cosine search). See ADR-017.
/// </para>
/// </summary>
public class CustomerMemory : ITenantEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; set; }

    public Guid CustomerId { get; set; }

    /// <summary>The human-readable memory statement.</summary>
    public string Content { get; set; } = string.Empty;

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

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? DeletedAt { get; set; }

    public Customer? Customer { get; set; }

    public Organization? Organization { get; set; }
}
