using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace Aveline.Api.Infrastructure.Integrations;

/// <summary>A Clerk session as returned by the Backend API.</summary>
public sealed record ClerkSession(
    string Id,
    string Status,
    DateTime? CreatedAt,
    DateTime? LastActiveAt,
    DateTime? ExpireAt);

/// <summary>Operations against the Clerk Backend API.</summary>
public interface IClerkAdminClient
{
    /// <summary>Sets a user's <c>public_metadata.role</c> to <c>admin</c>.</summary>
    Task GrantAdminRoleAsync(string clerkUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists a user's sessions. Clerk owns sessions; Aveline stores no session state
    /// (FR-3.6).
    /// </summary>
    Task<IReadOnlyList<ClerkSession>> ListSessionsAsync(
        string clerkUserId, CancellationToken cancellationToken = default);

    /// <summary>Revokes every active session for a user; returns how many were revoked.</summary>
    Task<int> RevokeAllSessionsAsync(string clerkUserId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Minimal Clerk Backend API client used to grant the <c>admin</c> team role once an
/// administrator approval request is approved, and to list/revoke user sessions.
/// Requires <c>Clerk:SecretKey</c>.
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
        // Clerk deprecated public_metadata on PATCH /v1/users/{id}; metadata is now
        // updated through the dedicated PATCH /v1/users/{id}/metadata endpoint.
        using var request = CreateRequest(
            HttpMethod.Patch, $"/users/{Uri.EscapeDataString(clerkUserId)}/metadata");
        request.Content = JsonContent.Create(new { public_metadata = new { role = "admin" } });

        var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Clerk Backend API rejected the role grant for user '{clerkUserId}' at " +
                $"{request.RequestUri}: {(int)response.StatusCode} {body}");
        }
    }

    public async Task<IReadOnlyList<ClerkSession>> ListSessionsAsync(
        string clerkUserId, CancellationToken cancellationToken = default)
    {
        // Sessions are listed from `/sessions` filtered by `user_id`.
        //
        // Clerk does serve `/users/{id}/sessions`, but only far enough to load the user: for a real
        // id it hands the request on and a router answers with a plain-text `404 page not found`,
        // which the endpoint reports as a 502 (assessment F-4.5). For an *unknown* id Clerk answers
        // in its JSON error shape instead — so probing the route with a made-up id makes it look
        // healthy, which is exactly how this was misdiagnosed twice.
        //
        // `limit` defaults to 10 and caps at 500. Without it, `RevokeAllSessionsAsync` — which lists
        // through here — would silently revoke only the first page.
        using var request = CreateRequest(
            HttpMethod.Get,
            $"/sessions?user_id={Uri.EscapeDataString(clerkUserId)}&limit=500");

        var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Clerk Backend API rejected the session list for user '{clerkUserId}' at " +
                $"{request.RequestUri}: {(int)response.StatusCode} {body}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var sessions = new List<ClerkSession>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            sessions.Add(new ClerkSession(
                element.TryGetProperty("id", out var id) ? id.GetString() ?? string.Empty : string.Empty,
                element.TryGetProperty("status", out var status) ? status.GetString() ?? string.Empty : string.Empty,
                ReadTimestamp(element, "created_at"),
                ReadTimestamp(element, "last_active_at"),
                ReadTimestamp(element, "expire_at")));
        }

        return sessions;
    }

    public async Task<int> RevokeAllSessionsAsync(
        string clerkUserId, CancellationToken cancellationToken = default)
    {
        var sessions = await ListSessionsAsync(clerkUserId, cancellationToken);
        var revoked = 0;

        foreach (var session in sessions.Where(s => !string.Equals(s.Status, "ended", StringComparison.OrdinalIgnoreCase)))
        {
            using var request = CreateRequest(
                HttpMethod.Post, $"/sessions/{Uri.EscapeDataString(session.Id)}/revoke");
            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new HttpRequestException(
                    $"Clerk Backend API rejected revoking session '{session.Id}' at " +
                    $"{request.RequestUri}: {(int)response.StatusCode} {body}");
            }

            revoked++;
        }

        return revoked;
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var secretKey = _configuration["Clerk:SecretKey"];
        if (string.IsNullOrWhiteSpace(secretKey))
        {
            throw new InvalidOperationException(
                "Clerk:SecretKey is not configured. Session operations cannot proceed.");
        }

        var baseUrl = _configuration["Clerk:BackendApiUrl"] ?? DefaultBackendApiUrl;
        var request = new HttpRequestMessage(method, $"{baseUrl.TrimEnd('/')}{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secretKey);
        return request;
    }

    private static DateTime? ReadTimestamp(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt64(out var milliseconds))
        {
            return null;
        }

        return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).UtcDateTime;
    }
}
