using Aveline.Api.Modules.Billing.Endpoints;
using Aveline.Api.Modules.Billing.Repositories;
using Aveline.Api.Modules.Billing.Services;

namespace Aveline.Api.Configurations;

/// <summary>Registers the idempotency replay store and its endpoint filter.</summary>
public static class IdempotencyConfiguration
{
    public static IServiceCollection AddAvelineIdempotency(this IServiceCollection services)
    {
        services.AddScoped<IIdempotencyRepository, IdempotencyRepository>();
        services.AddScoped<IIdempotencyService, IdempotencyService>();
        services.AddScoped<Modules.Billing.Endpoints.IdempotencyEndpointFilter>();
        return services;
    }

    /// <summary>
    /// Wraps the request body in a rewindable buffer for requests that carry an
    /// <c>Idempotency-Key</c>, **before** minimal-API parameter binding reads it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The buffer has to be established here rather than inside <see cref="IdempotencyEndpointFilter"/>
    /// because endpoint filters run after the body has been bound: by then the JSON has already been
    /// read to the end, and the filter's own <c>EnableBuffering()</c> call wraps a stream that has
    /// nothing left in it. The filter rewinds and reads the bytes this middleware preserved.
    /// </para>
    /// <para>
    /// It is deliberately conditional on the header, so the buffering (which can spill to disk for a
    /// large body) is paid only by the routes that actually dedup.
    /// </para>
    /// </remarks>
    public static IApplicationBuilder UseAvelineIdempotencyBodyBuffering(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (context.Request.Headers.ContainsKey(IdempotencyEndpointFilter.HeaderName))
            {
                context.Request.EnableBuffering();
            }

            await next(context);
        });
}
