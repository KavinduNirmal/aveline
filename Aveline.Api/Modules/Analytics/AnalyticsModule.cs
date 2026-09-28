using Aveline.Api.Configurations;
using Aveline.Api.Modules.Analytics.Endpoints;
using Aveline.Api.Modules.Analytics.Jobs;
using Aveline.Api.Modules.Analytics.Services;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aveline.Api.Modules.Analytics;

/// <summary>
/// Dependency injection and routing registration for the admin business-KPI module, following
/// the module-registration convention established by <c>BillingModule</c> and
/// <c>StatisticsModule</c>.
/// </summary>
public static class AnalyticsModule
{
    public static IServiceCollection AddAnalyticsModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<BusinessAnalyticsOptions>(
            configuration.GetSection(BusinessAnalyticsOptions.SectionName));

        // The clock the default `to` bound and the plan-mix `asOf` instant are read from.
        // Registered here rather than assumed, so a host that never called
        // `AddSingleton(TimeProvider.System)` still resolves it.
        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton<BusinessKpiCache>();
        services.AddScoped<IBusinessKpiService, BusinessKpiService>();

        // The daily 02:00 UTC snapshot, after BillingRollupJob's 01:30 so a same-day tier change
        // is already settled. Registered as a singleton first so a test can drive one pass.
        services.AddSingleton<OrganizationSubscriptionSnapshotJob>();
        services.AddHostedService(sp => sp.GetRequiredService<OrganizationSubscriptionSnapshotJob>());

        // Fail startup — not the first request — when a shared cache is required but the
        // deployment would silently fall back to the per-instance in-memory implementation, so
        // two replicas cannot serve different figures for the same request (R-9). Read straight
        // from configuration: building a provider here would be a captive-dependency trap.
        var options = configuration
            .GetSection(BusinessAnalyticsOptions.SectionName)
            .Get<BusinessAnalyticsOptions>() ?? new BusinessAnalyticsOptions();

        var error = BusinessKpiCache.ValidateSharedCache(
            options,
            CacheConfiguration.ResolveRedisConnectionString(configuration));

        if (error is not null)
        {
            throw new InvalidOperationException(error);
        }

        return services;
    }

    /// <summary>Maps the <c>/api/v1</c>-relative business-KPI routes (call on the v1 group).</summary>
    public static IEndpointRouteBuilder MapAnalyticsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapBusinessKpiEndpoints();
        return endpoints;
    }
}
