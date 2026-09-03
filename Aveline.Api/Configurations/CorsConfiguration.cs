using Microsoft.AspNetCore.Cors;

namespace Aveline.Api.Configurations;

/// <summary>
/// Configures the CORS policy that restricts cross-origin browser clients
/// (e.g. the Vite admin dashboard) to a configured allow-list of origins.
/// </summary>
/// <remarks>
/// Native Flutter clients send no Origin header and are unaffected by CORS.
/// Allowed origins come from config key <c>Cors:AllowedOrigins</c>
/// (e.g. <c>Cors__AllowedOrigins__0=http://localhost:5173</c>).
/// </remarks>
public static class CorsConfiguration
{
    /// <summary>The named CORS policy applied via <c>app.UseCors(...)</c>.</summary>
    public const string DefaultPolicy = "aveline-cors";

    public static IServiceCollection AddAvelineCors(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
        if (allowedOrigins is null
            || allowedOrigins.Length == 0
            || allowedOrigins.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException("Cors:AllowedOrigins must contain at least one non-empty origin.");
        }

        services.AddCors(options =>
        {
            options.AddPolicy(DefaultPolicy, policy =>
                policy.WithOrigins(allowedOrigins)
                      .AllowAnyHeader()
                      .AllowAnyMethod()
                      .WithExposedHeaders("X-Completed-Onboarding"));
        });

        return services;
    }
}
