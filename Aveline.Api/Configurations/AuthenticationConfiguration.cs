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
            .AddScheme<Aveline.Api.Infrastructure.Integrations.ScrapeTokenAuthenticationOptions, Aveline.Api.Infrastructure.Integrations.ScrapeTokenAuthenticationHandler>(
                Aveline.Api.Infrastructure.Integrations.ScrapeTokenAuthenticationHandler.SchemeName, _ => { })
            .AddScheme<Aveline.Api.Modules.ApiAccess.Authentication.ApiKeyAuthenticationOptions, Aveline.Api.Modules.ApiAccess.Authentication.ApiKeyAuthenticationHandler>(
                Aveline.Api.Modules.ApiAccess.Authentication.ApiKeyAuthenticationHandler.SchemeName, _ => { })
            .AddJwtBearer(options =>
            {
                options.Authority = authority;
                // Defaults to true (secure). Set Clerk:RequireHttpsMetadata=false only
                // for local development/tests against an HTTP authority.
                options.RequireHttpsMetadata = configuration.GetValue("Clerk:RequireHttpsMetadata", true);
                options.TokenValidationParameters = BuildTokenValidationParameters(
                    authority, configuration["Clerk:Audience"]);

                options.Events = new JwtBearerEvents
                {
                    // SignalR clients pass the JWT via the ?access_token= query string
                    // (the framework's convention for non-WebSocket transports). JwtBearer
                    // only reads the Authorization header by default, so forward the query
                    // token for hub requests when no header is present.
                    OnMessageReceived = context =>
                    {
                        var accessToken = context.Request.Query["access_token"];
                        var path = context.HttpContext.Request.Path;
                        if (!string.IsNullOrEmpty(accessToken)
                            && path.StartsWithSegments("/hubs")
                            && string.IsNullOrEmpty(context.Token))
                        {
                            context.Token = accessToken;
                        }
                        return Task.CompletedTask;
                    },
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
    /// <param name="authority">The Clerk Frontend API base; bound as the only valid issuer.</param>
    /// <param name="audience">
    /// Optional expected <c>aud</c>. Audience validation is enforced only when this is set, so the
    /// historical behaviour (no <c>aud</c> claim, issuer-bound) is preserved for deployments that
    /// have not added the claim to the Clerk JWT template.
    /// </param>
    /// <remarks>
    /// Clerk session tokens (including jwt-aveline-v1 template tokens) do not always carry an
    /// <c>aud</c> claim; the instance signing key (JWKS kid) already scopes tokens to this Clerk
    /// instance, so audience is not enforced by default. That leaves a latent gap: if a second
    /// application is ever added to the same Clerk instance, tokens minted for it would be
    /// accepted here because nothing else distinguishes the audience (assessment F-2.3). Wire
    /// <c>Clerk:Audience</c> (and the matching claim in the JWT template) to close it; see
    /// ADR-008, "Audience validation" for the revisit trigger.
    /// </remarks>
    public static TokenValidationParameters BuildTokenValidationParameters(
        string authority, string? audience = null)
    {
        var audienceConfigured = !string.IsNullOrWhiteSpace(audience);

        return new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = authority,
            // Opt-in: an absent Clerk:Audience keeps the previous behaviour rather than
            // rejecting every token at once.
            ValidateAudience = audienceConfigured,
            ValidAudiences = audienceConfigured ? [audience!] : null,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            NameClaimType = ClaimTypes.NameIdentifier,
        };
    }

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
