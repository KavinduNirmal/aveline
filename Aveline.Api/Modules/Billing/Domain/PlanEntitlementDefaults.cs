using Aveline.Api.Modules.Billing.Models;

namespace Aveline.Api.Modules.Billing.Domain;

/// <summary>
/// The default plan entitlement catalog, reproducing the tables in
/// <c>docs/architecture/pricing_plan.md</c>. It is the single source for the M4 seed and
/// the resolver's fallback when the catalog has not been seeded (for example an in-memory
/// test database where migrations do not run). Once M4 has seeded the catalog, the
/// database rows are authoritative.
/// </summary>
public static class PlanEntitlementDefaults
{
    public static readonly DateTime EffectiveFrom = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly Dictionary<PlanTier, Dictionary<string, EntitlementValue>> Catalog = Build();

    /// <summary>Returns the default entitlement values for a tier keyed by entitlement key.</summary>
    public static IReadOnlyDictionary<string, EntitlementValue> For(PlanTier tier) =>
        Catalog.TryGetValue(tier, out var values)
            ? values
            : new Dictionary<string, EntitlementValue>(StringComparer.Ordinal);

    private static Dictionary<PlanTier, Dictionary<string, EntitlementValue>> Build()
    {
        var tiers = new[]
        {
            PlanTier.Seed, PlanTier.Bloom, PlanTier.Orchid, PlanTier.Rose, PlanTier.Enterprise,
        };

        var numeric = new (string Key, EntitlementValueType Type, decimal[] Values)[]
        {
            ("blossoms.monthly", EntitlementValueType.Decimal, [150m, 750m, 2000m, 5000m, 9999m]),
            ("staff.max", EntitlementValueType.Integer, [1m, 3m, 10m, 25m, 9999m]),
            ("customers.active.max", EntitlementValueType.Integer, [50m, 250m, 1000m, 5000m, 999999m]),
            ("api.requests.monthly", EntitlementValueType.Integer, [0m, 0m, 0m, 1000000m, 10000000m]),
            ("api.requests.perMinute", EntitlementValueType.Integer, [30m, 60m, 300m, 600m, 6000m]),
            ("whatsapp.monthly", EntitlementValueType.Integer, [25m, 500m, 2000m, 5000m, 99999m]),
            ("stats.retentionDays", EntitlementValueType.Integer, [90m, 180m, 400m, 400m, 400m]),
        };

        var text = new (string Key, string[] Values)[]
        {
            ("agents.visual", ["limited", "full", "full", "full", "full"]),
            ("agents.commerce", ["limited", "limited", "full", "full", "full"]),
            ("ai.customContext", ["none", "basic", "full", "full", "full"]),
            ("analytics.level", ["basic", "basic", "advanced", "advanced", "advanced"]),
            ("automation.level", ["none", "basic", "advanced", "advanced", "advanced"]),
        };

        var boolean = new (string Key, bool[] Values)[]
        {
            ("api.access", [false, false, false, true, true]),
            ("ai.customAgents", [false, false, false, true, true]),
        };

        var catalog = new Dictionary<PlanTier, Dictionary<string, EntitlementValue>>();

        for (var index = 0; index < tiers.Length; index++)
        {
            var values = new Dictionary<string, EntitlementValue>(StringComparer.Ordinal);

            foreach (var (key, type, perTier) in numeric)
            {
                values[key] = new EntitlementValue(key, type, perTier[index], null, null, "Plan", EffectiveFrom);
            }

            foreach (var (key, perTier) in text)
            {
                values[key] = new EntitlementValue(
                    key, EntitlementValueType.String, null, null, perTier[index], "Plan", EffectiveFrom);
            }

            foreach (var (key, perTier) in boolean)
            {
                values[key] = new EntitlementValue(
                    key, EntitlementValueType.Boolean, null, perTier[index], null, "Plan", EffectiveFrom);
            }

            catalog[tiers[index]] = values;
        }

        return catalog;
    }
}
