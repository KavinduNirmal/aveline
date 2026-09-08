using Aveline.Api.Modules.Notifications.Channels;
using Aveline.Api.Modules.Notifications.Repositories;
using Aveline.Api.Modules.Notifications.Services;

namespace Aveline.Api.Modules.Notifications;

/// <summary>
/// Dependency-injection registration for the Notifications module. Channels are
/// registered as scoped logging demo implementations for now; real SignalR/FCM adapters
/// replace them in later slices without changing the dispatcher.
/// </summary>
public static class NotificationsModule
{
    public static IServiceCollection AddNotificationsModule(this IServiceCollection services)
    {
        services.AddScoped<INotificationRepository, NotificationRepository>();

        services.AddScoped<IRecipientResolver, OrganizationRecipientResolver>();
        services.AddScoped<IChannelRouter, ChannelRouter>();
        services.AddScoped<INotificationDispatcher, NotificationDispatcher>();

        services.AddScoped<IPushChannel, LoggingPushChannel>();
        services.AddScoped<IRealtimeChannel, LoggingRealtimeChannel>();
        services.AddScoped<IEmailChannel, LoggingEmailChannel>();

        return services;
    }
}
