using System.Security.Claims;
using Aveline.Api.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Aveline.Api.Configurations;

/// <summary>
/// Centralizes Clerk JWT bearer authentication setup.
/// </summary>
public static class AuthenticationConfiguration
{
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
            .AddJwtBearer(options =>
            {
                options.Authority = authority;
                options.TokenValidationParameters = BuildTokenValidationParameters(authority);

                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = context =>
                    {
                        RoleClaimNormalizer.PromoteRoleClaims(context.Principal);
                        return Task.CompletedTask;
                    }
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
}
