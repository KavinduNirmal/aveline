using System.Text.Json;

namespace Aveline.Api.Modules.Billing.DTOs;

/// <summary>
/// One per-organization entitlement override (FR-4.9). <see cref="Value"/> is the raw
/// JSON value; its shape is validated against <see cref="ValueType"/> by the service.
/// </summary>
public sealed record EntitlementOverrideInput(
    string Key,
    string ValueType,
    JsonElement Value,
    string Reason,
    DateTime? EffectiveFrom = null);

/// <summary>
/// The PATCH body: a set of overrides applied together and then resolved for the
/// organization.
/// </summary>
public sealed record SetEntitlementOverridesRequest(
    IReadOnlyList<EntitlementOverrideInput>? Overrides);
