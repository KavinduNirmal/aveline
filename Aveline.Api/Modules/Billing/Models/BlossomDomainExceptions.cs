namespace Aveline.Api.Modules.Billing.Models;

/// <summary>Base type for Blossom ledger failures that map to a specific HTTP status.</summary>
public abstract class BlossomDomainException(string message) : Exception(message);

/// <summary>Field or business validation failed (HTTP 400).</summary>
public sealed class BlossomValidationException(string message) : BlossomDomainException(message);

/// <summary>The period is closed to entitlement mutations (HTTP 409, <c>period-closed</c>).</summary>
public sealed class PeriodClosedException()
    : BlossomDomainException("The billing period is closed to entitlement changes.");

/// <summary>A debit would overdraw the balance (HTTP 409, <c>insufficient-balance</c>).</summary>
public sealed class InsufficientBalanceException(decimal available, decimal requested)
    : BlossomDomainException($"The debit would overdraw the balance. Available: {available}, requested: {requested}.")
{
    public decimal Available { get; } = available;

    public decimal Requested { get; } = requested;
}

/// <summary>The referenced grant cannot be revoked (HTTP 409, <c>grant-not-revocable</c>).</summary>
public sealed class GrantNotRevocableException(decimal availableToRevoke, string message)
    : BlossomDomainException(message)
{
    public decimal AvailableToRevoke { get; } = availableToRevoke;
}

/// <summary>Optimistic concurrency could not be resolved (HTTP 409, <c>concurrent-modification</c>).</summary>
public sealed class ConcurrentModificationException()
    : BlossomDomainException("The balance was modified concurrently; please retry.");

/// <summary>The organisation (or its account) does not exist (HTTP 404).</summary>
public sealed class BlossomOrganizationNotFoundException(Guid organizationId)
    : BlossomDomainException($"Organization '{organizationId}' was not found.");

/// <summary>The referenced ledger entry does not exist (HTTP 404).</summary>
public sealed class BlossomLedgerEntryNotFoundException(Guid entryId)
    : BlossomDomainException($"Ledger entry '{entryId}' was not found.");
