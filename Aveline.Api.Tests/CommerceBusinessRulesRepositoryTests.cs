using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Commerce.Models;
using Aveline.Api.Modules.Commerce.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Aveline.Api.Tests;

public class CommerceBusinessRulesRepositoryTests
{
    private readonly AppDbContext _context;
    private readonly BusinessRulesRepository _repository;
    private readonly Guid _orgId = Guid.NewGuid();

    public CommerceBusinessRulesRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"RepoTest_{Guid.NewGuid()}")
            .Options;

        _context = new AppDbContext(options);
        _repository = new BusinessRulesRepository(_context);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsRulesFilteredByOrganizationAndActiveFlag()
    {
        var otherOrg = Guid.NewGuid();
        await _repository.CreateAsync(new BusinessRule { OrganizationId = _orgId, RuleName = "R1", RuleType = "discount", IsActive = true });
        await _repository.CreateAsync(new BusinessRule { OrganizationId = _orgId, RuleName = "R2", RuleType = "discount", IsActive = false });
        await _repository.CreateAsync(new BusinessRule { OrganizationId = otherOrg, RuleName = "R3", RuleType = "discount", IsActive = true });

        var activeRules = await _repository.GetAllAsync(_orgId, activeOnly: true);
        var allRules = await _repository.GetAllAsync(_orgId, activeOnly: false);

        Assert.Single(activeRules);
        Assert.Equal("R1", activeRules.First().RuleName);
        Assert.Equal(2, allRules.Count);
    }

    [Fact]
    public async Task GetByIdAsync_WhenExists_ReturnsRule()
    {
        var rule = await _repository.CreateAsync(new BusinessRule
        {
            OrganizationId = _orgId,
            RuleName = "THRESHOLD_RULE",
            RuleType = "approval_threshold",
            RuleValue = "{}"
        });

        var found = await _repository.GetByIdAsync(rule.Id, _orgId);
        var notFound = await _repository.GetByIdAsync(rule.Id, Guid.NewGuid());

        Assert.NotNull(found);
        Assert.Equal(rule.Id, found.Id);
        Assert.Null(notFound);
    }

    [Fact]
    public async Task GetByTypeAsync_ReturnsActiveRuleMatchingType()
    {
        await _repository.CreateAsync(new BusinessRule
        {
            OrganizationId = _orgId,
            RuleName = "MARGIN_RULE",
            RuleType = "min_margin",
            RuleValue = "{\"min\": 0.2}",
            IsActive = true
        });

        var match = await _repository.GetByTypeAsync("min_margin", _orgId);
        var noMatch = await _repository.GetByTypeAsync("unknown_type", _orgId);

        Assert.NotNull(match);
        Assert.Equal("min_margin", match.RuleType);
        Assert.Null(noMatch);
    }

    [Fact]
    public async Task GetByTypesAsync_ReturnsActiveRulesMatchingTypes()
    {
        await _repository.CreateAsync(new BusinessRule
        {
            OrganizationId = _orgId,
            RuleName = "RULE_A",
            RuleType = "approval_threshold",
            IsActive = true
        });
        await _repository.CreateAsync(new BusinessRule
        {
            OrganizationId = _orgId,
            RuleName = "RULE_B",
            RuleType = "min_margin",
            IsActive = true
        });

        var results = await _repository.GetByTypesAsync(new[] { "approval_threshold", "min_margin" }, _orgId);

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task UpdateAsync_UpdatesPropertiesAndSetsUpdatedAt()
    {
        var rule = await _repository.CreateAsync(new BusinessRule
        {
            OrganizationId = _orgId,
            RuleName = "ORIGINAL_NAME",
            RuleType = "discount",
            RuleValue = "{}"
        });

        rule.RuleName = "UPDATED_NAME";
        rule.IsActive = false;

        var updated = await _repository.UpdateAsync(rule);

        Assert.Equal("UPDATED_NAME", updated.RuleName);
        Assert.False(updated.IsActive);
        Assert.NotNull(updated.UpdatedAt);
    }

    [Fact]
    public async Task DeleteAsync_WhenNotExists_ReturnsFalse()
    {
        var deleted = await _repository.DeleteAsync(Guid.NewGuid(), _orgId);
        Assert.False(deleted);
    }
}
