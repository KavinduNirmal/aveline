using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Aveline.Api.Configurations;

/// <summary>
/// Centralizes Clerk JWT bearer authentication and authorization setup.
/// </summary>
public static class AuthenticationConfiguration
{
    /// <summary>
    /// Configures JwtBearer to validate Clerk JWTs against the Clerk JWKS endpoint
    /// and registers role-based authorization.
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
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = authority,
                    // Clerk session tokens (including jwt-aveline-v1 template tokens) do not
                    // always carry an "aud" claim; the instance signing key (JWKS kid) already
                    // scopes tokens to this Clerk instance, so audience is not enforced here.
                    ValidateAudience = false,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    NameClaimType = ClaimTypes.NameIdentifier,
                };

                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = context =>
                    {
                        // Expose the JWT-template role claims (user_role = Aveline team,
                        // org_role = per-store owner/staff) as standard role claims so
                        // [Authorize(Roles = "...")] and IsInRole work for both.
                        var identity = context.Principal?.Identity as ClaimsIdentity;
                        foreach (var roleClaim in new[] { "user_role", "org_role" })
                        {
                            var value = context.Principal?.FindFirst(roleClaim)?.Value;
                            if (!string.IsNullOrEmpty(value))
                            {
                                identity?.AddClaim(new Claim(ClaimTypes.Role, value));
                            }
                        }
                        return Task.CompletedTask;
                    }
                };
            });

        return services;
    }
}
