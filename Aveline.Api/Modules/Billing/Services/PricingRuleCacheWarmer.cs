using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Billing.Repositories;

namespace Aveline.Api.Modules.Billing.Services;

/// <summary>
/// Hosted service that pre-resolves every active conversion rule into the in-process
/// cache every 60 seconds (FR-1.10). Warming is idempotent, so no distributed lock is
/// required: several instances may warm their own caches concurrently.
/// </summary>
public sealed class PricingRuleCacheWarmer(
    IServiceScopeFactory scopeFactory,
    ILogger<PricingRuleCacheWarmer> logger) : BackgroundService
{
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(60);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await WarmAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // Warming is best-effort; a failure must never crash the host.
                logger.LogError(exception, "Pricing rule cache warm failed.");
            }

            try
            {
                await Task.Delay(DefaultInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>Resolves every active rule scope (plus the global fallback) once.</summary>
    public async Task WarmAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IPricingRepository>();
        var pricing = scope.ServiceProvider.GetRequiredService<IPricingService>();

        var activeRules = await repository.ListRulesAsync(
            new PricingRuleFilter(Status: BlossomRuleStatus.Active), page: 1, pageSize: 200, cancellationToken);

        var scopes = activeRules
            .Select(rule => (rule.Provider, rule.Model))
            .Append(((string?)null, (string?)null))
            .Distinct();

        var now = DateTime.UtcNow;
        foreach (var (provider, model) in scopes)
        {
            await pricing.ResolveAsync(provider, model, now, cancellationToken);
        }
    }
}
