namespace Aveline.Api.Modules.Media;

/// <summary>The internal (service-to-service) mint request (migration plan §8.3).</summary>
public sealed class InternalMediaTokenRequest
{
    /// <summary>The tenant whose row the reference must belong to.</summary>
    public Guid OrganizationId { get; set; }

    /// <summary>One of <see cref="MediaReferenceKinds"/>; <c>externalUrl</c> is not tokenised.</summary>
    public string? ImageRefKind { get; set; }

    /// <summary>The referenced row's id.</summary>
    public Guid? ImageRefId { get; set; }

    /// <summary>The requested scope; only <c>vision.analyze</c> is permitted on this route.</summary>
    public string? Scope { get; set; }
}

/// <summary>
/// The member's mint request. The body is normally empty; a scope may be named only to assert the
/// one this route serves (<c>attachment.view</c>), and any other value is <c>403</c>.
/// </summary>
public sealed class ConversationMediaTokenRequest
{
    /// <summary>The requested scope; only <c>attachment.view</c> is permitted on this route.</summary>
    public string? Scope { get; set; }
}

/// <summary>
/// A mint's wire answer. <see cref="PublicId"/> is carried so the agent's analysis cache can key on
/// <c>{orgId}:{publicId}</c> instead of the ever-changing token URL (migration plan §7.6).
/// </summary>
public sealed record MediaTokenResponse(
    string Token,
    string Url,
    string PublicId,
    DateTimeOffset ExpiresAtUtc);
