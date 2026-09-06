using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;

namespace Aveline.Api.Infrastructure.Integrations;

/// <summary>Operations against the Clerk Backend API.</summary>
public interface IClerkAdminClient
{
    /// <summary>Sets a user's <c>public_metadata.role</c> to <c>admin</c>.</summary>
    Task GrantAdminRoleAsync(string clerkUserId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Minimal Clerk Backend API client used to grant the <c>admin</c> team role once an
/// administrator approval request is approved. Requires <c>Clerk:SecretKey</c>.
/// </summary>
public sealed class ClerkAdminClient : IClerkAdminClient
{
    public const string DefaultBackendApiUrl = "https://api.clerk.com/v1";

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    public ClerkAdminClient(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
    }

    public async Task GrantAdminRoleAsync(string clerkUserId, CancellationToken cancellationToken = default)
    {
        var secretKey = _configuration["Clerk:SecretKey"];
        if (string.IsNullOrWhiteSpace(secretKey))
        {
            throw new InvalidOperationException(
                "Clerk:SecretKey is not configured. Administrator approval cannot grant the admin role.");
        }

        var baseUrl = _configuration["Clerk:BackendApiUrl"] ?? DefaultBackendApiUrl;
        using var request = new HttpRequestMessage(
            HttpMethod.Patch,
            $"{baseUrl.TrimEnd('/')}/users/{Uri.EscapeDataString(clerkUserId)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secretKey);
        request.Content = JsonContent.Create(new { public_metadata = new { role = "admin" } });

        var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Clerk Backend API rejected the role grant for user '{clerkUserId}': " +
                $"{(int)response.StatusCode} {body}");
        }
    }
}
