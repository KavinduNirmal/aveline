using Aveline.Api.Infrastructure.Integrations;

namespace Aveline.Api.Configurations;

/// <summary>
/// Registers HTTP clients for internal services (e.g. the agent service).
/// </summary>
public static class ServiceClientsConfiguration
{
    public static IServiceCollection AddAgentServiceClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var baseUrl = configuration["AgentService:BaseUrl"]
            ?? throw new InvalidOperationException("AgentService:BaseUrl is not configured.");

        // DelegatingHandlers must be transient: HttpClientFactory assigns InnerHandler
        // on each resolved handler, so a cached/singleton instance is invalid.
        services.AddTransient<InternalServiceAuthHandler>();

        services
            .AddHttpClient<IAgentServiceClient, AgentServiceClient>(client =>
            {
                client.BaseAddress = new Uri(baseUrl);
            })
            .AddHttpMessageHandler<InternalServiceAuthHandler>();

        return services;
    }

    /// <summary>
    /// Registers the Clerk Backend API client used to grant roles on admin approval.
    /// Requires <c>Clerk:SecretKey</c> at call time (enforced inside the client).
    /// </summary>
    public static IServiceCollection AddClerkAdminClient(this IServiceCollection services)
    {
        services.AddHttpClient<IClerkAdminClient, ClerkAdminClient>();
        return services;
    }
}
