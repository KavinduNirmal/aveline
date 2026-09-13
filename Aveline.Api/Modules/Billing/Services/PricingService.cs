using Aveline.Api.Infrastructure.Eventing;
using Aveline.Api.Modules.Audit.Models;
using Aveline.Api.Modules.Audit.Services;
using Aveline.Api.Modules.Billing.Domain;
using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Billing.Repositories;

namespace Aveline.Api.Modules.Billing.Services;

/// <summary>
/// Default <see cref="IPricingService"/>. Resolution reads through a short-TTL L1 cache
/// (FR-1.10); every mutation invalidates that cache and writes an audit entry (FR-1.3).
/// </summary>
public sealed class PricingService(
    IPricingRepository repository,
    PricingRuleCache cache,
    IAuditService auditService,
    ILogger<PricingService> logger,
    IEventBus? eventBus = null) : IPricingService
{
    private const int MinChangeReasonLength = 10;
    private const int MaxChangeReasonLength = 500;
    private const int MaxUnitsPerBlossom = 10_000_000;

    public async Task<PricingResolution> ResolveAsync(
        string? provider, string? model, DateTime at, CancellationToken cancellationToken = default)
    {
        var scopeKey = $"{provider}|{model}";

        if (cache.TryGet(scopeKey, at, out var cached) && cached is not null)
        {
            return new PricingResolution(cached, cached.RuleId is null);
        }

        var rule = await repository.ResolveActiveRuleAsync(provider, model, at, cancellationToken);
        if (rule is null)
        {
            logger.LogWarning(
                "pricing.rule.missing provider={Provider} model={Model} at={At}; using fallback defaults.",
                provider, model, at);
            cache.Set(scopeKey, at, PricingRuleSnapshot.Fallback);
            return new PricingResolution(PricingRuleSnapshot.Fallback, IsFallback: true);
        }

        var snapshot = new PricingRuleSnapshot(
            rule.Id, rule.Version, rule.UnitsPerBlossom,
            rule.MinimumChargeBlossoms, rule.RoundingMode, rule.RoundingDecimals);

        cache.Set(scopeKey, at, snapshot);
        return new PricingResolution(snapshot, IsFallback: false);
    }

    public async Task<BlossomConversionRule> CreateRuleAsync(
        CreatePricingRuleCommand command, CancellationToken cancellationToken = default)
    {
        ValidateScope(command.ScopeKind, command.Provider, command.Model);
        ValidateValues(
            command.UnitsPerBlossom, command.MinimumChargeBlossoms,
            command.RoundingMode, command.RoundingDecimals, command.ChangeReason);
        ValidateWindow(command.EffectiveFrom, command.EffectiveTo);

        if (command.EffectiveFrom < DateTime.UtcNow && !command.AllowBackdate)
        {
            throw new PricingBackdateForbiddenException(
                "A past EffectiveFrom requires the pricing:backdate permission.");
        }

        var rule = new BlossomConversionRule
        {
            ScopeKind = command.ScopeKind,
            Provider = command.Provider,
            Model = command.Model,
            UnitsPerBlossom = command.UnitsPerBlossom,
            MinimumChargeBlossoms = command.MinimumChargeBlossoms,
            RoundingMode = command.RoundingMode,
            RoundingDecimals = (short)command.RoundingDecimals,
            EffectiveFrom = command.EffectiveFrom,
            EffectiveTo = command.EffectiveTo,
            Status = BlossomRuleStatus.Draft,
            Version = await repository.GetNextVersionAsync(
                command.ScopeKind, command.Provider, command.Model, cancellationToken),
            ChangeReason = command.ChangeReason,
            CreatedByUserId = command.CreatedByUserId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        await repository.AddRuleAsync(rule, cancellationToken);
        cache.Invalidate();
        await RecordAuditAsync(AuditAction.PricingRuleCreated, rule, command.CreatedByUserId, cancellationToken);
        return rule;
    }

    public async Task<BlossomConversionRule> UpdateRuleAsync(
        Guid ruleId, UpdatePricingRuleCommand command, CancellationToken cancellationToken = default)
    {
        var rule = await GetOrThrowAsync(ruleId, cancellationToken);

        if (rule.Status != BlossomRuleStatus.Draft)
        {
            throw new PricingRuleImmutableException(
                "Only a Draft rule may be edited; supersede it with a new rule instead.");
        }

        var units = command.UnitsPerBlossom ?? rule.UnitsPerBlossom;
        var minimum = command.MinimumChargeBlossoms ?? rule.MinimumChargeBlossoms;
        var roundingMode = command.RoundingMode ?? rule.RoundingMode;
        var roundingDecimals = command.RoundingDecimals ?? rule.RoundingDecimals;
        var reason = command.ChangeReason ?? rule.ChangeReason;
        var effectiveFrom = command.EffectiveFrom ?? rule.EffectiveFrom;
        var effectiveTo = command.EffectiveTo ?? rule.EffectiveTo;

        ValidateValues(units, minimum, roundingMode, roundingDecimals, reason);
        ValidateWindow(effectiveFrom, effectiveTo);

        rule.UnitsPerBlossom = units;
        rule.MinimumChargeBlossoms = minimum;
        rule.RoundingMode = roundingMode;
        rule.RoundingDecimals = (short)roundingDecimals;
        rule.ChangeReason = reason;
        rule.EffectiveFrom = effectiveFrom;
        rule.EffectiveTo = effectiveTo;
        rule.UpdatedAt = DateTime.UtcNow;

        await repository.UpdateRuleAsync(rule, cancellationToken);
        cache.Invalidate();
        await RecordAuditAsync(AuditAction.PricingRuleUpdated, rule, command.ActorUserId, cancellationToken);
        return rule;
    }

    public async Task<BlossomConversionRule> ActivateRuleAsync(
        Guid ruleId, DateTime? effectiveFrom = null, CancellationToken cancellationToken = default)
    {
        var rule = await GetOrThrowAsync(ruleId, cancellationToken);

        if (rule.Status != BlossomRuleStatus.Draft)
        {
            throw new PricingRuleImmutableException("Only a Draft rule can be activated.");
        }

        rule.EffectiveFrom = effectiveFrom ?? rule.EffectiveFrom;
        ValidateWindow(rule.EffectiveFrom, rule.EffectiveTo);

        // The predecessor trim and the successor activation must land together (BR-1.8).
        // Disposing the scope without committing rolls back the trim when the successor
        // write fails, so the scope is never left with no Active rule.
        await using var transaction = await repository.BeginTransactionAsync(cancellationToken);

        // Trim and supersede the predecessor first: the exclusion constraint only covers
        // Draft and Active rows, so this order keeps both writes legal.
        var predecessor = await repository.FindPredecessorAsync(rule, cancellationToken);
        if (predecessor is not null)
        {
            predecessor.EffectiveTo = rule.EffectiveFrom;
            predecessor.Status = BlossomRuleStatus.Superseded;
            predecessor.UpdatedAt = DateTime.UtcNow;
            await repository.UpdateRuleAsync(predecessor, cancellationToken);
        }

        rule.Status = BlossomRuleStatus.Active;
        rule.UpdatedAt = DateTime.UtcNow;
        await repository.UpdateRuleAsync(rule, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        cache.Invalidate();
        await RecordAuditAsync(AuditAction.PricingRuleActivated, rule, rule.CreatedByUserId, cancellationToken);
        await PublishRuleActivatedAsync(rule, cancellationToken);
        return rule;
    }

    public async Task<BlossomConversionRule> CancelRuleAsync(
        Guid ruleId, string reason, CancellationToken cancellationToken = default)
    {
        var rule = await GetOrThrowAsync(ruleId, cancellationToken);
        ValidateChangeReason(reason);

        rule.Status = BlossomRuleStatus.Cancelled;
        rule.ChangeReason = reason;
        rule.UpdatedAt = DateTime.UtcNow;

        await repository.UpdateRuleAsync(rule, cancellationToken);
        cache.Invalidate();
        await RecordAuditAsync(AuditAction.PricingRuleCancelled, rule, rule.CreatedByUserId, cancellationToken);
        await PublishRuleCancelledAsync(rule, reason, cancellationToken);
        return rule;
    }

    public Task<BlossomConversionRule?> GetRuleAsync(Guid ruleId, CancellationToken cancellationToken = default) =>
        repository.GetRuleAsync(ruleId, cancellationToken);

    public async Task<PricingRulePage> ListRulesAsync(
        PricingRuleFilter filter, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var safePage = Math.Max(page, 1);
        var safePageSize = Math.Clamp(pageSize, 1, 200);

        var items = await repository.ListRulesAsync(filter, safePage, safePageSize, cancellationToken);
        var total = await repository.CountRulesAsync(filter, cancellationToken);

        return new PricingRulePage(items, total, safePage, safePageSize);
    }

    public async Task<BlossomPriceEntry> CreatePriceEntryAsync(
        CreatePriceEntryCommand command, CancellationToken cancellationToken = default)
    {
        ValidateChangeReason(command.ChangeReason);

        if (command.BlossomQuantity <= 0)
        {
            throw new PricingValidationException("BlossomQuantity must be greater than zero.");
        }

        if (command.PriceLkr < 0)
        {
            throw new PricingValidationException("PriceLkr must not be negative.");
        }

        ValidateWindow(command.EffectiveFrom, command.EffectiveTo);

        var entry = new BlossomPriceEntry
        {
            PlanTier = command.PlanTier,
            OrganizationId = command.OrganizationId,
            SkuKind = command.SkuKind,
            SkuCode = command.SkuCode,
            BlossomQuantity = command.BlossomQuantity,
            PriceLkr = command.PriceLkr,
            EffectiveFrom = command.EffectiveFrom,
            EffectiveTo = command.EffectiveTo,
            Status = BlossomRuleStatus.Draft,
            ChangeReason = command.ChangeReason,
            CreatedByUserId = command.CreatedByUserId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        await repository.AddPriceEntryAsync(entry, cancellationToken);
        await auditService.RecordAsync(new AuditEntryRequest(
            Action: AuditAction.PricingPriceEntryCreated,
            EntityType: nameof(BlossomPriceEntry),
            EntityId: entry.Id.ToString(),
            ActorUserId: command.CreatedByUserId,
            After: entry,
            Reason: command.ChangeReason), cancellationToken);

        return entry;
    }

    public async Task<BlossomPriceEntry> UpdatePriceEntryAsync(
        Guid entryId, UpdatePriceEntryCommand command, CancellationToken cancellationToken = default)
    {
        var entry = await repository.FindPriceEntryAsync(entryId, cancellationToken)
            ?? throw new PricingPriceEntryNotFoundException(entryId);

        var quantity = command.BlossomQuantity ?? entry.BlossomQuantity;
        var price = command.PriceLkr ?? entry.PriceLkr;
        var reason = command.ChangeReason ?? entry.ChangeReason;

        if (quantity <= 0)
        {
            throw new PricingValidationException("BlossomQuantity must be greater than zero.");
        }

        if (price < 0)
        {
            throw new PricingValidationException("PriceLkr must not be negative.");
        }

        ValidateChangeReason(reason);
        ValidateWindow(command.EffectiveFrom ?? entry.EffectiveFrom, command.EffectiveTo ?? entry.EffectiveTo);

        entry.BlossomQuantity = quantity;
        entry.PriceLkr = price;
        entry.ChangeReason = reason;
        entry.EffectiveFrom = command.EffectiveFrom ?? entry.EffectiveFrom;
        entry.EffectiveTo = command.EffectiveTo ?? entry.EffectiveTo;
        entry.Status = command.Status ?? entry.Status;
        entry.UpdatedAt = DateTime.UtcNow;

        await repository.UpdatePriceEntryAsync(entry, cancellationToken);
        await auditService.RecordAsync(new AuditEntryRequest(
            Action: AuditAction.PricingPriceEntryUpdated,
            EntityType: nameof(BlossomPriceEntry),
            EntityId: entry.Id.ToString(),
            ActorUserId: command.ActorUserId,
            After: entry,
            Reason: reason), cancellationToken);

        return entry;
    }

    public Task<BlossomPriceEntry?> GetPriceEntryAsync(
        Guid entryId, Guid? organizationId, CancellationToken cancellationToken = default) =>
        repository.GetPriceEntryAsync(entryId, organizationId, cancellationToken);

    public Task<IReadOnlyList<BlossomPriceEntry>> ListPriceEntriesAsync(
        BlossomSkuKind? skuKind,
        PlanTier? planTier,
        Guid? organizationId,
        CancellationToken cancellationToken = default) =>
        repository.ListPriceEntriesAsync(skuKind, planTier, organizationId, cancellationToken);

    private async Task<BlossomConversionRule> GetOrThrowAsync(Guid ruleId, CancellationToken cancellationToken)
    {
        return await repository.GetRuleAsync(ruleId, cancellationToken)
            ?? throw new PricingRuleNotFoundException(ruleId);
    }

    private Task PublishRuleActivatedAsync(
        BlossomConversionRule rule, CancellationToken cancellationToken) =>
        PublishAsync(
            "pricing.rule.activated",
            new
            {
                ruleId = rule.Id,
                scopeKind = rule.ScopeKind.ToString(),
                provider = rule.Provider,
                model = rule.Model,
                unitsPerBlossom = rule.UnitsPerBlossom,
                effectiveFrom = rule.EffectiveFrom,
                actorUserId = rule.CreatedByUserId,
            },
            cancellationToken);

    private Task PublishRuleCancelledAsync(
        BlossomConversionRule rule, string reason, CancellationToken cancellationToken) =>
        PublishAsync(
            "pricing.rule.cancelled",
            new { ruleId = rule.Id, actorUserId = rule.CreatedByUserId, reason },
            cancellationToken);

    private Task PublishAsync(string eventType, object payload, CancellationToken cancellationToken) =>
        eventBus is null
            ? Task.CompletedTask
            : eventBus.PublishAsync(
                eventType, organizationId: null, payload, cancellationToken: cancellationToken);

    private Task RecordAuditAsync(
        string action, BlossomConversionRule rule, Guid actorUserId, CancellationToken cancellationToken) =>
        auditService.RecordAsync(new AuditEntryRequest(
            Action: action,
            EntityType: nameof(BlossomConversionRule),
            EntityId: rule.Id.ToString(),
            ActorUserId: actorUserId == Guid.Empty ? null : actorUserId,
            After: rule,
            Reason: rule.ChangeReason), cancellationToken);

    private static void ValidateScope(BlossomRuleScopeKind scopeKind, string? provider, string? model)
    {
        var valid = scopeKind switch
        {
            BlossomRuleScopeKind.Global => provider is null && model is null,
            BlossomRuleScopeKind.Provider => !string.IsNullOrWhiteSpace(provider) && model is null,
            BlossomRuleScopeKind.ProviderModel =>
                !string.IsNullOrWhiteSpace(provider) && !string.IsNullOrWhiteSpace(model),
            _ => false,
        };

        if (!valid)
        {
            throw new PricingValidationException(
                "ScopeKind is inconsistent with the supplied Provider and Model.");
        }
    }

    private static void ValidateValues(
        int unitsPerBlossom,
        decimal minimumChargeBlossoms,
        BlossomRoundingMode roundingMode,
        int roundingDecimals,
        string changeReason)
    {
        if (unitsPerBlossom <= 0 || unitsPerBlossom > MaxUnitsPerBlossom)
        {
            throw new PricingValidationException(
                $"UnitsPerBlossom must be between 1 and {MaxUnitsPerBlossom}.");
        }

        if (minimumChargeBlossoms < 0)
        {
            throw new PricingValidationException("MinimumChargeBlossoms must not be negative.");
        }

        if (roundingDecimals is < 0 or > BlossomCalculator.MaxRoundingDecimals)
        {
            throw new PricingValidationException(
                $"RoundingDecimals must be between 0 and {BlossomCalculator.MaxRoundingDecimals}.");
        }

        if (roundingMode == BlossomRoundingMode.HalfUp && roundingDecimals < 1)
        {
            throw new PricingValidationException("HalfUp rounding requires at least one decimal place.");
        }

        ValidateChangeReason(changeReason);
    }

    private static void ValidateChangeReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)
            || reason.Length < MinChangeReasonLength
            || reason.Length > MaxChangeReasonLength)
        {
            throw new PricingValidationException(
                $"ChangeReason must be between {MinChangeReasonLength} and {MaxChangeReasonLength} characters.");
        }
    }

    private static void ValidateWindow(DateTime effectiveFrom, DateTime? effectiveTo)
    {
        if (effectiveFrom == default)
        {
            throw new PricingValidationException("EffectiveFrom is required.");
        }

        if (effectiveTo is not null && effectiveTo <= effectiveFrom)
        {
            throw new PricingValidationException("EffectiveTo must be after EffectiveFrom.");
        }
    }
}
