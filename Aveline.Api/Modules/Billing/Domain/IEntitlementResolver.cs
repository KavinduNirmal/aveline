using Aveline.Api.Modules.Billing.Models;

namespace Aveline.Api.Modules.Billing.Domain;

/// <summary>A resolved entitlement value and where it came from.</summary>
public sealed record EntitlementValue(
    string Key,
    EntitlementValueType ValueType,
    decimal? Number,
    bool? Flag,
    string? Text,
    string Source,
    DateTime EffectiveFrom);

/// <summary>
/// The single source of plan truth. Replaces the hardcoded plan maps (defects D-1, D-2,
/// D-12). An organisation override always wins over the tier row (BR-2.15).
/// </summary>
public interface IEntitlementResolver
{
    Task<EntitlementValue?> GetAsync(
        Guid organizationId, string key, DateTime? at = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, EntitlementValue>> GetAllAsync(
        Guid organizationId, DateTime? at = null, CancellationToken cancellationToken = default);

    Task<decimal> GetDecimalAsync(
        Guid organizationId, string key, decimal fallback, DateTime? at = null,
        CancellationToken cancellationToken = default);
}
