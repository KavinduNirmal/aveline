namespace Aveline.Api.Modules.Billing.Models;

/// <summary>How an entitlement value is interpreted (domain-model.md §4.5).</summary>
public enum EntitlementValueType
{
    Integer,
    Decimal,
    Boolean,
    String,
}
