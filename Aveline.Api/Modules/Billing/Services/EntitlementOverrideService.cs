using System.Text.Json;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Audit.Models;
using Aveline.Api.Modules.Audit.Services;
using Aveline.Api.Modules.Billing.Domain;
using Aveline.Api.Modules.Billing.DTOs;
using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Billing.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Aveline.Api.Modules.Billing.Services;

/// <summary>
/// Default <see cref="IEntitlementOverrideService"/>. Validates every override against the
/// entitlement catalog, upserts it on <c>(OrganizationId, Key, EffectiveFrom)</c>, records
/// an audit entry, then re-resolves the organization so the caller sees the effective result
/// rather than the raw rows. The whole batch is validated up front and applied in one
/// transaction, so a rejected entry cannot leave a committed, unaudited prefix behind
/// (§3.8(b)); overlapping windows for the same key are refused (§3.8(c)).
/// </summary>
public sealed class EntitlementOverrideService(
    AppDbContext db,
    IEntitlementRepository repository,
    IEntitlementResolver resolver,
    IAuditService audit) : IEntitlementOverrideService
{
    private const int MaxKeyLength = 64;
    private const int MaxReasonLength = 500;
    private const int MaxTextLength = 200;

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

        // Validate and materialise the whole batch before writing anything. A bad second
        // entry must not leave the first one committed without its audit row (§3.8(b)).
        var prepared = new List<PlanEntitlementOverride>(overrides.Count);
        foreach (var input in overrides)
        {
            prepared.Add(PrepareOverride(organizationId, actorUserId, input));
        }

        // PostgreSQL gets a real transaction; the in-memory provider used by tests has no
        // transactions, and the earlier validation pass keeps those writes all-or-nothing.
        IDbContextTransaction? transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        try
        {
            var applied = new List<PlanEntitlementOverride>(prepared.Count);
            foreach (var entry in prepared)
            {
                await EnsureNoOverlapAsync(entry, cancellationToken);
                applied.Add(await repository.UpsertOverrideAsync(entry, cancellationToken));
            }

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
                    entry.EffectiveTo,
                    entry.Reason,
                }),
                Reason: BuildReason(applied)), cancellationToken);

            var resolved = await resolver.GetAllAsync(organizationId, at: null, cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

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
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }

    private static PlanEntitlementOverride PrepareOverride(
        Guid organizationId, Guid actorUserId, EntitlementOverrideInput input)
    {
        var key = ValidateKey(input.Key);
        var valueType = ParseValueType(input.ValueType);
        var canonicalType = CanonicalType(key);
        if (valueType != canonicalType)
        {
            throw new EntitlementOverrideValidationException(
                $"'{key}' is a {canonicalType} entitlement and cannot be overridden as {valueType}.");
        }

        var (number, flag, text) = ParseValue(key, valueType, input.Value);
        var reason = ValidateReason(input.Reason);
        var effectiveFrom = NormaliseEffectiveFrom(input.EffectiveFrom);
        var effectiveTo = NormaliseEffectiveTo(input.EffectiveTo);

        if (effectiveTo is not null && effectiveTo <= effectiveFrom)
        {
            throw new EntitlementOverrideValidationException(
                "'effectiveTo' must be after 'effectiveFrom'.");
        }

        return new PlanEntitlementOverride
        {
            OrganizationId = organizationId,
            Key = key,
            ValueType = valueType,
            ValueDecimal = number,
            ValueBool = flag,
            ValueText = text,
            EffectiveFrom = effectiveFrom,
            EffectiveTo = effectiveTo,
            Reason = reason,
            CreatedByUserId = actorUserId,
            CreatedAt = DateTime.UtcNow,
        };
    }

    /// <summary>
    /// Refuses a second window for the same key that overlaps an existing one. An upsert at
    /// the same <c>EffectiveFrom</c> is allowed (it is the documented update path); any other
    /// overlap must be retired first so the resolved value is never silently ambiguous
    /// (§3.8(c)).
    /// </summary>
    private async Task EnsureNoOverlapAsync(
        PlanEntitlementOverride entry, CancellationToken cancellationToken)
    {
        var overlaps = await db.PlanEntitlementOverrides
            .AsNoTracking()
            .Where(existing => existing.OrganizationId == entry.OrganizationId
                               && existing.Key == entry.Key
                               && existing.EffectiveFrom != entry.EffectiveFrom)
            .Where(existing => existing.EffectiveTo == null || existing.EffectiveTo > entry.EffectiveFrom)
            .Where(existing => entry.EffectiveTo == null || existing.EffectiveFrom < entry.EffectiveTo)
            .AnyAsync(cancellationToken);

        if (overlaps)
        {
            throw new EntitlementOverrideValidationException(
                $"An override for '{entry.Key}' already covers {entry.EffectiveFrom:O}. Retire it "
                + "by setting 'effectiveTo', or update it by using the same 'effectiveFrom'.");
        }
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

                var text = value.GetString();
                if (text is { Length: > MaxTextLength })
                {
                    throw new EntitlementOverrideValidationException(
                        $"The value of '{key}' must not exceed {MaxTextLength} characters.");
                }

                return (null, null, text);

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

    private static DateTime? NormaliseEffectiveTo(DateTime? effectiveTo)
    {
        if (effectiveTo is not { } value)
        {
            return null;
        }

        return value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
    }

    /// <summary>
    /// The catalog's canonical type for a key. Letting a caller declare a different type
    /// stored, for example, a boolean <c>api.access</c> as a decimal and made every reader's
    /// coercion the last line of defence (§3.8(d)).
    /// </summary>
    private static EntitlementValueType CanonicalType(string key)
    {
        if (!PlanEntitlementDefaults.For(PlanTier.Seed).TryGetValue(key, out var canonical))
        {
            throw new EntitlementOverrideValidationException(
                $"'{key}' is not a known entitlement key.");
        }

        return canonical.ValueType;
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
