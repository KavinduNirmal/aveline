using Aveline.Api.Modules.ApiAccess.Models;

namespace Aveline.Api.Modules.ApiAccess.DTOs;

/// <summary>
/// An API key as returned by the management endpoints. The secret and its hash are
/// never part of this shape (FR-3.11).
/// </summary>
public sealed record ApiKeyDto(
    Guid Id,
    string Name,
    string Prefix,
    IReadOnlyList<string> Scopes,
    string Environment,
    string Status,
    DateTime CreatedAt,
    DateTime? ExpiresAt,
    DateTime? LastUsedAt,
    DateTime? RevokedAt,
    string? RevokedReason,
    long RequestCount)
{
    public static ApiKeyDto From(ApiKey key) => new(
        key.Id,
        key.Name,
        key.Prefix,
        key.Scopes,
        key.Environment.ToString(),
        key.Status.ToString(),
        key.CreatedAt,
        key.ExpiresAt,
        key.LastUsedAt,
        key.RevokedAt,
        key.RevokedReason,
        key.RequestCount);
}

/// <summary>Request to create a key. <c>Environment</c> is <c>live</c> or <c>test</c>.</summary>
public sealed record CreateApiKeyRequest(
    string Name,
    IReadOnlyList<string> Scopes,
    string? Environment = null,
    DateTime? ExpiresAt = null);

/// <summary>
/// The 201 response. <see cref="Secret"/> is present exactly once and can never be
/// retrieved again (FR-3.10).
/// </summary>
public sealed record CreateApiKeyResponse(ApiKeyDto Key, string Secret);

/// <summary>Request to revoke a key.</summary>
public sealed record RevokeApiKeyRequest(string? Reason = null);
