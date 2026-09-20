namespace Aveline.Api.Modules.Revenue.Models;

/// <summary>
/// Base for the revenue family's domain failures, mirroring
/// <see cref="Modules.Billing.Models.BlossomDomainException"/> so the endpoint layer can map them
/// with one switch and a stated wire code.
/// </summary>
public abstract class RevenueDomainException(string message) : Exception(message);

/// <summary>A field or business rule was not satisfied. Maps to <c>400</c>.</summary>
public sealed class RevenueValidationException(string message) : RevenueDomainException(message);

/// <summary>A referenced income entry does not exist, or does not belong to the organization. Maps to <c>404</c>.</summary>
public sealed class IncomeLedgerEntryNotFoundException(Guid entryId)
    : RevenueDomainException($"Income ledger entry '{entryId}' was not found.");

/// <summary>The organization does not exist. Maps to <c>404</c>.</summary>
public sealed class RevenueOrganizationNotFoundException(Guid organizationId)
    : RevenueDomainException($"Organization '{organizationId}' was not found.");

/// <summary>
/// A live entry already holds this <c>(SourceKind, SourceRef)</c> identity. Maps to <c>409</c> with
/// <c>code = "duplicate-revenue-entry"</c>.
/// </summary>
public sealed class DuplicateRevenueEntryException(IncomeSourceKind sourceKind, string sourceRef)
    : RevenueDomainException(
        $"A live income entry already exists for {sourceKind} reference '{sourceRef}'.")
{
    public string Code => "duplicate-revenue-entry";
}

/// <summary>
/// The supersede target is already voided, so it cannot be voided again. Maps to <c>409</c> with
/// <c>code = "income-entry-not-voidable"</c>.
/// </summary>
public sealed class IncomeEntryNotVoidableException(Guid entryId, string reason)
    : RevenueDomainException($"Income ledger entry '{entryId}' cannot be superseded: {reason}")
{
    public string Code => "income-entry-not-voidable";
}
