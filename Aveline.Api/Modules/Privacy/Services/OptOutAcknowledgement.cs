using Aveline.Api.Modules.Integrations.Services;
using Microsoft.Extensions.Caching.Distributed;

namespace Aveline.Api.Modules.Privacy.Services;

/// <summary>
/// Builds the single opt-out acknowledgement (plan §5.2 step 4, §15 Q-9). It is deliberately
/// generic: no name, no order, no history, and no personalisation of any kind, because the customer
/// has just asked to be left alone and a helpful-sounding detail would be a contradiction.
/// </summary>
public interface IOptOutAcknowledgementBodyBuilder
{
    /// <summary>The current version marker, recorded in the audit evidence.</summary>
    string CurrentVersion { get; }

    /// <summary>Renders the body for the boutique that held the consent.</summary>
    string Build(string organizationName);
}

/// <summary>
/// The opt-out acknowledgement text. Fixed copy, with the boutique name as the only substitution.
/// </summary>
public sealed class OptOutAcknowledgementBodyBuilder : IOptOutAcknowledgementBodyBuilder
{
    /// <summary>The version recorded on the consent audit row.</summary>
    public const string CurrentVersionValue = "v1";

    /// <inheritdoc />
    public string CurrentVersion => CurrentVersionValue;

    /// <inheritdoc />
    public string Build(string organizationName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(organizationName);

        return
            $"You have opted out of messages from {organizationName.Trim()}. "
            + "We have stopped processing your messages and will not contact you again. "
            + "This is the only confirmation you will receive.";
    }
}

/// <summary>
/// The exactly-once gate for the opt-out acknowledgement (plan §11 item 4.5). A short-lived Redis key
/// is the concurrency control: the first caller for a (boutique, number) inside the window wins and
/// the rest send nothing.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a cache key rather than a column.</b> Pr5 adds no schema change and the acknowledgement is
/// not a consent state - it is a rate limit on an outbound message. A key with a TTL expresses
/// "at most one per 24 hours" without a row that would have to be cleaned up.
/// </para>
/// <para>
/// <b>Fail closed.</b> If the gate cannot be read, no message is sent: an outage must not turn the
/// "one acknowledgement" rule into "one per retry" (the same reasoning as the OTP counters, DR-6).
/// </para>
/// <para>
/// <b>A failed send releases the gate</b> so a provider outage delays the acknowledgement instead of
/// losing it, while a provider that accepted the message keeps the gate for the full window.
/// </para>
/// </remarks>
public interface IOptOutAcknowledgementGate
{
    /// <summary>
    /// Claims the window for this (boutique, number). <c>true</c> only for the caller that may send.
    /// Throws <see cref="OtpStoreUnavailableException"/> when the store is unreachable, because the
    /// correct answer is then "send nothing", not "send anyway".
    /// </summary>
    Task<bool> TryClaimAsync(
        Guid organizationId, string phoneE164, CancellationToken cancellationToken = default);

    /// <summary>Releases a claim whose send did not reach the provider, so a later attempt retries.</summary>
    Task ReleaseAsync(
        Guid organizationId, string phoneE164, CancellationToken cancellationToken = default);
}

/// <summary>
/// The acknowledgement dispatcher: gate, build, send, audit - in that order (plan §11 item 4.5,
/// §5.2 step 4).
/// </summary>
public interface IOptOutAcknowledgementService
{
    /// <summary>
    /// Sends the acknowledgement once per 24 hours, or reports that it was already sent. Never
    /// throws for a provider or configuration failure; those are outcomes.
    /// </summary>
    Task<OptOutAcknowledgementResult> SendOnceAsync(
        Guid organizationId,
        Guid customerId,
        string phoneE164,
        string organizationName,
        CancellationToken cancellationToken = default);
}

/// <summary>The outcome of one acknowledgement attempt.</summary>
public enum OptOutAcknowledgementOutcome
{
    /// <summary>The acknowledgement was handed to the provider.</summary>
    Sent,

    /// <summary>One was already sent inside the 24-hour window; nothing was sent.</summary>
    AlreadyAcknowledged,

    /// <summary>The boutique has no usable WhatsApp channel; nothing was sent.</summary>
    NotConfigured,

    /// <summary>The provider refused the message; the gate was released so a retry can happen.</summary>
    Failed,

    /// <summary>The gate store is unreachable; nothing was sent (fail closed).</summary>
    GateUnavailable,
}

/// <summary>One acknowledgement outcome, with the provider id when it sent.</summary>
public sealed record OptOutAcknowledgementResult(
    OptOutAcknowledgementOutcome Outcome,
    string? ProviderMessageId = null);
