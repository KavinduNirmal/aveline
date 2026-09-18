using Aveline.Api.Modules.Conversations.Repositories;
using Aveline.Api.Modules.Conversations.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Aveline.Api.Modules.Conversations;

/// <summary>
/// Dependency-injection registration for the Conversations module ("The Salon"). The API is
/// the system of record for messages; the agent service emits content events that the API
/// persists and broadcasts.
/// </summary>
public static class ConversationsModule
{
    public static IServiceCollection AddConversationsModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<IConversationRepository, ConversationRepository>();
        services.AddScoped<IMessageRepository, MessageRepository>();
        services.AddScoped<ISignOffDecisionRepository, SignOffDecisionRepository>();
        services.AddScoped<IConversationService, ConversationService>();
        services.AddScoped<IMessageBroadcaster, SignalRMessageBroadcaster>();
        services.AddHostedService<ConversationEventSubscriber>();

        return services;
    }
}
