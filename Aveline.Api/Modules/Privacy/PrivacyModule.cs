using Aveline.Api.Modules.CustomerConcierge.Services;
using Aveline.Api.Modules.Privacy.Jobs;
using Aveline.Api.Modules.Privacy.Metrics;
using Aveline.Api.Modules.Privacy.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aveline.Api.Modules.Privacy;

/// <summary>
/// Dependency-injection registration for the privacy surface (consent transparency, the permanent
/// opt-out link and the first-contact disclosure). The signing key is read by
/// <see cref="PrivacyLinkSigner"/> and validated at startup by
/// <see cref="PrivacyOptionsValidator"/>.
/// </summary>
public static class PrivacyModule
{
    public static IServiceCollection AddPrivacyModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // The signer and the body builder are stateless, so they are singletons. The disclosure
        // service is scoped because it holds the request's AppDbContext (Pr2's trap, documented in
        // IntegrationsModule): a background sender must create its own scope, which
        // DisclosureDispatchWorker does.
        services.AddSingleton<IPrivacyLinkSigner, PrivacyLinkSigner>();
        services.AddSingleton<IDisclosureBodyBuilder, DisclosureBodyBuilder>();
        services.AddSingleton<DisclosureMetrics>();
        services.AddSingleton<OtpMetrics>();
        services.AddScoped<IDisclosureDispatchService, DisclosureDispatchService>();

        // The OTP service is stateless apart from the distributed cache, so it is a singleton and
        // safe to resolve from any scope. TimeProvider.System is the production clock; tests swap it.
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IOtpService, OtpService>();
        // Scoped, not singleton: the outbound channel it depends on holds the scoped AppDbContext.
        services.AddScoped<IOtpDeliveryService, OtpDeliveryService>();

        // The acknowledgement gate is stateless over the cache; the dispatcher is scoped because it
        // holds AppDbContext, and the worker creates a scope per intent.
        services.AddSingleton<IOptOutAcknowledgementBodyBuilder, OptOutAcknowledgementBodyBuilder>();
        services.AddSingleton<IOptOutAcknowledgementGate, DistributedOptOutAcknowledgementGate>();
        services.AddSingleton<PrivacyDeliveryMetrics>();
        services.AddScoped<IOptOutAcknowledgementService, OptOutAcknowledgementService>();

        // The revocation core. PhoneSubjectLocator is the cross-organisation read behind a global
        // opt-out (plan §5.4 Option 1); it is scoped with the context it queries.
        services.AddScoped<IPhoneSubjectLocator, PhoneSubjectLocator>();
        services.AddScoped<IConsentRevoker, ConsentRevoker>();

        // One queue instance serves both halves: the webhook enqueues through the writer interface,
        // the worker drains through the reader interface. Registered concrete first so the two
        // interface registrations resolve the same channel.
        services.AddSingleton<DisclosureDispatchQueue>();
        services.AddSingleton<IDisclosureDispatchQueue>(
            sp => sp.GetRequiredService<DisclosureDispatchQueue>());
        services.AddSingleton<IDisclosureDispatchQueueReader>(
            sp => sp.GetRequiredService<DisclosureDispatchQueue>());
        services.AddHostedService<DisclosureDispatchWorker>();

        // Phase 5 (plan §7.2, §7.3): export and erasure. All scoped because they hold the request's
        // AppDbContext; the tombstone store is what the consent gate consults after an erasure
        // (Q-4), and the cache invalidator is best-effort over the shared distributed cache.
        services.AddScoped<IConsentTombstoneStore, ErasureTombstoneStore>();
        services.AddScoped<ICustomerCacheInvalidator, CustomerCacheInvalidator>();
        services.AddScoped<IDataSubjectRequestLog, DataSubjectRequestLog>();
        services.AddScoped<IDataSubjectExportService, DataSubjectExportService>();
        services.AddScoped<IErasureService, ErasureService>();

        // Phase 6 (plan §11 Phase 6 item 6.2): the notification producers. Scoped because the
        // dispatcher is scoped (it resolves the scoped repositories), and the revocation, erasure
        // and disclosure services that call it are scoped for the same reason. The rights metric
        // family is a singleton instrument, like the other metric families.
        services.AddScoped<IPrivacyNotificationService, PrivacyNotificationService>();
        services.AddSingleton<RightsMetrics>();

        return services;
    }
}
