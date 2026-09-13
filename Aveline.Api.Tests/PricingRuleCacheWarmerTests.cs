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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aveline.Api.Tests;

/// <summary>
/// Issue #187 — the cache warmer pre-resolves active rules so the ingest path never
/// pays a cold database round-trip (FR-1.10).
/// </summary>
public class PricingRuleCacheWarmerTests
{
    [Fact]
    public async Task WarmAsync_PopulatesCacheForEveryActiveScope()
    {
        var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"Warm_{Guid.NewGuid()}")
            .Options);

        var now = DateTime.UtcNow;
        context.BlossomConversionRules.AddRange(
            Rule(BlossomRuleScopeKind.Global, null, null, now, 1000),
            Rule(BlossomRuleScopeKind.ProviderModel, "openai", "gpt-4o", now, 2000));
        await context.SaveChangesAsync();

        var cache = new PricingRuleCache(new MemoryCache(new MemoryCacheOptions()));
        var services = new ServiceCollection();
        services.AddSingleton(cache);
        services.AddScoped<IPricingRepository>(_ => new PricingRepository(context));
        services.AddScoped<IAuditService>(_ => new AuditService(
            new AuditRepository(context), new AuditRedactor(),
            new HttpContextAccessor(), NullLogger<AuditService>.Instance));
        services.AddScoped<IPricingService>(provider => new PricingService(
            provider.GetRequiredService<IPricingRepository>(),
            cache,
            provider.GetRequiredService<IAuditService>(),
            NullLogger<PricingService>.Instance));

        var providerRoot = services.BuildServiceProvider();
        var warmer = new PricingRuleCacheWarmer(
            providerRoot.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<PricingRuleCacheWarmer>.Instance);

        await warmer.WarmAsync(CancellationToken.None);

        Assert.True(cache.TryGet("|", now, out var global));
        Assert.Equal(1000, global!.UnitsPerBlossom);
        Assert.True(cache.TryGet("openai|gpt-4o", now, out var providerModel));
        Assert.Equal(2000, providerModel!.UnitsPerBlossom);
    }

    private static BlossomConversionRule Rule(
        BlossomRuleScopeKind scopeKind, string? provider, string? model, DateTime from, int units) => new()
    {
        ScopeKind = scopeKind,
        Provider = provider,
        Model = model,
        UnitsPerBlossom = units,
        MinimumChargeBlossoms = 0.1m,
        RoundingMode = BlossomRoundingMode.Ceiling,
        RoundingDecimals = 1,
        EffectiveFrom = from.AddDays(-1),
        Status = BlossomRuleStatus.Active,
        ChangeReason = "Cache warmer test rule.",
        CreatedByUserId = Guid.CreateVersion7(),
    };
}
