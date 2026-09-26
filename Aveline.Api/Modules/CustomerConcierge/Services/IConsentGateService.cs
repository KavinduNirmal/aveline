namespace Aveline.Api.Modules.CustomerConcierge.Services;

/// <summary>
/// The reasons a message did not clear consent-gated processing. They are the
/// <c>message_skip_total</c> label values (plan §9.1), so they are constants rather than free
/// text: a counter whose reasons drift is useless to alert on.
/// </summary>
public static class ConsentGateReasons
{
    /// <summary>The customer has revoked consent; the run must not happen.</summary>
    public const string ConsentRevoked = "consent_revoked";

    /// <summary>
    /// No customer is bound to the message. An unknown number cannot be consent-gated, so the
    /// message is still processed (plan §8.4) - but the gate did not clear it either.
    /// </summary>
    public const string NoCustomerContext = "no_customer_context";

    /// <summary>
    /// The consent store could not be read. Processing is refused (fail closed) rather than
    /// risking a revoked customer being served (plan §8.3).
    /// </summary>
    public const string ConsentCheckUnavailable = "consent_check_unavailable";
}

/// <summary>
/// The outcome of a consent check for one inbound message. <see cref="Reason"/> is set for every
/// outcome that was not an unconditional grant, so the caller can label its skip counter without
/// re-deriving anything.
/// </summary>
/// <param name="ShouldProcess">
/// True only when the message may be handed to the agent. <b>False</b> when consent is revoked or
/// when the check itself failed (fail closed).
/// </param>
/// <param name="Status">The resolved status: <c>granted</c>, <c>pending</c>, <c>revoked</c>, or <see cref="UnavailableStatus"/>.</param>
/// <param name="Reason">One of the <see cref="ConsentGateReasons"/> values, or null for a clean grant.</param>
public sealed record ConsentDecision(bool ShouldProcess, string Status, string? Reason)
{
    /// <summary>The status reported when the consent store could not be read. Never persisted.</summary>
    public const string UnavailableStatus = "unavailable";

    /// <summary>The message may be processed.</summary>
    public static ConsentDecision Process(string status, string? reason = null) => new(true, status, reason);

    /// <summary>The message must not be processed.</summary>
    public static ConsentDecision Skip(string status, string reason) => new(false, status, reason);

    /// <summary>The consent store failed; refuse the run rather than risk serving a revoked customer.</summary>
    public static ConsentDecision FailClosed() =>
        Skip(UnavailableStatus, ConsentGateReasons.ConsentCheckUnavailable);
}

/// <summary>
/// The single decision point for "may this message be processed at all?" (plan §8.3 Layer 1).
/// It exists so the rule is stated once: an absent customer is not gated, <c>revoked</c> is a
/// skip, and a read failure is a skip rather than an exception.
/// </summary>
public interface IConsentGateService
{
    /// <summary>
    /// Resolves the consent decision for an inbound message. Never throws for a store failure:
    /// the failure is reported as a fail-closed <see cref="ConsentDecision"/>.
    /// </summary>
    /// <param name="organizationId">The tenant the message arrived for.</param>
    /// <param name="customerId">The bound customer, or null when the number is unknown.</param>
    Task<ConsentDecision> CheckAsync(
        Guid organizationId,
        Guid? customerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The same decision, additionally consulting the erasure tombstone for
    /// <paramref name="phoneE164"/> (plan §15 Q-4, risk R-3). An erased number must not be
    /// re-processed: the identify path treats "no customer yet" as <c>pending</c>, so deleting the
    /// consent row alone would silently lapse the opt-out.
    /// </summary>
    /// <remarks>
    /// The default implementation ignores the number and delegates to the two-argument overload, so
    /// a test double that only knows the old contract keeps working; the production
    /// <c>ConsentGateService</c> overrides it. A tombstone is treated as a revocation whenever the
    /// live status is not an explicit <c>granted</c> — a customer who later re-consents must not be
    /// locked out forever.
    /// </remarks>
    Task<ConsentDecision> CheckAsync(
        Guid organizationId,
        Guid? customerId,
        string? phoneE164,
        CancellationToken cancellationToken = default)
        => CheckAsync(organizationId, customerId, cancellationToken);
}
