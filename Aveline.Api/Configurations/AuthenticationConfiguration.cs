using System.Security.Claims;
using Aveline.Api.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace Aveline.Api.Configurations;

/// <summary>
/// Centralizes Clerk JWT bearer authentication setup.
/// </summary>
public static class AuthenticationConfiguration
{
    /// <summary>Log category for authentication events.</summary>
    public const string LogCategory = "Aveline.Api.Authentication";

    /// <summary>
    /// Configures JwtBearer to validate Clerk JWTs against the Clerk JWKS endpoint.
    /// </summary>
    /// <remarks>
    /// Authority = Clerk Frontend API base (e.g. https://&lt;instance&gt;.clerk.accounts.dev),
    /// read from config key <c>Clerk:Authority</c> (override via <c>Clerk__Authority</c>).
    /// </remarks>
    public static IServiceCollection AddAvelineAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var authority = configuration["Clerk:Authority"]
            ?? throw new InvalidOperationException("Clerk:Authority is not configured.");

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddScheme<Aveline.Api.Infrastructure.Integrations.InternalTokenAuthenticationOptions, Aveline.Api.Infrastructure.Integrations.InternalTokenAuthenticationHandler>(
                Aveline.Api.Infrastructure.Integrations.InternalTokenAuthenticationHandler.SchemeName, _ => { })
            .AddJwtBearer(options =>
            {
                options.Authority = authority;
                // Defaults to true (secure). Set Clerk:RequireHttpsMetadata=false only
                // for local development/tests against an HTTP authority.
                options.RequireHttpsMetadata = configuration.GetValue("Clerk:RequireHttpsMetadata", true);
                options.TokenValidationParameters = BuildTokenValidationParameters(authority);

                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = context =>
                    {
                        RoleClaimNormalizer.PromoteRoleClaims(context.Principal);
                        var userId = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier)
                                    ?? context.Principal?.FindFirstValue("sub");
                        Log(context.HttpContext, LogLevel.Information,
                            "Authentication succeeded. userId={UserId}", userId);
                        return Task.CompletedTask;
                    },
                    OnAuthenticationFailed = context =>
                    {
                        Log(context.HttpContext, LogLevel.Warning, context.Exception,
                            "Authentication failed. reason={Reason}",
                            context.Exception?.GetType().Name);
                        return Task.CompletedTask;
                    },
                };
            });

        return services;
    }

    /// <summary>
    /// Builds the JWT validation rules for Clerk session tokens.
    /// </summary>
    /// <remarks>
    /// Clerk session tokens (including jwt-aveline-v1 template tokens) do not always
    /// carry an <c>aud</c> claim; the instance signing key (JWKS kid) already scopes
    /// tokens to this Clerk instance, so audience is not enforced here.
    /// </remarks>
    public static TokenValidationParameters BuildTokenValidationParameters(string authority) => new()
    {
        ValidateIssuer = true,
        ValidIssuer = authority,
        ValidateAudience = false,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        NameClaimType = ClaimTypes.NameIdentifier,
    };

    private static ILogger Logger(HttpContext httpContext) =>
        httpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(LogCategory);

    private static void Log(
        HttpContext httpContext,
        LogLevel level,
        string message,
        params object?[] args) =>
        Logger(httpContext).Log(level, message, args);

    private static void Log(
        HttpContext httpContext,
        LogLevel level,
        Exception? exception,
        string message,
        params object?[] args) =>
        Logger(httpContext).Log(level, exception, message, args);
}
