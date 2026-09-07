using Aveline.Api.Modules.Notifications.Channels;
using Aveline.Api.Modules.Notifications.Repositories;
using Aveline.Api.Modules.Notifications.Services;

namespace Aveline.Api.Modules.Notifications;

/// <summary>
/// Dependency-injection registration for the Notifications module. The realtime channel
/// is backed by SignalR; push/email remain logging demo implementations until the FCM
/// adapter lands in a later slice. Channel swaps never change the dispatcher.
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
        services.AddScoped<IRealtimeChannel, SignalRRealtimeChannel>();
        services.AddScoped<IEmailChannel, LoggingEmailChannel>();

        return services;
    }
}
