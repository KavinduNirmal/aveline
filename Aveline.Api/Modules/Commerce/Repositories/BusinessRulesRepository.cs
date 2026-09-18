using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Commerce.Models;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.Commerce.Repositories;

public class BusinessRulesRepository : IBusinessRulesRepository
{
    private readonly AppDbContext _context;

    public BusinessRulesRepository(AppDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<IReadOnlyList<BusinessRule>> GetAllAsync(
        Guid organizationId,
        bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        var query = _context.BusinessRules
            .Where(r => r.OrganizationId == organizationId);

        if (activeOnly)
        {
            query = query.Where(r => r.IsActive);
        }

        return await query
            .AsNoTracking()
            .OrderBy(r => r.RuleType)
            .ThenBy(r => r.RuleName)
            .ToListAsync(cancellationToken);
    }

    public async Task<BusinessRule?> GetByIdAsync(
        Guid id,
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        return await _context.BusinessRules
            .FirstOrDefaultAsync(r => r.Id == id && r.OrganizationId == organizationId, cancellationToken);
    }

    public async Task<BusinessRule?> GetByTypeAsync(
        string ruleType,
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        return await _context.BusinessRules
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.OrganizationId == organizationId && r.RuleType == ruleType && r.IsActive, cancellationToken);
    }

    public async Task<IReadOnlyList<BusinessRule>> GetByTypesAsync(
        IEnumerable<string> ruleTypes,
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        var typeList = ruleTypes.ToList();
        return await _context.BusinessRules
            .AsNoTracking()
            .Where(r => r.OrganizationId == organizationId && typeList.Contains(r.RuleType) && r.IsActive)
            .ToListAsync(cancellationToken);
    }

    public async Task<BusinessRule> CreateAsync(
        BusinessRule rule,
        CancellationToken cancellationToken = default)
    {
        rule.CreatedAt = DateTime.UtcNow;
        rule.UpdatedAt = null;

        await _context.BusinessRules.AddAsync(rule, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        return rule;
    }

    public async Task<BusinessRule> UpdateAsync(
        BusinessRule rule,
        CancellationToken cancellationToken = default)
    {
        rule.UpdatedAt = DateTime.UtcNow;

        _context.BusinessRules.Update(rule);
        await _context.SaveChangesAsync(cancellationToken);

        return rule;
    }

    public async Task<bool> DeleteAsync(
        Guid id,
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        var rule = await GetByIdAsync(id, organizationId, cancellationToken);
        if (rule is null)
        {
            return false;
        }

        _context.BusinessRules.Remove(rule);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }
}
