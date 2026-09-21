namespace Aveline.Api.Modules.Commerce.Models;

/// <summary>
/// Base for the boutique-sale ledger's domain failures, mirroring
/// <see cref="Modules.Revenue.Models.RevenueDomainException"/> so the endpoint layer can map them
/// with one switch and a stated wire code.
/// </summary>
public abstract class BoutiqueSaleDomainException(string message) : Exception(message);

/// <summary>A field or business rule was not satisfied. Maps to <c>400</c>.</summary>
public sealed class BoutiqueSaleValidationException(string message) : BoutiqueSaleDomainException(message);

/// <summary>A referenced entry does not exist, or does not belong to the organization. Maps to <c>404</c>.</summary>
public sealed class BoutiqueSaleEntryNotFoundException(Guid entryId)
    : BoutiqueSaleDomainException($"Boutique sale entry '{entryId}' was not found.");

/// <summary>The organization does not exist. Maps to <c>404</c>.</summary>
public sealed class BoutiqueSaleOrganizationNotFoundException(Guid organizationId)
    : BoutiqueSaleDomainException($"Organization '{organizationId}' was not found.");

/// <summary>
/// A live entry already holds this <c>(OrganizationId, SourceKind, SourceRef)</c> identity. Maps to
/// <c>409</c> with <c>code = "duplicate-boutique-sale-entry"</c>.
/// </summary>
public sealed class DuplicateBoutiqueSaleEntryException(BoutiqueSaleSourceKind sourceKind, string sourceRef)
    : BoutiqueSaleDomainException(
        $"A live boutique sale entry already exists for {sourceKind} reference '{sourceRef}'.")
{
    public string Code => "duplicate-boutique-sale-entry";
}

/// <summary>
/// The supersede target is already voided, so it cannot be voided again. Maps to <c>409</c> with
/// <c>code = "boutique-sale-entry-not-voidable"</c>.
/// </summary>
public sealed class BoutiqueSaleEntryNotVoidableException(Guid entryId, string reason)
    : BoutiqueSaleDomainException($"Boutique sale entry '{entryId}' cannot be superseded: {reason}")
{
    public string Code => "boutique-sale-entry-not-voidable";
}
