namespace Aveline.Api.Modules.Handbook.Models;

/// <summary>
/// One retrievable slice of the Aveline handbook (ADR-025).
///
/// <para>
/// Global, not tenant-scoped: the product documentation and the company facts describe Aveline
/// itself, not a boutique, so there is no <c>OrganizationId</c>. <see cref="Audience"/> is the
/// only access dimension, and it exists from day one so a future customer-facing retrieval cannot
/// leak staff-only pages into a client conversation.
/// </para>
///
/// <para>
/// The two search columns are deliberately NOT part of the EF model, exactly as
/// <c>CustomerMemory.embedding</c> is not (ADR-017): the in-memory test provider cannot map
/// pgvector's <c>vector</c> or PostgreSQL's <c>tsvector</c>, so adding them would break model
/// validation for the whole in-memory suite. They are created by the migration via raw SQL and
/// read/written by <c>HandbookRepository</c> through raw SQL. See
/// <c>docs/architecture/handbook.md</c>.
/// </para>
/// </summary>
public class HandbookChunk
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Stable source id, e.g. <c>web-docs/salon</c> or <c>company/plans-and-pricing</c>.</summary>
    public string SourceKey { get; set; } = string.Empty;

    /// <summary><c>web-docs</c> | <c>company</c> | <c>legal</c>.</summary>
    public string SourceKind { get; set; } = "company";

    /// <summary>The page title a reader would recognise, e.g. "Salon".</summary>
    public string SourceTitle { get; set; } = string.Empty;

    /// <summary>Where to read more, e.g. <c>/docs/salon</c> or <c>/contact</c>.</summary>
    public string SourceUrl { get; set; } = string.Empty;

    /// <summary>Heading trail, e.g. <c>Pieces › Add a piece</c>.</summary>
    public string HeadingPath { get; set; } = string.Empty;

    /// <summary>Slug anchor for a deep link, when the heading has one.</summary>
    public string? Anchor { get; set; }

    /// <summary>The chunk body. Markdown, with inline code left intact.</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// sha256 of the normalised content. Lets a re-seed recognise an unchanged chunk and skip
    /// re-embedding it, which is what makes the seeder cheap to run repeatedly.
    /// </summary>
    public string ContentHash { get; set; } = string.Empty;

    /// <summary><c>staff</c> | <c>customer</c> | <c>both</c>. Retrieval always filters on this.</summary>
    public string Audience { get; set; } = "staff";

    /// <summary>Position of the chunk within its source; makes the upsert idempotent.</summary>
    public int Ordinal { get; set; }

    /// <summary>Optional topic tags (JSONB).</summary>
    public string TagsJson { get; set; } = "{}";

    /// <summary>False retires a chunk from retrieval without deleting its row.</summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
