using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Billing.Repositories;

namespace Aveline.Api.Modules.Billing.Domain;

/// <summary>
/// Default <see cref="IEntitlementResolver"/>. Resolves the organisation's tier, applies
/// effective dating, then lets any organisation override replace the tier value.
/// </summary>
public sealed class EntitlementResolver(IEntitlementRepository repository) : IEntitlementResolver
{
    public async Task<EntitlementValue?> GetAsync(
        Guid organizationId, string key, DateTime? at = null, CancellationToken cancellationToken = default)
    {
        var all = await GetAllAsync(organizationId, at, cancellationToken);
        return all.TryGetValue(key, out var value) ? value : null;
    }

    public async Task<IReadOnlyDictionary<string, EntitlementValue>> GetAllAsync(
        Guid organizationId, DateTime? at = null, CancellationToken cancellationToken = default)
    {
        var asOf = at ?? DateTime.UtcNow;
        var tier = await repository.GetOrganizationPlanTierAsync(organizationId, cancellationToken);

        var planRows = await repository.ListEffectivePlanEntitlementsAsync(tier, asOf, cancellationToken);
        var overrideRows = await repository.ListEffectiveOverridesAsync(organizationId, asOf, cancellationToken);

        var resolved = new Dictionary<string, EntitlementValue>(StringComparer.Ordinal);

        // The documented defaults are always the floor. Seeding them unconditionally means
        // a partially seeded catalog still resolves an unlisted key to its documented
        // default instead of letting the caller's arbitrary fallback win (M-20).
        foreach (var (key, value) in PlanEntitlementDefaults.For(tier))
        {
            resolved[key] = value;
        }

        foreach (var group in planRows.Where(row => row.IsEnabled).GroupBy(row => row.Key, StringComparer.Ordinal))
        {
            var row = group.OrderByDescending(entry => entry.EffectiveFrom).First();
            resolved[row.Key] = new EntitlementValue(
                row.Key, row.ValueType, row.ValueDecimal, row.ValueBool, row.ValueText, "Plan", row.EffectiveFrom);
        }

        // Overrides always win, whatever the tier says.
        foreach (var group in overrideRows.GroupBy(row => row.Key, StringComparer.Ordinal))
        {
            var row = group.OrderByDescending(entry => entry.EffectiveFrom).First();
            resolved[row.Key] = new EntitlementValue(
                row.Key, row.ValueType, row.ValueDecimal, row.ValueBool, row.ValueText, "Override", row.EffectiveFrom);
        }

        return resolved;
    }

    public async Task<decimal> GetDecimalAsync(
        Guid organizationId, string key, decimal fallback, DateTime? at = null,
        CancellationToken cancellationToken = default)
    {
        var value = await GetAsync(organizationId, key, at, cancellationToken);
        if (value is null)
        {
            return fallback;
        }

        if (value.Number is { } number)
        {
            return number;
        }

        if (value.Flag is { } flag)
        {
            return flag ? 1m : 0m;
        }

        return decimal.TryParse(value.Text, out var parsed) ? parsed : fallback;
    }

    public async Task<decimal> GetTierDecimalAsync(
        PlanTier tier, string key, decimal fallback, DateTime? at = null,
        CancellationToken cancellationToken = default)
    {
        var asOf = at ?? DateTime.UtcNow;
        var planRows = await repository.ListEffectivePlanEntitlementsAsync(tier, asOf, cancellationToken);

        var row = planRows
            .Where(entry => entry.IsEnabled && string.Equals(entry.Key, key, StringComparison.Ordinal))
            .OrderByDescending(entry => entry.EffectiveFrom)
            .FirstOrDefault();

        if (row is not null)
        {
            // A corrected database row wins over the in-memory catalog.
            return Coerce(row.ValueType, row.ValueDecimal, row.ValueBool, row.ValueText, fallback);
        }

        if (PlanEntitlementDefaults.For(tier).TryGetValue(key, out var fallbackValue)
            && fallbackValue.Number is { } number)
        {
            return number;
        }

        return fallback;
    }

    private static decimal Coerce(
        EntitlementValueType valueType, decimal? number, bool? flag, string? text, decimal fallback) =>
        valueType switch
        {
            EntitlementValueType.Decimal => number ?? fallback,
            EntitlementValueType.Integer => number ?? fallback,
            EntitlementValueType.Boolean => flag is { } value ? (value ? 1m : 0m) : fallback,
            EntitlementValueType.String => decimal.TryParse(text, out var parsed) ? parsed : fallback,
            _ => fallback,
        };
}
