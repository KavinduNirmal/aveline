using Aveline.Api.Modules.Conversations.Attachments;
using Aveline.Api.Modules.Conversations.Repositories;
using Aveline.Api.Modules.Conversations.Services;
using Aveline.Api.Modules.Media;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
        // The byte boundary: one row seam, selected once, by `Media:Provider`. `MediaModule`
        // (L1) registers the provider seam `IMediaStorage`; this is the row seam only.
        // This must be the *single* registration: `AddScoped` resolves the last one, so an
        // unconditional database line would silently win over the provider selection
        // (strategy §3.1, migration plan §6.2).
        var provider = configuration.GetValue("Media:Provider", MediaProvider.Database);
        switch (provider)
        {
            case MediaProvider.Database:
                services.AddScoped<IAttachmentStore, DatabaseAttachmentStore>();
                break;

            case MediaProvider.Cloudinary:
                services.AddScoped<IAttachmentStore, CloudinaryAttachmentStore>();
                break;

            default:
                throw new InvalidOperationException(
                    $"Media:Provider value '{provider}' is not recognised. The valid values are "
                    + "'database' and 'cloudinary'.");
        }

        // The Cloudinary row seam reads the clock for the `date:` tag and the row's CreatedAtUtc.
        // `TryAdd` keeps this idempotent with the registration `AnalyticsModule` also makes.
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IConversationService, ConversationService>();
        // The outbound channel path is its own service rather than a method on the conversation
        // service: "written into the Salon" and "delivered to the customer's channel" are different
        // promises, and one class that could do both is how a caller comes to confuse them.
        services.AddScoped<ICustomerDeliveryService, CustomerDeliveryService>();
        services.AddScoped<IMessageBroadcaster, SignalRMessageBroadcaster>();
        services.AddHostedService<ConversationEventSubscriber>();
        // Unbound uploads (a cancelled picker, a refused send) are swept after the TTL.
        services.AddHostedService<AttachmentSweepJob>();
        // Bound attachments past `Conversations:AttachmentRetentionDays` are released from the
        // provider and then deleted (S7). Deliberately a separate job from the sweep above:
        // different trigger, different policy (strategy §5.3).
        services.AddHostedService<ConversationAttachmentRetentionJob>();
        // Clients created before the create path minted a Salon are repaired once at boot. The
        // repair is idempotent, so a restart is safe and a second instance is a no-op.
        services.AddHostedService<CustomerSalonBackfillJob>();

        return services;
    }
}
