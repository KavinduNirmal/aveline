using Aveline.Api.Modules.Billing.Domain;
using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Billing.Repositories;

namespace Aveline.Api.Modules.Billing.Services;

public sealed record CreatePricingRuleCommand(
    BlossomRuleScopeKind ScopeKind,
    string? Provider,
    string? Model,
    int UnitsPerBlossom,
    decimal MinimumChargeBlossoms,
    BlossomRoundingMode RoundingMode,
    int RoundingDecimals,
    DateTime EffectiveFrom,
    DateTime? EffectiveTo,
    string ChangeReason,
    Guid CreatedByUserId,
    bool AllowBackdate = false);

public sealed record UpdatePricingRuleCommand(
    int? UnitsPerBlossom,
    decimal? MinimumChargeBlossoms,
    BlossomRoundingMode? RoundingMode,
    int? RoundingDecimals,
    DateTime? EffectiveFrom,
    DateTime? EffectiveTo,
    string? ChangeReason,
    Guid ActorUserId);

public sealed record PricingRulePage(
    IReadOnlyList<BlossomConversionRule> Items, int Total, int Page, int PageSize);

public sealed record CreatePriceEntryCommand(
    PlanTier? PlanTier,
    Guid? OrganizationId,
    BlossomSkuKind SkuKind,
    string? SkuCode,
    decimal BlossomQuantity,
    decimal PriceLkr,
    DateTime EffectiveFrom,
    DateTime? EffectiveTo,
    string ChangeReason,
    Guid CreatedByUserId);

public sealed record UpdatePriceEntryCommand(
    decimal? BlossomQuantity,
    decimal? PriceLkr,
    DateTime? EffectiveFrom,
    DateTime? EffectiveTo,
    string? ChangeReason,
    BlossomRuleStatus? Status,
    Guid ActorUserId);

/// <summary>Rule resolution and the administrative pricing lifecycle.</summary>
public interface IPricingService
{
    /// <summary>
    /// The rule that prices a (provider, model) workflow at <paramref name="at"/>,
    /// falling back to the legacy defaults when no rule matches (BR-1.9).
    /// </summary>
    Task<PricingResolution> ResolveAsync(
        string? provider, string? model, DateTime at, CancellationToken cancellationToken = default);

    Task<BlossomConversionRule> CreateRuleAsync(
        CreatePricingRuleCommand command, CancellationToken cancellationToken = default);

    Task<BlossomConversionRule> UpdateRuleAsync(
        Guid ruleId, UpdatePricingRuleCommand command, CancellationToken cancellationToken = default);

    Task<BlossomConversionRule> ActivateRuleAsync(
        Guid ruleId, DateTime? effectiveFrom = null, CancellationToken cancellationToken = default);

    Task<BlossomConversionRule> CancelRuleAsync(
        Guid ruleId, string reason, CancellationToken cancellationToken = default);

    Task<BlossomConversionRule?> GetRuleAsync(Guid ruleId, CancellationToken cancellationToken = default);

    Task<PricingRulePage> ListRulesAsync(
        PricingRuleFilter filter, int page, int pageSize, CancellationToken cancellationToken = default);

    Task<BlossomPriceEntry> CreatePriceEntryAsync(
        CreatePriceEntryCommand command, CancellationToken cancellationToken = default);

    Task<BlossomPriceEntry> UpdatePriceEntryAsync(
        Guid entryId, UpdatePriceEntryCommand command, CancellationToken cancellationToken = default);

    Task<BlossomPriceEntry?> GetPriceEntryAsync(Guid entryId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BlossomPriceEntry>> ListPriceEntriesAsync(
        BlossomSkuKind? skuKind,
        PlanTier? planTier,
        Guid? organizationId,
        CancellationToken cancellationToken = default);
}
