namespace Aveline.Api.Modules.Payments.Domain;

/// <summary>
/// Base for the payment family's domain failures, mirroring
/// <c>Modules.Billing.Models.BlossomDomainException</c> and
/// <c>Modules.Revenue.Models.RevenueDomainException</c> so the endpoint layer can map the whole
/// family with one switch and a stated wire code.
/// </summary>
/// <remarks>
/// The concrete members are the seven exception types of plan §6.2; this abstract base exists so
/// the contract test suite and the endpoint mapping can address them as one family.
/// </remarks>
public abstract class PaymentDomainException : Exception
{
    protected PaymentDomainException(string message)
        : base(message)
    {
    }

    protected PaymentDomainException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// The HTTP status the endpoint layer maps this failure to, or null when the failure is never
    /// surfaced to a caller (for example a duplicate webhook, which is a replay and not an error).
    /// </summary>
    public virtual int? StatusCode => null;

    /// <summary>The stable wire code, or null when the status alone is the contract.</summary>
    public virtual string? ErrorCode => null;
}

/// <summary>
/// The configured provider is missing, disabled, or (for the mock) disallowed in this environment.
/// Maps to <c>503 payment-provider-unavailable</c>.
/// </summary>
public sealed class PaymentProviderNotConfiguredException(string message)
    : PaymentDomainException(message)
{
    public override int? StatusCode => 503;

    public override string? ErrorCode => "payment-provider-unavailable";
}

/// <summary>
/// A call was made that <see cref="IPaymentProvider.Capabilities"/> says is unsupported. Maps to
/// <c>501 payment-provider-capability-missing</c>.
/// </summary>
public sealed class PaymentProviderNotSupportedException(string message)
    : PaymentDomainException(message)
{
    public override int? StatusCode => 501;

    public override string? ErrorCode => "payment-provider-capability-missing";
}

/// <summary>
/// A webhook signature, secret, or timestamp was invalid. Maps to <c>403</c> with an empty body,
/// matching the WhatsApp webhook convention, so an attacker is not told which part failed.
/// </summary>
public sealed class PaymentWebhookVerificationException(string reason)
    : PaymentDomainException($"The payment webhook could not be verified: {reason}")
{
    public override int? StatusCode => 403;
}

/// <summary>An unknown intent for this organisation. Maps to <c>404</c>.</summary>
public sealed class PaymentIntentNotFoundException : PaymentDomainException
{
    public PaymentIntentNotFoundException(Guid intentId)
        : base($"Payment intent '{intentId}' was not found.")
    {
        IntentId = intentId;
    }

    /// <summary>
    /// The 404 for an event that names an intent Aveline does not hold, or an intent belonging to
    /// another organisation. The message deliberately does not distinguish the two: plan §6.6 fails
    /// tenant isolation closed, and a caller must not be able to probe for another tenant's ids.
    /// </summary>
    public PaymentIntentNotFoundException(string message)
        : base(message)
    {
    }

    public Guid? IntentId { get; }

    public override int? StatusCode => 404;
}

/// <summary>
/// A confirm, cancel, or refund was attempted in the wrong state. Maps to
/// <c>409 payment-intent-state</c>.
/// </summary>
public sealed class PaymentIntentStateException(string message)
    : PaymentDomainException(message)
{
    public override int? StatusCode => 409;

    public override string? ErrorCode => "payment-intent-state";
}

/// <summary>
/// The provider could not be reached. Maps to <c>502 payment-provider-error</c>, and the intent is
/// left in <c>RequiresAction</c> or <c>Processing</c>, never <c>Failed</c>, because an unknown
/// outcome is not a failure.
/// </summary>
public sealed class PaymentProviderTransportException(string message, Exception? innerException = null)
    : PaymentDomainException(message, innerException)
{
    public override int? StatusCode => 502;

    public override string? ErrorCode => "payment-provider-error";
}

/// <summary>
/// The webhook inbox already holds the event id. Not surfaced to the caller: a provider retry is a
/// replay, logged and swallowed as a no-op rather than reported as an error.
/// </summary>
public sealed class DuplicateProviderEventException(string provider, string providerEventId)
    : PaymentDomainException(
        $"Provider event '{providerEventId}' from '{provider}' has already been received.")
{
    public string Provider { get; } = provider;

    public string ProviderEventId { get; } = providerEventId;
}

/// <summary>
/// A settled event's amount or currency does not match the intent it names. Maps to
/// <c>409 payment-intent-mismatch</c>.
/// </summary>
/// <remarks>
/// Plan §6.6 treats this as a real security event rather than a reconciliation to be netted away:
/// the inbound event is stored with a null <c>ProcessedAt</c> and a <c>ProcessingError</c>, the
/// critical <c>aveline.payment.settlement{outcome="mismatch"}</c> series moves, and nothing is
/// granted.
/// </remarks>
public sealed class PaymentIntentMismatchException(string message)
    : PaymentDomainException(message)
{
    public override int? StatusCode => 409;

    public override string? ErrorCode => "payment-intent-mismatch";
}

/// <summary>
/// A client idempotency key was reused with a materially different request. Maps to
/// <c>409 idempotency-key-reuse</c>, the code the shared <c>IdempotencyEndpointFilter</c> already
/// returns for the same condition.
/// </summary>
/// <remarks>
/// The filter catches this at the HTTP boundary by comparing request hashes; this exception is the
/// service's own belt-and-braces guard for a direct call and for the case where the filter's stored
/// response has expired while the intent row's unique tuple has not.
/// </remarks>
public sealed class PaymentIdempotencyKeyReuseException(string idempotencyKey, Exception? innerException = null)
    : PaymentDomainException(
        $"The idempotency key '{idempotencyKey}' was already used with a different payment request.",
        innerException)
{
    public override int? StatusCode => 409;

    public override string? ErrorCode => "idempotency-key-reuse";

    public string IdempotencyKey { get; } = idempotencyKey;
}
