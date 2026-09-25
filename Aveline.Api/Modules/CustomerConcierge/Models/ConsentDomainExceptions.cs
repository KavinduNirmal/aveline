namespace Aveline.Api.Modules.CustomerConcierge.Models;

/// <summary>Base type for consent failures that map to a specific HTTP status.</summary>
public abstract class ConsentDomainException(string message) : Exception(message);

/// <summary>
/// The requested consent status is not one of <c>pending</c>|<c>granted</c>|<c>revoked</c>
/// (HTTP 400, <c>invalid-consent-status</c>). A typed domain error rather than
/// <see cref="ArgumentException"/>, so a client mistake is distinguishable from a programming one
/// and the endpoint can map it deliberately instead of relying on a broad catch.
/// </summary>
public sealed class InvalidConsentStatusException(string status)
    : ConsentDomainException($"Unsupported consent status: {status}")
{
    /// <summary>The rejected value, echoed for the caller's benefit.</summary>
    public string Status { get; } = status;

    /// <summary>Machine-readable discriminator surfaced in the 400 body.</summary>
    public string Code => "invalid-consent-status";
}
