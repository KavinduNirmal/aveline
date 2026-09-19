using Aveline.Api.Modules.Conversations.Attachments;
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
        services.AddScoped<IConversationReadStateRepository, ConversationReadStateRepository>();
        services.AddScoped<IMessageAttachmentRepository, MessageAttachmentRepository>();
        // The byte boundary: the database adapter today, a CDN adapter later. Every caller goes
        // through the interface, so the swap changes no read path.
        services.AddScoped<IAttachmentStore, DatabaseAttachmentStore>();
        services.AddScoped<IConversationService, ConversationService>();
        services.AddScoped<IMessageBroadcaster, SignalRMessageBroadcaster>();
        services.AddHostedService<ConversationEventSubscriber>();
        // Unbound uploads (a cancelled picker, a refused send) are swept after the TTL.
        services.AddHostedService<AttachmentSweepJob>();

        return services;
    }
}
