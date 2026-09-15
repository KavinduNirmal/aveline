using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Audit.Repositories;
using Aveline.Api.Modules.Audit.Services;
using Aveline.Api.Modules.Billing.Domain;
using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Billing.Repositories;
using Aveline.Api.Modules.Billing.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;

namespace Aveline.Api.Tests;

/// <summary>
/// Issue #198 — the pricing snapshot persisted on each usage record (FR-1.4).
/// </summary>
public class UsagePricingSnapshotTests
{
    private readonly AppDbContext _context;

    public UsagePricingSnapshotTests()
    {
        _context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"PricingSnapshot_{Guid.NewGuid()}")
            .Options);
    }

    private UsageTrackerService CreateService(bool useLegacyFormula)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Billing:AbnormalCostThresholdUsd"] = "1.00",
                ["Pricing:UseLegacyFormula"] = useLegacyFormula ? "true" : "false",
            })
            .Build();

        var pricing = new PricingService(
            new PricingRepository(_context),
            new PricingRuleCache(new MemoryCache(new MemoryCacheOptions())),
            new AuditService(
                new AuditRepository(_context), new AuditRedactor(),
                new HttpContextAccessor(), new TestLogger<AuditService>()),
            new TestLogger<PricingService>());

        return new UsageTrackerService(
            new UsageRepository(_context), new TestLogger<UsageTrackerService>(), config, pricing);
    }

    private void SeedActiveRule()
    {
        _context.BlossomConversionRules.Add(new BlossomConversionRule
        {
            ScopeKind = BlossomRuleScopeKind.ProviderModel,
            Provider = "openai",
            Model = "gpt-4o",
            UnitsPerBlossom = 500,
            MinimumChargeBlossoms = 0.1m,
            RoundingMode = BlossomRoundingMode.HalfUp,
            RoundingDecimals = 2,
            EffectiveFrom = DateTime.UtcNow.AddDays(-1),
            Status = BlossomRuleStatus.Active,
            Version = 3,
            ChangeReason = "Snapshot test rule.",
            CreatedByUserId = Guid.CreateVersion7(),
        });
        _context.SaveChanges();
    }

    private static RecordUsageRequest Request() => new(
        Guid.NewGuid(), Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"),
        "openai", "gpt-4o", 1000, 0, 0, 0.001m);

    [Fact]
    public async Task Record_WhenRuleMatches_StoresTheSnapshot()
    {
        SeedActiveRule();
        var service = CreateService(useLegacyFormula: false);

        var record = await service.RecordWorkflowUsageAsync(Request());

        Assert.NotNull(record.PricingRuleId);
        Assert.Equal(3, record.PricingRuleVersion);
        Assert.Equal(500, record.UnitsPerBlossom);
        Assert.Equal(BlossomRoundingMode.HalfUp, record.RoundingMode);
        Assert.Equal((short?)2, record.RoundingDecimals);
        Assert.Equal((long?)1000, record.NormalizedUnits);
    }

    [Fact]
    public async Task Record_WhenFallbackUsed_StoresNulls()
    {
        var service = CreateService(useLegacyFormula: false);

        var record = await service.RecordWorkflowUsageAsync(Request());

        Assert.Null(record.PricingRuleId);
        Assert.Null(record.UnitsPerBlossom);
        Assert.Null(record.RoundingMode);
        Assert.Null(record.NormalizedUnits);
    }

    [Fact]
    public async Task Record_WhenLegacyFormula_StoresNulls()
    {
        SeedActiveRule();
        var service = CreateService(useLegacyFormula: true);

        var record = await service.RecordWorkflowUsageAsync(Request());

        Assert.Null(record.PricingRuleId);
    }
}
