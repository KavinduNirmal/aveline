namespace Aveline.Api.Modules.Billing.Models;

/// <summary>Domain exceptions for the Billing module.</summary>
public abstract class BillingDomainException(string message) : Exception(message);

/// <summary>
/// Raised when a usage record submission references an organisation that does not exist.
/// </summary>
public sealed class OrganizationNotFoundException(Guid organizationId)
    : BillingDomainException($"Organisation '{organizationId}' was not found.");

/// <summary>
/// Raised when the usage record payload fails basic validation before processing.
/// </summary>
public sealed class InvalidUsageRecordException(string reason)
    : BillingDomainException($"Invalid usage record: {reason}");
