using System.Text.Json;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Audit.Models;
using Aveline.Api.Modules.Audit.Services;
using Aveline.Api.Modules.Billing.Domain;
using Aveline.Api.Modules.Billing.DTOs;
using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Billing.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.Billing.Services;

/// <summary>
/// Default <see cref="IEntitlementOverrideService"/>. Validates each override against
/// the entitlement catalog, upserts it on <c>(OrganizationId, Key, EffectiveFrom)</c>,
/// records an audit entry, then re-resolves the organization so the caller sees the
/// effective result rather than the raw rows.
/// </summary>
public sealed class EntitlementOverrideService(
    AppDbContext db,
    IEntitlementRepository repository,
    IEntitlementResolver resolver,
    IAuditService audit) : IEntitlementOverrideService
{
    private const int MaxKeyLength = 64;
    private const int MaxReasonLength = 500;

    public async Task<IReadOnlyList<EntitlementItemView>> SetOverridesAsync(
        Guid organizationId,
        Guid actorUserId,
        IReadOnlyList<EntitlementOverrideInput> overrides,
        CancellationToken cancellationToken = default)
    {
        var organizationExists = await db.Organizations
            .AnyAsync(organization => organization.Id == organizationId, cancellationToken);
        if (!organizationExists)
        {
            throw new OrganizationNotFoundException(organizationId);
        }

        if (overrides is null || overrides.Count == 0)
        {
            throw new EntitlementOverrideValidationException("At least one override is required.");
        }

        var applied = new List<PlanEntitlementOverride>(overrides.Count);

        foreach (var input in overrides)
        {
            var key = ValidateKey(input.Key);
            var valueType = ParseValueType(input.ValueType);
            var (number, flag, text) = ParseValue(key, valueType, input.Value);
            var reason = ValidateReason(input.Reason);

            applied.Add(await repository.UpsertOverrideAsync(new PlanEntitlementOverride
            {
                OrganizationId = organizationId,
                Key = key,
                ValueType = valueType,
                ValueDecimal = number,
                ValueBool = flag,
                ValueText = text,
                EffectiveFrom = NormaliseEffectiveFrom(input.EffectiveFrom),
                Reason = reason,
                CreatedByUserId = actorUserId,
                CreatedAt = DateTime.UtcNow,
            }, cancellationToken));
        }

        var resolved = await resolver.GetAllAsync(organizationId, at: null, cancellationToken);

        await audit.RecordAsync(new AuditEntryRequest(
            Action: AuditAction.EntitlementOverrideUpdated,
            EntityType: nameof(PlanEntitlementOverride),
            EntityId: organizationId.ToString(),
            OrganizationId: organizationId,
            ActorKind: AuditActorKind.User,
            ActorUserId: actorUserId == Guid.Empty ? null : actorUserId,
            After: applied.Select(entry => new
            {
                entry.Key,
                ValueType = entry.ValueType.ToString(),
                Value = ValueOf(entry),
                entry.EffectiveFrom,
                entry.Reason,
            }),
            Reason: BuildReason(applied)), cancellationToken);

        return resolved.Values
            .OrderBy(value => value.Key, StringComparer.Ordinal)
            .Select(value => new EntitlementItemView(
                value.Key,
                value.ValueType.ToString(),
                value.ValueType switch
                {
                    EntitlementValueType.Boolean => value.Flag,
                    EntitlementValueType.String => value.Text,
                    _ => value.Number,
                },
                value.Source,
                value.EffectiveFrom))
            .ToArray();
    }

    private static string ValidateKey(string? key)
    {
        var trimmed = key?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || trimmed.Length > MaxKeyLength)
        {
            throw new EntitlementOverrideValidationException(
                $"'key' must be 1..{MaxKeyLength} characters.");
        }

        if (!PlanEntitlementDefaults.KnownKeys.Contains(trimmed))
        {
            throw new EntitlementOverrideValidationException(
                $"'{trimmed}' is not a known entitlement key.");
        }

        return trimmed;
    }

    private static EntitlementValueType ParseValueType(string? valueType)
    {
        if (Enum.TryParse<EntitlementValueType>(valueType, ignoreCase: true, out var parsed)
            && Enum.IsDefined(parsed))
        {
            return parsed;
        }

        throw new EntitlementOverrideValidationException(
            $"'{valueType}' is not a valid entitlement value type.");
    }

    private static (decimal? Number, bool? Flag, string? Text) ParseValue(
        string key, EntitlementValueType valueType, JsonElement value)
    {
        switch (valueType)
        {
            case EntitlementValueType.Integer:
            case EntitlementValueType.Decimal:
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out var number))
                {
                    throw new EntitlementOverrideValidationException(
                        $"The value of '{key}' must be a number.");
                }

                return (number, null, null);

            case EntitlementValueType.Boolean:
                if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    throw new EntitlementOverrideValidationException(
                        $"The value of '{key}' must be a boolean.");
                }

                return (null, value.GetBoolean(), null);

            case EntitlementValueType.String:
                if (value.ValueKind != JsonValueKind.String)
                {
                    throw new EntitlementOverrideValidationException(
                        $"The value of '{key}' must be a string.");
                }

                return (null, null, value.GetString());

            default:
                throw new EntitlementOverrideValidationException(
                    $"'{key}' has an unsupported entitlement value type.");
        }
    }

    private static string ValidateReason(string? reason)
    {
        var trimmed = reason?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || trimmed.Length > MaxReasonLength)
        {
            throw new EntitlementOverrideValidationException(
                $"'reason' must be 1..{MaxReasonLength} characters.");
        }

        return trimmed;
    }

    private static DateTime NormaliseEffectiveFrom(DateTime? effectiveFrom)
    {
        var value = effectiveFrom ?? DateTime.UtcNow;
        return value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
    }

    private static object? ValueOf(PlanEntitlementOverride entry) => entry.ValueType switch
    {
        EntitlementValueType.Boolean => entry.ValueBool,
        EntitlementValueType.String => entry.ValueText,
        _ => entry.ValueDecimal,
    };

    private static string BuildReason(IReadOnlyList<PlanEntitlementOverride> applied)
    {
        var reason = string.Join("; ", applied.Select(entry => entry.Reason));
        return reason.Length <= MaxReasonLength ? reason : reason[..MaxReasonLength];
    }
}
