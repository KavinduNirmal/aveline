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
using Microsoft.Extensions.Logging;

namespace Aveline.Api.Tests;

/// <summary>
/// Issue #185 — effective-dated rule resolution and lifecycle (BR-1.6..BR-1.9).
/// </summary>
public class PricingServiceTests
{
    private static readonly DateTime Now = DateTime.UtcNow;

    private readonly AppDbContext _context;

    public PricingServiceTests()
    {
        _context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"PricingService_{Guid.NewGuid()}")
            .Options);
    }

    private PricingService CreateService()
    {
        var repository = new PricingRepository(_context);
        var audit = new AuditService(
            new AuditRepository(_context),
            new AuditRedactor(),
            new HttpContextAccessor(),
            new TestLogger<AuditService>());

        return new PricingService(
            repository,
            new PricingRuleCache(new MemoryCache(new MemoryCacheOptions())),
            audit,
            new TestLogger<PricingService>());
    }

    private static CreatePricingRuleCommand Command(
        BlossomRuleScopeKind scopeKind = BlossomRuleScopeKind.Global,
        string? provider = null,
        string? model = null,
        int unitsPerBlossom = 1000,
        DateTime? effectiveFrom = null,
        string changeReason = "A valid change reason.",
        bool allowBackdate = false) => new(
            ScopeKind: scopeKind,
            Provider: provider,
            Model: model,
            UnitsPerBlossom: unitsPerBlossom,
            MinimumChargeBlossoms: 0.1m,
            RoundingMode: BlossomRoundingMode.Ceiling,
            RoundingDecimals: 1,
            EffectiveFrom: effectiveFrom ?? Now.AddDays(1),
            EffectiveTo: null,
            ChangeReason: changeReason,
            CreatedByUserId: Guid.CreateVersion7(),
            AllowBackdate: allowBackdate);

    private static BlossomConversionRule ActiveRule(
        BlossomRuleScopeKind scopeKind,
        string? provider,
        string? model,
        DateTime from,
        DateTime? to,
        int unitsPerBlossom) => new()
    {
        ScopeKind = scopeKind,
        Provider = provider,
        Model = model,
        UnitsPerBlossom = unitsPerBlossom,
        MinimumChargeBlossoms = 0.1m,
        RoundingMode = BlossomRoundingMode.Ceiling,
        RoundingDecimals = 1,
        EffectiveFrom = from,
        EffectiveTo = to,
        Status = BlossomRuleStatus.Active,
        ChangeReason = "Seed rule.",
        CreatedByUserId = Guid.CreateVersion7(),
    };

    [Fact]
    public async Task ResolveAsync_PrefersProviderModelThenProviderThenGlobal()
    {
        var at = Now;
        _context.BlossomConversionRules.AddRange(
            ActiveRule(BlossomRuleScopeKind.Global, null, null, at.AddDays(-1), null, 1111),
            ActiveRule(BlossomRuleScopeKind.Provider, "openai", null, at.AddDays(-1), null, 2222),
            ActiveRule(BlossomRuleScopeKind.ProviderModel, "openai", "gpt-4o", at.AddDays(-1), null, 3333));
        await _context.SaveChangesAsync();

        var service = CreateService();

        Assert.Equal(3333, (await service.ResolveAsync("openai", "gpt-4o", at)).Rule.UnitsPerBlossom);
        Assert.Equal(2222, (await service.ResolveAsync("openai", "other-model", at)).Rule.UnitsPerBlossom);
        Assert.Equal(1111, (await service.ResolveAsync("anthropic", "claude", at)).Rule.UnitsPerBlossom);
    }

    [Fact]
    public async Task ResolveAsync_EffectiveFromIsInclusive_AndEffectiveToIsExclusive()
    {
        var start = Now;
        var end = start.AddDays(10);
        _context.BlossomConversionRules.Add(
            ActiveRule(BlossomRuleScopeKind.Global, null, null, start, end, 1000));
        await _context.SaveChangesAsync();

        var service = CreateService();

        Assert.False((await service.ResolveAsync(null, null, start)).IsFallback);
        Assert.True((await service.ResolveAsync(null, null, end)).IsFallback);
        Assert.False((await service.ResolveAsync(null, null, start.AddDays(5))).IsFallback);
    }

    [Fact]
    public async Task ResolveAsync_WithNoRule_ReturnsFallbackDefaults()
    {
        var service = CreateService();

        var resolution = await service.ResolveAsync("openai", "gpt-4o", Now);

        Assert.True(resolution.IsFallback);
        Assert.Equal(1000, resolution.Rule.UnitsPerBlossom);
        Assert.Equal(0.1m, resolution.Rule.MinimumChargeBlossoms);
        Assert.Equal(BlossomRoundingMode.Ceiling, resolution.Rule.RoundingMode);
        Assert.Equal(1, resolution.Rule.RoundingDecimals);
    }

    [Fact]
    public async Task CreateRuleAsync_PersistsDraft()
    {
        var service = CreateService();

        var rule = await service.CreateRuleAsync(Command());

        Assert.Equal(BlossomRuleStatus.Draft, rule.Status);
        Assert.Equal(1, rule.Version);
        Assert.Equal(1, await _context.BlossomConversionRules.CountAsync());
    }

    [Fact]
    public async Task CreateRuleAsync_GlobalScopeWithProvider_Throws()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<PricingValidationException>(() =>
            service.CreateRuleAsync(Command(BlossomRuleScopeKind.Global, provider: "openai")));
    }

    [Fact]
    public async Task CreateRuleAsync_PastEffectiveFromWithoutBackdate_Throws()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<PricingBackdateForbiddenException>(() =>
            service.CreateRuleAsync(Command(effectiveFrom: Now.AddDays(-1))));
    }

    [Fact]
    public async Task CreateRuleAsync_ShortChangeReason_Throws()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<PricingValidationException>(() =>
            service.CreateRuleAsync(Command(changeReason: "too short")));
    }

    [Fact]
    public async Task UpdateRuleAsync_OnActiveRule_ThrowsImmutable()
    {
        var service = CreateService();
        var rule = await service.CreateRuleAsync(Command());
        await service.ActivateRuleAsync(rule.Id);

        await Assert.ThrowsAsync<PricingRuleImmutableException>(() =>
            service.UpdateRuleAsync(rule.Id, new UpdatePricingRuleCommand(
                UnitsPerBlossom: 2000, null, null, null, null, null, "Updated after activation.", rule.CreatedByUserId)));
    }

    [Fact]
    public async Task ActivateRuleAsync_TrimsPredecessorAndSupersedesIt()
    {
        var predecessor = ActiveRule(BlossomRuleScopeKind.Global, null, null, Now.AddDays(-1), null, 1000);
        _context.BlossomConversionRules.Add(predecessor);
        await _context.SaveChangesAsync();

        var service = CreateService();
        var rule = await service.CreateRuleAsync(Command(effectiveFrom: Now.AddDays(1)));

        var activated = await service.ActivateRuleAsync(rule.Id);

        Assert.Equal(BlossomRuleStatus.Active, activated.Status);

        var storedPredecessor = await _context.BlossomConversionRules.SingleAsync(r => r.Id == predecessor.Id);
        Assert.Equal(BlossomRuleStatus.Superseded, storedPredecessor.Status);
        Assert.Equal(activated.EffectiveFrom, storedPredecessor.EffectiveTo);
    }

    [Fact]
    public async Task CancelRuleAsync_SetsCancelledStatus()
    {
        var service = CreateService();
        var rule = await service.CreateRuleAsync(Command());

        var cancelled = await service.CancelRuleAsync(rule.Id, "No longer required after review.");

        Assert.Equal(BlossomRuleStatus.Cancelled, cancelled.Status);
    }

    [Fact]
    public async Task CancelRuleAsync_ShortReason_Throws()
    {
        var service = CreateService();
        var rule = await service.CreateRuleAsync(Command());

        await Assert.ThrowsAsync<PricingValidationException>(() =>
            service.CancelRuleAsync(rule.Id, "short"));
    }

    [Fact]
    public async Task ResolveAsync_AfterActivation_ReflectsTheNewRule()
    {
        var service = CreateService();
        var activateAt = Now.AddDays(1);
        var rule = await service.CreateRuleAsync(Command(effectiveFrom: activateAt, unitsPerBlossom: 5000));
        await service.ActivateRuleAsync(rule.Id);

        var resolution = await service.ResolveAsync(null, null, activateAt);

        Assert.False(resolution.IsFallback);
        Assert.Equal(5000, resolution.Rule.UnitsPerBlossom);
    }
}
