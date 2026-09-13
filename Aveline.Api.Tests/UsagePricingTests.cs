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
/// Issue #187 — usage ingest prices through the conversion rules, guarded by the
/// <c>Pricing:UseLegacyFormula</c> escape hatch (FR-1.4, FR-1.12, BR-1.12).
/// </summary>
public class UsagePricingTests
{
    private static readonly DateTime Now = DateTime.UtcNow;

    private readonly AppDbContext _context;

    public UsagePricingTests()
    {
        _context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"UsagePricing_{Guid.NewGuid()}")
            .Options);
    }

    private UsageTrackerService CreateService(bool useLegacyFormula, bool withPricing = true)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Billing:AbnormalCostThresholdUsd"] = "1.00",
                ["Pricing:UseLegacyFormula"] = useLegacyFormula ? "true" : "false",
            })
            .Build();

        IPricingService? pricing = null;
        if (withPricing)
        {
            var audit = new AuditService(
                new AuditRepository(_context),
                new AuditRedactor(),
                new HttpContextAccessor(),
                new TestLogger<AuditService>());

            pricing = new PricingService(
                new PricingRepository(_context),
                new PricingRuleCache(new MemoryCache(new MemoryCacheOptions())),
                audit,
                new TestLogger<PricingService>());
        }

        return new UsageTrackerService(
            new UsageRepository(_context),
            new TestLogger<UsageTrackerService>(),
            config,
            pricing);
    }

    private void SeedActiveRule(int unitsPerBlossom, string? provider = null, string? model = null)
    {
        _context.BlossomConversionRules.Add(new BlossomConversionRule
        {
            ScopeKind = provider is null ? BlossomRuleScopeKind.Global : BlossomRuleScopeKind.ProviderModel,
            Provider = provider,
            Model = model,
            UnitsPerBlossom = unitsPerBlossom,
            MinimumChargeBlossoms = 0.1m,
            RoundingMode = BlossomRoundingMode.Ceiling,
            RoundingDecimals = 1,
            EffectiveFrom = Now.AddDays(-1),
            Status = BlossomRuleStatus.Active,
            ChangeReason = "Seed rule for ingest pricing.",
            CreatedByUserId = Guid.CreateVersion7(),
        });
        _context.SaveChanges();
    }

    private static RecordUsageRequest Request(int input, int output, int cached = 0) => new(
        OrganizationId: Guid.NewGuid(),
        RequestId: Guid.NewGuid().ToString("N"),
        WorkflowId: Guid.NewGuid().ToString("N"),
        Provider: "openai",
        Model: "gpt-4o",
        InputTokens: input,
        OutputTokens: output,
        CachedTokens: cached,
        ActualCostUsd: 0.001m);

    [Fact]
    public async Task RecordWorkflowUsageAsync_WhenLegacyFormulaEnabled_UsesLegacyNumbers()
    {
        SeedActiveRule(250); // would double the charge if the rule were applied
        var service = CreateService(useLegacyFormula: true);

        var record = await service.RecordWorkflowUsageAsync(Request(1000, 0));

        Assert.Equal(1.0m, record.BlossomUnits);
    }

    [Fact]
    public async Task RecordWorkflowUsageAsync_WhenRuleActive_PricesWithTheRule()
    {
        SeedActiveRule(500);
        var service = CreateService(useLegacyFormula: false);

        var record = await service.RecordWorkflowUsageAsync(Request(1000, 0));

        // 1000 / 500 = 2.0
        Assert.Equal(2.0m, record.BlossomUnits);
    }

    [Fact]
    public async Task RecordWorkflowUsageAsync_WhenNoRule_FallsBackToLegacyDefaults()
    {
        var service = CreateService(useLegacyFormula: false);

        var record = await service.RecordWorkflowUsageAsync(Request(1000, 0));

        Assert.Equal(1.0m, record.BlossomUnits);
    }

    [Fact]
    public async Task RecordWorkflowUsageAsync_WhenPricingServiceMissing_UsesLegacyNumbers()
    {
        var service = CreateService(useLegacyFormula: false, withPricing: false);

        var record = await service.RecordWorkflowUsageAsync(Request(1000, 0));

        Assert.Equal(1.0m, record.BlossomUnits);
    }

    [Fact]
    public async Task RecordWorkflowUsageAsync_RuleChange_DoesNotAlterExistingRecords()
    {
        SeedActiveRule(500);
        var service = CreateService(useLegacyFormula: false);

        var first = await service.RecordWorkflowUsageAsync(Request(1000, 0));

        // A later, more expensive rule must not rewrite the already-stored record.
        _context.BlossomConversionRules.Add(new BlossomConversionRule
        {
            ScopeKind = BlossomRuleScopeKind.Global,
            UnitsPerBlossom = 100,
            MinimumChargeBlossoms = 0.1m,
            RoundingMode = BlossomRoundingMode.Ceiling,
            RoundingDecimals = 1,
            EffectiveFrom = Now.AddMinutes(1),
            Status = BlossomRuleStatus.Active,
            ChangeReason = "Later, more expensive rule.",
            CreatedByUserId = Guid.CreateVersion7(),
        });
        await _context.SaveChangesAsync();

        var stored = await _context.AiUsageRecords.FindAsync(first.Id);

        Assert.Equal(2.0m, stored!.BlossomUnits);
    }
}
