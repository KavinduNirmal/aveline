namespace Aveline.Api.Modules.Billing.Models;

/// <summary>
/// Raised when an admin entitlement override is malformed (unknown key, unknown value
/// type, or a value that does not match its declared type).
/// </summary>
public sealed class EntitlementOverrideValidationException(string message)
    : BillingDomainException(message);
