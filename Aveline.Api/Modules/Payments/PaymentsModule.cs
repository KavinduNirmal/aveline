using Aveline.Api.Modules.Payments.Domain;
using Aveline.Api.Modules.Payments.Endpoints;
using Aveline.Api.Modules.Payments.Providers;
using Aveline.Api.Modules.Payments.Repositories;
using Aveline.Api.Modules.Payments.Services;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Aveline.Api.Modules.Payments;

/// <summary>
/// Dependency injection registration for the payment module (plan §6.5). No endpoints are mapped in
/// P2-A: this phase introduces the abstraction and the adapters without changing any existing flow.
/// </summary>
public static class PaymentsModule
{
    public static IServiceCollection AddPaymentsModule(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        // Fail startup - not the first request. Read straight from configuration: building a provider
        // here would be a captive-dependency trap (AnalyticsModule precedent).
        var options = configuration
            .GetSection(PaymentsOptions.SectionName)
            .Get<PaymentsOptions>() ?? new PaymentsOptions();

        options.Validate(environment);

        services.Configure<PaymentsOptions>(configuration.GetSection(PaymentsOptions.SectionName));

        // The mock reads the clock through TimeProvider (plan §7.3, determinism). Other modules
        // already TryAdd it; keeping the fallback here means AddPaymentsModule is self-sufficient
        // for the factory and adapter resolution.
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<IPaymentProviderFactory, PaymentProviderFactory>();
        services.AddScoped<IPaymentIntentRepository, PaymentIntentRepository>();
        services.AddScoped<IPaymentProviderEventRepository, PaymentProviderEventRepository>();
        services.AddScoped<IPaymentIntentService, PaymentIntentService>();
        services.AddScoped<IPaymentSettlementService, PaymentSettlementService>();
        services.AddScoped<IPaymentProviderEventService, PaymentProviderEventService>();
        // Plan §10 Phase 7: the durable expiry state and the one reconciliation derivation the read
        // and the alert both consume.
        services.AddScoped<IPaymentIntentExpiryService, PaymentIntentExpiryService>();
        services.AddScoped<IPaymentReconciliationService, PaymentReconciliationService>();

        // The two Phase 7 background passes. Both ride the repository's scheduled-job base, so a
        // multi-instance deployment runs each once and `RunAsync` stays testable.
        services.AddHostedService<Jobs.PaymentIntentExpiryJob>();
        services.AddHostedService<Jobs.PaymentReconciliationMetricCollectorJob>();

        // Adapters. One line per provider; adding one changes nothing else. Registered keyed and
        // unconditionally so `IPaymentProviderFactory.Resolve(key)` can serve an intent created under
        // a provider that is no longer the configured one.
        //
        // Both adapters are **singletons**, and that is load-bearing rather than an optimisation.
        // Each keeps its provider-side state (the intent store and the idempotency index) in instance
        // fields, standing in for the provider's own store; a scoped registration would hand every
        // HTTP request a different adapter, so the Development-only mock checkout page could never
        // settle an intent created by the request before it. Both adapters are thread-safe
        // (ConcurrentDictionary) and depend only on singletons. The durable dedup identity remains
        // the intent row's filtered unique indexes, never this cache.
        services.AddKeyedSingleton<IPaymentProvider, ManualPaymentProvider>(ManualPaymentProvider.ProviderKey);
        services.AddKeyedSingleton<IPaymentProvider, MockPaymentProvider>(MockPaymentProvider.ProviderKey);

        // Phase 8: the external adapter (plan §8.4 S5, decision Q5 - OnePay). One named HttpClient,
        // because the adapter is a singleton and a typed client's default transient lifetime would be
        // a captive dependency. The named client exists for every deployment even when another
        // provider is active, so flipping `Payments:Provider` to "onepay" needs no other change.
        services.AddHttpClient(OnePayPaymentProvider.HttpClientName, client =>
        {
            var baseUrl = string.IsNullOrWhiteSpace(options.OnePay.BaseUrl)
                ? "https://api.onepay.lk"
                : options.OnePay.BaseUrl;

            client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        services.AddKeyedSingleton<IPaymentProvider, OnePayPaymentProvider>(OnePayPaymentProvider.ProviderKey);
        //   ^ The line above is the whole swap surface (plan §8.2).

        // Guardrail 4 of plan §7.4: a gauge a Grafana alert can fire on when it reads 1 outside
        // Development. Primed from configuration so the series exists as 0 or 1 per provider key
        // even when a request never resolves the adapter, and re-asserted by the adapter's
        // constructor so a directly-constructed mock still reports itself.
        services.AddSingleton<PaymentMetrics>(_ =>
        {
            var metrics = new PaymentMetrics();
            metrics.SetMockProviderActive(MockPaymentProvider.ProviderKey, options.Mock.Enabled);
            return metrics;
        });

        return services;
    }

    /// <summary>
    /// Routes registered at the application root rather than inside the versioned group, because the
    /// admin statistics paths already carry the full <c>/api/v1</c> prefix (the Blossom sibling's
    /// reason). Today: the P7 reconciliation read.
    /// </summary>
    public static IEndpointRouteBuilder MapPaymentModuleEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPaymentReconciliationEndpoints();
        return app;
    }
}
