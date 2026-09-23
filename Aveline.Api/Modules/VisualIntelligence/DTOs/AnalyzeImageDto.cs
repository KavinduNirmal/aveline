using System.Text.Json.Serialization;

namespace Aveline.Api.Modules.VisualIntelligence.DTOs;

/// <summary>
/// The internal analyze-image request: either a named reference (the arm the agent should use) or a
/// caller-supplied external URL (the compatibility arm).
/// </summary>
/// <remarks>
/// <para>
/// <b><see cref="ImageUrl"/> stays a non-nullable <see cref="string"/> defaulting to empty</b>
/// (strategy §4 C14). Making it nullable would be a nullability change on a live DTO and would
/// alter binder behaviour, so the reference arm treats <b>empty as absent</b> instead: a stale
/// <c>""</c> never shadows a named reference, and every existing caller that omits the property
/// sees exactly the object it saw before.
/// </para>
/// <para>
/// <see cref="ImageRefKind"/> may name <c>attachment</c> or <c>inventoryImage</c> (the two
/// tokenisable row kinds, <c>MediaReferenceKinds</c>). <c>externalUrl</c> and any unknown kind fall
/// through to the <see cref="ImageUrl"/> arm, which is deliberately permissive (Q7 deferred it).
/// </para>
/// </remarks>
public class AnalyzeImageDto
{
    public Guid OrganizationId { get; set; }

    public Guid OrgId
    {
        get => OrganizationId;
        set => OrganizationId = value;
    }

    /// <summary>
    /// <c>"inventoryImage"</c> | <c>"attachment"</c> | <c>"externalUrl"</c> | <c>null</c>
    /// (legacy: use <see cref="ImageUrl"/>).
    /// </summary>
    public string? ImageRefKind { get; set; }

    /// <summary>The referenced row's id; absent means the reference arm does not apply.</summary>
    public Guid? ImageRefId { get; set; }

    /// <summary>
    /// Retained: a caller-supplied external URL, and the pre-reference contract. Non-nullable and
    /// defaulting to empty, with empty treated as absent (strategy §4 C14).
    /// </summary>
    public string ImageUrl { get; set; } = string.Empty;

    public string? FileName { get; set; }

    public string? ContextHint { get; set; }

    public string? Prompt { get; set; }
}
