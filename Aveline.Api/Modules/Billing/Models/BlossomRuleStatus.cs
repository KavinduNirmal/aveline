namespace Aveline.Api.Modules.Billing.Models;

/// <summary>Lifecycle of an effective-dated pricing row (BR-1.6, BR-1.8).</summary>
public enum BlossomRuleStatus
{
    Draft,
    Active,
    Superseded,
    Cancelled,
}
