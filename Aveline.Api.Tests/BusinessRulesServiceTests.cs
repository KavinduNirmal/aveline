using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Commerce.DTOs;
using Aveline.Api.Modules.Commerce.Models;
using Aveline.Api.Modules.Commerce.Repositories;
using Aveline.Api.Modules.Commerce.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aveline.Api.Tests;

public class BusinessRulesServiceTests
{
    private readonly AppDbContext _context;
    private readonly BusinessRulesRepository _repository;
    private readonly BusinessRulesService _service;
    private readonly Guid _orgId = Guid.NewGuid();

    public BusinessRulesServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"BusinessRules_{Guid.NewGuid()}")
            .Options;

        _context = new AppDbContext(options);
        _repository = new BusinessRulesRepository(_context);
        _service = new BusinessRulesService(_repository, NullLogger<BusinessRulesService>.Instance);
    }

    [Fact]
    public async Task EvaluateOrderRules_WhenOrderIsWithinAllDefaults_IsAutoApproved()
    {
        // Arrange (Order LKR 25,000, VIP 10% discount, 35% margin)
        var request = new EvaluateOrderRulesRequestDto(
            OrderTotal: 25000m,
            Margin: 0.35m,
            RequestedDiscount: 0.10m,
            CustomerTier: "VIP"
        );

        // Act
        var result = await _service.EvaluateOrderRulesAsync(_orgId, request);

        // Assert
        Assert.True(result.IsAutoApproved);
        Assert.False(result.RequiresApproval);
        Assert.Empty(result.Flags);
        Assert.Empty(result.TriggeredRules);
        Assert.Equal(0.10m, result.MaxAllowedDiscount);
        Assert.Equal(40000m, result.HighValueThreshold);
        Assert.Equal(0.25m, result.MinRequiredMargin);
    }

    [Fact]
    public async Task EvaluateOrderRules_WhenOrderTotalExceedsDefaultThreshold_RequiresApproval()
    {
        // Arrange (Order LKR 45,000 > 40,000 default threshold)
        var request = new EvaluateOrderRulesRequestDto(
            OrderTotal: 45000m,
            Margin: 0.35m,
            RequestedDiscount: 0.05m,
            CustomerTier: "Regular"
        );

        // Act
        var result = await _service.EvaluateOrderRulesAsync(_orgId, request);

        // Assert
        Assert.True(result.RequiresApproval);
        Assert.False(result.IsAutoApproved);
        Assert.Contains("HIGH_VALUE_THRESHOLD_EXCEEDED", result.TriggeredRules);
    }

    [Fact]
    public async Task EvaluateOrderRules_WhenRequestedDiscountExceedsVIPLimit_RequiresApproval()
    {
        // Arrange (VIP requested 20% discount > 10% default cap)
        var request = new EvaluateOrderRulesRequestDto(
            OrderTotal: 30000m,
            Margin: 0.30m,
            RequestedDiscount: 0.20m,
            CustomerTier: "VIP"
        );

        // Act
        var result = await _service.EvaluateOrderRulesAsync(_orgId, request);

        // Assert
        Assert.True(result.RequiresApproval);
        Assert.False(result.IsAutoApproved);
        Assert.Contains("DISCOUNT_LIMIT_EXCEEDED", result.TriggeredRules);
    }

    [Fact]
    public async Task EvaluateOrderRules_WhenMarginIsBelowMinimum_RequiresApproval()
    {
        // Arrange (Margin 18% < 25% default threshold)
        var request = new EvaluateOrderRulesRequestDto(
            OrderTotal: 20000m,
            Margin: 0.18m,
            RequestedDiscount: 0.00m,
            CustomerTier: "New"
        );

        // Act
        var result = await _service.EvaluateOrderRulesAsync(_orgId, request);

        // Assert
        Assert.True(result.RequiresApproval);
        Assert.False(result.IsAutoApproved);
        Assert.Contains("LOW_MARGIN_THRESHOLD", result.TriggeredRules);
    }

    [Fact]
    public async Task EvaluateOrderRules_WithCustomDynamicRuleFromDatabase_AppliesCustomThreshold()
    {
        // Arrange: Seed dynamic rule raising threshold to LKR 75,000
        await _repository.CreateAsync(new BusinessRule
        {
            Id = Guid.NewGuid(),
            OrganizationId = _orgId,
            RuleName = "HOLIDAY_HIGH_VALUE_LIMIT",
            RuleType = "approval_threshold",
            RuleValue = "{\"threshold\": 75000}",
            IsActive = true
        });

        var request = new EvaluateOrderRulesRequestDto(
            OrderTotal: 60000m,
            Margin: 0.35m,
            RequestedDiscount: 0.05m,
            CustomerTier: "Regular"
        );

        // Act
        var result = await _service.EvaluateOrderRulesAsync(_orgId, request);

        // Assert: 60,000 is under the new 75,000 limit -> Auto-approved!
        Assert.True(result.IsAutoApproved);
        Assert.False(result.RequiresApproval);
        Assert.Equal(75000m, result.HighValueThreshold);
    }

    [Fact]
    public async Task CreateRule_PersistsRule_AndReturnsResponseDto()
    {
        // Arrange
        var dto = new CreateBusinessRuleDto(
            RuleName: "CUSTOM_VIP_DISCOUNT",
            RuleType: "discount",
            RuleValue: "{\"vip_discount_cap\": 0.15}",
            Description: "Special 15% VIP discount rule"
        );

        // Act
        var created = await _service.CreateRuleAsync(_orgId, dto);

        // Assert
        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Equal("CUSTOM_VIP_DISCOUNT", created.RuleName);
        Assert.Equal("discount", created.RuleType);
        Assert.True(created.IsActive);

        var fetched = await _service.GetRuleByIdAsync(created.Id, _orgId);
        Assert.NotNull(fetched);
        Assert.Equal("CUSTOM_VIP_DISCOUNT", fetched.RuleName);
    }

    [Fact]
    public async Task DeleteRule_WhenExists_RemovesFromDatabase()
    {
        // Arrange
        var created = await _service.CreateRuleAsync(_orgId, new CreateBusinessRuleDto(
            RuleName: "TEMP_RULE",
            RuleType: "min_margin",
            RuleValue: "{\"min_margin\": 0.30}",
            Description: "Temporary margin rule"
        ));

        // Act
        var deleted = await _service.DeleteRuleAsync(created.Id, _orgId);

        // Assert
        Assert.True(deleted);
        var fetched = await _service.GetRuleByIdAsync(created.Id, _orgId);
        Assert.Null(fetched);
    }
}
