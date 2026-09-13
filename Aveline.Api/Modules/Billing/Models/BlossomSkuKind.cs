namespace Aveline.Api.Modules.Billing.Models;

/// <summary>What a commercial price entry covers (domain-model.md §3.2).</summary>
public enum BlossomSkuKind
{
    PlanAllowance,
    TopUpPack,
    OverageUsage,
}
