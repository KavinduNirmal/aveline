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

    /// <summary>
    /// Reads a price entry scoped to the caller's organization context: a global entry
    /// (<see cref="BlossomPriceEntry.OrganizationId"/> <c>null</c>) is always readable, a
    /// per-organization override is readable only when it belongs to
    /// <paramref name="organizationId"/> (H-4 defence in depth).
    /// </summary>
    Task<BlossomPriceEntry?> GetPriceEntryAsync(
        Guid entryId, Guid? organizationId, CancellationToken cancellationToken = default);

    /// <summary>Unscoped lookup used by the authenticated write path to locate any entry.</summary>
    Task<BlossomPriceEntry?> FindPriceEntryAsync(Guid entryId, CancellationToken cancellationToken = default);

    Task UpdatePriceEntryAsync(BlossomPriceEntry entry, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BlossomPriceEntry>> ListPriceEntriesAsync(
        BlossomSkuKind? skuKind,
        PlanTier? planTier,
        Guid? organizationId,
        CancellationToken cancellationToken = default);
}
