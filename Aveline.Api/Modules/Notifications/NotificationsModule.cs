using Aveline.Api.Configurations;
using Aveline.Api.Modules.Notifications.Channels;
using Aveline.Api.Modules.Notifications.Repositories;
using Aveline.Api.Modules.Notifications.Services;

namespace Aveline.Api.Modules.Notifications;

/// <summary>
/// Dependency-injection registration for the Notifications module. The realtime channel is
/// backed by SignalR. The push channel is backed by FCM when Firebase is configured,
/// otherwise a logging demo implementation is used so the app runs without credentials.
/// Channel swaps never change the dispatcher.
/// </summary>
public static class NotificationsModule
{
    public static IServiceCollection AddNotificationsModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<IDeviceTokenRepository, DeviceTokenRepository>();
        services.AddScoped<IUserNotificationRepository, UserNotificationRepository>();

        services.AddScoped<IRecipientResolver, OrganizationRecipientResolver>();
        services.AddScoped<IChannelRouter, ChannelRouter>();
        services.AddScoped<INotificationDispatcher, NotificationDispatcher>();

        services.AddScoped<IRealtimeChannel, SignalRRealtimeChannel>();
        services.AddScoped<IEmailChannel, LoggingEmailChannel>();

        if (FirebaseConfiguration.IsConfigured(configuration))
        {
            services.AddSingleton<IFirebaseMessagingClient, FirebaseMessagingClient>();
            services.AddScoped<IPushChannel, FcmPushChannel>();
        }
        else
        {
            services.AddScoped<IPushChannel, LoggingPushChannel>();
        }

        return services;
    }
}
