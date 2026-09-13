using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Aveline.Api.Modules.SystemHealth.HealthChecks;

/// <summary>
/// Readiness check for the Clerk JWKS document. The discovery document and JWKS are
/// cached so a healthy dependency is not fetched on every probe; an unreachable Clerk is
/// Degraded, never Unhealthy, because already-issued JWTs keep validating (§8.7).
/// </summary>
public sealed class ClerkJwksHealthCheck(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    IMemoryCache cache) : IHealthCheck
{
    private const string CacheKey = "health:clerk-jwks";
    private static readonly TimeSpan SuccessTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan FailureTtl = TimeSpan.FromSeconds(30);

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue(CacheKey, out object? cachedValue)
            && cachedValue is HealthCheckResult cached)
        {
            return cached;
        }

        var authority = configuration["Clerk:Authority"];
        if (string.IsNullOrWhiteSpace(authority))
        {
            return HealthCheckResult.Degraded("Clerk authority is not configured.");
        }

        var result = await ProbeAsync(authority, cancellationToken);
        cache.Set(CacheKey, result, result.Status == HealthStatus.Healthy ? SuccessTtl : FailureTtl);
        return result;
    }

    private async Task<HealthCheckResult> ProbeAsync(string authority, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));

            var client = httpClientFactory.CreateClient(nameof(ClerkJwksHealthCheck));
            var discoveryUri = $"{authority.TrimEnd('/')}/.well-known/openid-configuration";
            var discovery = await client.GetFromJsonAsync<JsonElement>(discoveryUri, timeout.Token);

            if (!discovery.TryGetProperty("jwks_uri", out var jwksUri)
                || string.IsNullOrWhiteSpace(jwksUri.GetString()))
            {
                return HealthCheckResult.Degraded("Clerk discovery document has no JWKS URI.");
            }

            var jwks = await client.GetFromJsonAsync<JsonElement>(jwksUri.GetString()!, timeout.Token);
            if (jwks.TryGetProperty("keys", out var keys) && keys.GetArrayLength() > 0)
            {
                return HealthCheckResult.Healthy("Clerk JWKS reachable.");
            }

            return HealthCheckResult.Degraded("Clerk JWKS is empty.");
        }
        catch (Exception)
        {
            // Clerk being unreachable must not fail readiness; cached JWTs still work.
            return HealthCheckResult.Degraded("Clerk JWKS is unreachable.");
        }
    }
}
