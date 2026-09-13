using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Billing.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Aveline.Api.Modules.Billing.Repositories;

/// <summary>EF Core implementation of <see cref="IPricingRepository"/>.</summary>
public sealed class PricingRepository(AppDbContext db) : IPricingRepository
{
    public async Task<IPricingTransaction> BeginTransactionAsync(
        CancellationToken cancellationToken = default)
    {
        if (!db.Database.IsRelational())
        {
            return NoOpPricingTransaction.Instance;
        }

        var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        return new EfPricingTransaction(transaction);
    }

    public Task<BlossomConversionRule?> ResolveActiveRuleAsync(
        string? provider, string? model, DateTime at, CancellationToken cancellationToken = default)
    {
        return db.BlossomConversionRules
            .Where(rule => rule.Status == BlossomRuleStatus.Active)
            .Where(rule => rule.EffectiveFrom <= at)
            .Where(rule => rule.EffectiveTo == null || rule.EffectiveTo > at)
            .Where(rule =>
                rule.ScopeKind == BlossomRuleScopeKind.Global
                || (rule.ScopeKind == BlossomRuleScopeKind.Provider
                    && provider != null && rule.Provider == provider)
                || (rule.ScopeKind == BlossomRuleScopeKind.ProviderModel
                    && provider != null && rule.Provider == provider
                    && model != null && rule.Model == model))
            // ProviderModel (3) > Provider (2) > Global (1), then newest window.
            .OrderByDescending(rule =>
                rule.ScopeKind == BlossomRuleScopeKind.ProviderModel
                    ? 3
                    : rule.ScopeKind == BlossomRuleScopeKind.Provider ? 2 : 1)
            .ThenByDescending(rule => rule.EffectiveFrom)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<BlossomConversionRule?> GetRuleAsync(Guid ruleId, CancellationToken cancellationToken = default) =>
        db.BlossomConversionRules.FirstOrDefaultAsync(rule => rule.Id == ruleId, cancellationToken);

    public async Task<IReadOnlyList<BlossomConversionRule>> ListRulesAsync(
        PricingRuleFilter filter, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        return await ApplyFilter(filter)
            .OrderByDescending(rule => rule.EffectiveFrom)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
    }

    public Task<int> CountRulesAsync(PricingRuleFilter filter, CancellationToken cancellationToken = default) =>
        ApplyFilter(filter).CountAsync(cancellationToken);

    public async Task<int> GetNextVersionAsync(
        BlossomRuleScopeKind scopeKind, string? provider, string? model, CancellationToken cancellationToken = default)
    {
        var current = await db.BlossomConversionRules
            .Where(rule => rule.ScopeKind == scopeKind && rule.Provider == provider && rule.Model == model)
            .Select(rule => (int?)rule.Version)
            .MaxAsync(cancellationToken);

        return (current ?? 0) + 1;
    }

    public Task<BlossomConversionRule?> FindPredecessorAsync(
        BlossomConversionRule candidate, CancellationToken cancellationToken = default)
    {
        var candidateEnd = candidate.EffectiveTo ?? DateTime.MaxValue;

        return db.BlossomConversionRules
            .Where(rule => rule.Id != candidate.Id)
            .Where(rule => rule.Status == BlossomRuleStatus.Active)
            .Where(rule => rule.ScopeKind == candidate.ScopeKind
                && rule.Provider == candidate.Provider
                && rule.Model == candidate.Model)
            .Where(rule => rule.EffectiveFrom <= candidate.EffectiveFrom)
            .Where(rule => rule.EffectiveTo == null || rule.EffectiveTo > candidate.EffectiveFrom)
            .Where(rule => rule.EffectiveFrom < candidateEnd)
            .OrderByDescending(rule => rule.EffectiveFrom)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task AddRuleAsync(BlossomConversionRule rule, CancellationToken cancellationToken = default)
    {
        db.BlossomConversionRules.Add(rule);
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task<bool> HasPricedUsageAsync(Guid ruleId, CancellationToken cancellationToken = default) =>
        db.AiUsageRecords.AnyAsync(record => record.PricingRuleId == ruleId, cancellationToken);

    public async Task UpdateRuleAsync(BlossomConversionRule rule, CancellationToken cancellationToken = default)
    {
        db.BlossomConversionRules.Update(rule);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<BlossomPriceEntry> AddPriceEntryAsync(
        BlossomPriceEntry entry, CancellationToken cancellationToken = default)
    {
        db.BlossomPriceEntries.Add(entry);
        await db.SaveChangesAsync(cancellationToken);
        return entry;
    }

    public Task<BlossomPriceEntry?> GetPriceEntryAsync(
        Guid entryId, Guid? organizationId, CancellationToken cancellationToken = default) =>
        db.BlossomPriceEntries
            .Where(entry => entry.Id == entryId)
            // Defence in depth (H-4): a global entry is readable; a per-organization
            // override only within its own organization.
            .Where(entry => entry.OrganizationId == null || entry.OrganizationId == organizationId)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<BlossomPriceEntry?> FindPriceEntryAsync(
        Guid entryId, CancellationToken cancellationToken = default) =>
        db.BlossomPriceEntries.FirstOrDefaultAsync(entry => entry.Id == entryId, cancellationToken);

    public async Task UpdatePriceEntryAsync(
        BlossomPriceEntry entry, CancellationToken cancellationToken = default)
    {
        db.BlossomPriceEntries.Update(entry);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<BlossomPriceEntry>> ListPriceEntriesAsync(
        BlossomSkuKind? skuKind,
        PlanTier? planTier,
        Guid? organizationId,
        CancellationToken cancellationToken = default)
    {
        var query = db.BlossomPriceEntries.AsQueryable();

        if (skuKind is not null)
        {
            query = query.Where(entry => entry.SkuKind == skuKind);
        }

        if (planTier is not null)
        {
            query = query.Where(entry => entry.PlanTier == planTier);
        }

        if (organizationId is not null)
        {
            query = query.Where(entry => entry.OrganizationId == organizationId);
        }

        return await query
            .OrderByDescending(entry => entry.EffectiveFrom)
            .ToListAsync(cancellationToken);
    }

    private IQueryable<BlossomConversionRule> ApplyFilter(PricingRuleFilter filter)
    {
        var query = db.BlossomConversionRules.AsQueryable();

        if (filter.ScopeKind is not null)
        {
            query = query.Where(rule => rule.ScopeKind == filter.ScopeKind);
        }

        if (filter.Provider is not null)
        {
            query = query.Where(rule => rule.Provider == filter.Provider);
        }

        if (filter.Model is not null)
        {
            query = query.Where(rule => rule.Model == filter.Model);
        }

        if (filter.Status is not null)
        {
            query = query.Where(rule => rule.Status == filter.Status);
        }

        if (filter.ActiveAt is { } at)
        {
            query = query
                .Where(rule => rule.Status == BlossomRuleStatus.Active)
                .Where(rule => rule.EffectiveFrom <= at)
                .Where(rule => rule.EffectiveTo == null || rule.EffectiveTo > at);
        }

        return query;
    }

    private sealed class NoOpPricingTransaction : IPricingTransaction
    {
        public static readonly NoOpPricingTransaction Instance = new();

        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class EfPricingTransaction(IDbContextTransaction transaction) : IPricingTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken = default) =>
            transaction.CommitAsync(cancellationToken);

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
