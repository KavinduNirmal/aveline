using Aveline.Api.Modules.Billing.Models;

namespace Aveline.Api.Modules.Billing.Repositories;

/// <summary>Filters for the admin rule listing.</summary>
public sealed record PricingRuleFilter(
    BlossomRuleScopeKind? ScopeKind = null,
    string? Provider = null,
    string? Model = null,
    BlossomRuleStatus? Status = null,
    DateTime? ActiveAt = null);

/// <summary>Persistence for conversion rules and the commercial price book.</summary>
public interface IPricingRepository
{
    /// <summary>Resolves the most specific active rule for (provider, model) at a timestamp (BR-1.9).</summary>
    Task<BlossomConversionRule?> ResolveActiveRuleAsync(
        string? provider, string? model, DateTime at, CancellationToken cancellationToken = default);

    Task<BlossomConversionRule?> GetRuleAsync(Guid ruleId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BlossomConversionRule>> ListRulesAsync(
        PricingRuleFilter filter, int page, int pageSize, CancellationToken cancellationToken = default);

    Task<int> CountRulesAsync(PricingRuleFilter filter, CancellationToken cancellationToken = default);

    /// <summary>Returns the next monotonic version for the rule's scope.</summary>
    Task<int> GetNextVersionAsync(
        BlossomRuleScopeKind scopeKind, string? provider, string? model, CancellationToken cancellationToken = default);

    /// <summary>The active rule immediately preceding the candidate's window, if any (BR-1.8).</summary>
    Task<BlossomConversionRule?> FindPredecessorAsync(
        BlossomConversionRule candidate, CancellationToken cancellationToken = default);

    Task AddRuleAsync(BlossomConversionRule rule, CancellationToken cancellationToken = default);

    Task UpdateRuleAsync(BlossomConversionRule rule, CancellationToken cancellationToken = default);

    Task<BlossomPriceEntry> AddPriceEntryAsync(
        BlossomPriceEntry entry, CancellationToken cancellationToken = default);

    Task<BlossomPriceEntry?> GetPriceEntryAsync(Guid entryId, CancellationToken cancellationToken = default);

    Task UpdatePriceEntryAsync(BlossomPriceEntry entry, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BlossomPriceEntry>> ListPriceEntriesAsync(
        BlossomSkuKind? skuKind,
        PlanTier? planTier,
        Guid? organizationId,
        CancellationToken cancellationToken = default);
}
