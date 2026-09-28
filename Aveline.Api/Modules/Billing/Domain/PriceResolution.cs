using Aveline.Api.Modules.Billing.Models;

namespace Aveline.Api.Modules.Billing.Domain;

/// <summary>Which scope of the price book a resolved subscription price came from.</summary>
public enum PriceScopeKind
{
    /// <summary>A per-organisation contract override (<c>OrganizationId</c> is set).</summary>
    Organization,

    /// <summary>The plan tier's list price (<c>OrganizationId IS NULL</c>, <c>PlanTier</c> matches).</summary>
    PlanTier,

    /// <summary>The global list price (<c>OrganizationId IS NULL</c> and <c>PlanTier IS NULL</c>).</summary>
    Global,
}

/// <summary>
/// The outcome of resolving one subscription price from the price book.
/// </summary>
/// <remarks>
/// <see cref="PriceLkr"/> is <c>null</c> when no active row is effective at the requested instant.
/// That is deliberately not the same as a zero-priced row: zero is a real free plan, while null
/// means "this plan is not priced" and must never be coerced to zero (the distinction the plan's
/// §9.1 calls out).
/// </remarks>
public sealed record PriceResolution(BlossomPriceEntry? Entry, PriceScopeKind? Scope)
{
    /// <summary>No active price-book row applies. Not a zero.</summary>
    public static PriceResolution Missing { get; } = new(null, null);

    public decimal? PriceLkr => Entry?.PriceLkr;

    public bool IsMissing => Entry is null;
}

/// <summary>
/// The price-book selection rules. They are pure decisions over rows so a caller can read them
/// however it likes; <c>EntitlementResolver</c> is the shape <see cref="Resolve"/> mirrors.
/// </summary>
public static class PriceBookSelection
{
    /// <summary>
    /// Resolves the subscription price for one organisation and tier: an <c>OrganizationId</c>
    /// override wins, then the <c>PlanTier</c> list price, then the global row. Within the winning
    /// scope the newest <c>EffectiveFrom &lt;= at</c> inside its effective window wins.
    /// </summary>
    public static PriceResolution Resolve(
        IEnumerable<BlossomPriceEntry> entries, Guid organizationId, PlanTier planTier, DateTime at)
    {
        var effective = entries.Where(entry => IsEffective(entry, at));

        if (Newest(effective.Where(entry => entry.OrganizationId == organizationId)) is { } organizationRow)
        {
            return new PriceResolution(organizationRow, PriceScopeKind.Organization);
        }

        if (Newest(effective.Where(entry => entry.OrganizationId is null && entry.PlanTier == planTier))
            is { } tierRow)
        {
            return new PriceResolution(tierRow, PriceScopeKind.PlanTier);
        }

        if (Newest(effective.Where(entry => entry.OrganizationId is null && entry.PlanTier is null))
            is { } globalRow)
        {
            return new PriceResolution(globalRow, PriceScopeKind.Global);
        }

        return PriceResolution.Missing;
    }

    /// <summary>
    /// The active, effective-window row for one SKU at <paramref name="at"/> — the newest when more
    /// than one is in force. The top-up catalogue and the top-up purchase both select through this,
    /// so a pack the catalogue offers cannot be rejected at purchase and vice versa.
    /// </summary>
    public static BlossomPriceEntry? SelectActiveSku(
        IEnumerable<BlossomPriceEntry> entries, string skuCode, DateTime at) =>
        entries
            .Where(entry => string.Equals(entry.SkuCode, skuCode, StringComparison.Ordinal)
                            && IsEffective(entry, at))
            .OrderByDescending(entry => entry.EffectiveFrom)
            .FirstOrDefault();

    /// <summary>
    /// The effective window: only an <c>Active</c> row whose <c>EffectiveFrom</c> is inclusive and
    /// whose <c>EffectiveTo</c> is exclusive contains <paramref name="at"/> (domain-model.md §3.2).
    /// </summary>
    public static bool IsEffective(BlossomPriceEntry entry, DateTime at) =>
        entry.Status == BlossomRuleStatus.Active
        && entry.EffectiveFrom <= at
        && (entry.EffectiveTo is null || entry.EffectiveTo > at);

    private static BlossomPriceEntry? Newest(IEnumerable<BlossomPriceEntry> entries) =>
        entries.OrderByDescending(entry => entry.EffectiveFrom).FirstOrDefault();
}
