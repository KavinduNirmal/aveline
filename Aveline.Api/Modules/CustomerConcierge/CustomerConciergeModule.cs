using Aveline.Api.Modules.CustomerConcierge.Repositories;
using Aveline.Api.Modules.CustomerConcierge.Services;

namespace Aveline.Api.Modules.CustomerConcierge;

/// <summary>
/// Dependency-injection registration for the Customer Concierge &amp; Memory module (Slice 1).
/// Services delegate to repositories; the Python agent service talks to the internal endpoints
/// mapped in <see cref="Endpoints.CustomerConciergeEndpoints"/> (wired in Program.cs).
/// </summary>
public static class CustomerConciergeModule
{
    public static IServiceCollection AddCustomerConciergeModule(this IServiceCollection services)
    {
        // Embedding client (OpenAI-compatible embeddings). BaseAddress defaults to OpenAI and can
        // be overridden via the "Embeddings:BaseUrl" config key. EmbeddingService throws at call
        // time when Embeddings:ApiKey is absent, so unconfigured environments never make a call.
        services.AddHttpClient<IEmbeddingService, EmbeddingService>((sp, client) =>
        {
            var baseUrl = sp.GetRequiredService<IConfiguration>()["Embeddings:BaseUrl"];
            client.BaseAddress = string.IsNullOrWhiteSpace(baseUrl)
                ? new Uri("https://api.openai.com")
                : new Uri(baseUrl);
        });

        // Repositories
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<ICustomerMemoryRepository, CustomerMemoryRepository>();
        services.AddScoped<ICustomerInteractionRepository, CustomerInteractionRepository>();
        services.AddScoped<ICustomerConsentRepository, CustomerConsentRepository>();
        services.AddScoped<ICustomerEventRepository, CustomerEventRepository>();
        services.AddScoped<ICustomerTagRepository, CustomerTagRepository>();

        // Services
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<ICustomerMemoryService, CustomerMemoryService>();
        services.AddScoped<ICustomerInteractionService, CustomerInteractionService>();
        services.AddScoped<ICustomerConsentService, CustomerConsentService>();
        services.AddScoped<ICustomerEventService, CustomerEventService>();
        services.AddScoped<ICustomerLoyaltyService, CustomerLoyaltyService>();
        services.AddScoped<IEventReminderService, EventReminderService>();
        services.AddHostedService<EventReminderWorker>();

        return services;
    }
}
