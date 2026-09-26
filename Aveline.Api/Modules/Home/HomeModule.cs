using Aveline.Api.Modules.Home.Services;

namespace Aveline.Api.Modules.Home;

/// <summary>
/// Dependency-injection registration for the Home module: the derived focus feed
/// and the dismissal record that is its only persisted state.
/// </summary>
public static class HomeModule
{
    public static IServiceCollection AddHomeModule(this IServiceCollection services)
    {
        services.AddScoped<IFocusFeedService, FocusFeedService>();
        services.AddScoped<IFocusDismissalService, FocusDismissalService>();
        return services;
    }
}
