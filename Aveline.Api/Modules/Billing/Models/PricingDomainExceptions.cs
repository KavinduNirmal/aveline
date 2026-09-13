namespace Aveline.Api.Modules.Billing.Models;

/// <summary>Base type for pricing failures that map to a specific HTTP status.</summary>
public abstract class PricingDomainException(string message) : Exception(message);

/// <summary>Field or business validation failed (HTTP 400).</summary>
public class PricingValidationException(string message) : PricingDomainException(message)
{
    /// <summary>Machine-readable discriminator surfaced in the <c>400</c> body (docs/api/README.md §C.1).</summary>
    public virtual string Code => "validation";
}

/// <summary>
/// The declared scope disagrees with the supplied provider/model (HTTP 400,
/// <c>scope-inconsistent</c>).
/// </summary>
public sealed class PricingScopeInconsistentException(string message) : PricingValidationException(message)
{
    public override string Code => "scope-inconsistent";
}

/// <summary>The rule is no longer a Draft and cannot transition (HTTP 400, <c>rule-not-draft</c>).</summary>
public sealed class PricingRuleNotDraftException(string message) : PricingDomainException(message);

/// <summary>
/// Cancelling the rule would strand already-priced usage; supersede it instead
/// (HTTP 400, <c>rule-priced</c>).
/// </summary>
public sealed class PricingRuleHasPricedUsageException(string message) : PricingDomainException(message);

/// <summary>A backdated effective date requires <c>pricing:backdate</c> (HTTP 403).</summary>
public sealed class PricingBackdateForbiddenException(string message) : PricingDomainException(message);

/// <summary>The rule is not a Draft and may no longer be edited (HTTP 409, <c>rule-immutable</c>).</summary>
public sealed class PricingRuleImmutableException(string message) : PricingDomainException(message);

/// <summary>The rule does not exist (HTTP 404).</summary>
public sealed class PricingRuleNotFoundException(Guid ruleId)
    : PricingDomainException($"Pricing rule '{ruleId}' was not found.");

/// <summary>The effective window overlaps an existing non-cancelled rule (HTTP 409, <c>rule-overlap</c>).</summary>
public sealed class PricingRuleOverlapException(string message) : PricingDomainException(message);

/// <summary>The price-book entry does not exist (HTTP 404).</summary>
public sealed class PricingPriceEntryNotFoundException(Guid entryId)
    : PricingDomainException($"Price-book entry '{entryId}' was not found.");
