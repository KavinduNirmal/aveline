using Aveline.Api.Modules.Billing.Domain;
using Aveline.Api.Modules.Billing.Endpoints;
using Aveline.Api.Modules.Billing.Repositories;
using Aveline.Api.Modules.Billing.Services;

namespace Aveline.Api.Modules.Billing;

/// <summary>
/// Dependency injection and routing registration for the Billing module.
/// </summary>
public static class BillingModule
{
    public static IServiceCollection AddBillingModule(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddScoped<IUsageRepository, UsageRepository>();
        services.AddScoped<IUsageTrackerService, UsageTrackerService>();
        services.AddScoped<IBlossomLedgerRepository, BlossomLedgerRepository>();
        services.AddScoped<IBlossomService, BlossomService>();
        services.AddScoped<ISubscriptionService, SubscriptionService>();
        services.AddScoped<IPricingRepository, PricingRepository>();
        services.AddScoped<IPricingService, PricingService>();
        services.AddScoped<IEntitlementRepository, EntitlementRepository>();
        services.AddScoped<IEntitlementResolver, EntitlementResolver>();
        services.AddScoped<IEntitlementOverrideService, EntitlementOverrideService>();
        services.AddScoped<IBillingStatisticsService, BillingStatisticsService>();
        services.AddSingleton<PricingRuleCache>();
        services.AddHostedService<PricingRuleCacheWarmer>();
        services.AddHostedService<Jobs.BlossomExpiryJob>();
        services.AddHostedService<Jobs.BillingPeriodRolloverJob>();
        services.AddHostedService<Jobs.IdempotencyRecordCleanupJob>();
        services.AddHostedService<Jobs.BillingRollupJob>();
        services.AddHostedService<Jobs.EntitlementCountingJob>();

        return services;
    }

    public static IEndpointRouteBuilder MapBillingEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapUsageEndpoints();
        return app;
    }
}
