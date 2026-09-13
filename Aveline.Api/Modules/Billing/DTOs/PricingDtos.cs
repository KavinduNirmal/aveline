using Aveline.Api.Modules.Billing.Domain;
using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Billing.Services;

namespace Aveline.Api.Modules.Billing.DTOs;

public sealed record PricingRuleDto(
    Guid Id,
    string ScopeKind,
    string? Provider,
    string? Model,
    int UnitsPerBlossom,
    decimal MinimumChargeBlossoms,
    string RoundingMode,
    int RoundingDecimals,
    DateTime EffectiveFrom,
    DateTime? EffectiveTo,
    string Status,
    int Version,
    string ChangeReason,
    Guid CreatedByUserId,
    Guid? ApprovedByUserId,
    DateTime CreatedAt,
    DateTime UpdatedAt)
{
    public static PricingRuleDto From(BlossomConversionRule rule) => new(
        rule.Id,
        rule.ScopeKind.ToString(),
        rule.Provider,
        rule.Model,
        rule.UnitsPerBlossom,
        rule.MinimumChargeBlossoms,
        rule.RoundingMode.ToString(),
        rule.RoundingDecimals,
        rule.EffectiveFrom,
        rule.EffectiveTo,
        rule.Status.ToString(),
        rule.Version,
        rule.ChangeReason,
        rule.CreatedByUserId,
        rule.ApprovedByUserId,
        rule.CreatedAt,
        rule.UpdatedAt);
}

public sealed record PricingRulePageDto(
    IReadOnlyList<PricingRuleDto> Items, int Total, int Page, int PageSize)
{
    public static PricingRulePageDto From(PricingRulePage page) => new(
        page.Items.Select(PricingRuleDto.From).ToArray(), page.Total, page.Page, page.PageSize);
}

public sealed record PricingPriceEntryDto(
    Guid Id,
    string? PlanTier,
    Guid? OrganizationId,
    string SkuKind,
    string? SkuCode,
    decimal BlossomQuantity,
    decimal PriceLkr,
    DateTime EffectiveFrom,
    DateTime? EffectiveTo,
    string Status,
    string ChangeReason,
    DateTime CreatedAt,
    DateTime UpdatedAt)
{
    public static PricingPriceEntryDto From(BlossomPriceEntry entry) => new(
        entry.Id,
        entry.PlanTier?.ToString(),
        entry.OrganizationId,
        entry.SkuKind.ToString(),
        entry.SkuCode,
        entry.BlossomQuantity,
        entry.PriceLkr,
        entry.EffectiveFrom,
        entry.EffectiveTo,
        entry.Status.ToString(),
        entry.ChangeReason,
        entry.CreatedAt,
        entry.UpdatedAt);
}

public sealed record CreatePricingRuleRequest(
    BlossomRuleScopeKind ScopeKind,
    string? Provider,
    string? Model,
    int UnitsPerBlossom,
    DateTime EffectiveFrom,
    string ChangeReason,
    decimal MinimumChargeBlossoms = 0.1m,
    BlossomRoundingMode RoundingMode = BlossomRoundingMode.Ceiling,
    int RoundingDecimals = 1,
    DateTime? EffectiveTo = null);

public sealed record UpdatePricingRuleRequest(
    int? UnitsPerBlossom,
    decimal? MinimumChargeBlossoms,
    BlossomRoundingMode? RoundingMode,
    int? RoundingDecimals,
    DateTime? EffectiveFrom,
    DateTime? EffectiveTo,
    string? ChangeReason);

public sealed record ActivatePricingRuleRequest(DateTime? EffectiveFrom);

public sealed record CancelPricingRuleRequest(string Reason);

public sealed record CreatePriceEntryRequest(
    PlanTier? PlanTier,
    Guid? OrganizationId,
    BlossomSkuKind SkuKind,
    string? SkuCode,
    decimal BlossomQuantity,
    decimal PriceLkr,
    DateTime EffectiveFrom,
    DateTime? EffectiveTo,
    string ChangeReason);

public sealed record UpdatePriceEntryRequest(
    decimal? BlossomQuantity,
    decimal? PriceLkr,
    DateTime? EffectiveFrom,
    DateTime? EffectiveTo,
    string? ChangeReason,
    BlossomRuleStatus? Status);
