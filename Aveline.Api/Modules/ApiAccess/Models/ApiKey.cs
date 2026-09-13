using System.Text.Json.Serialization;

namespace Aveline.Api.Modules.ApiAccess.Models;

/// <summary>
/// A scoped, revocable credential that authenticates a single organization's
/// machine-to-machine traffic (FR-3.10–FR-3.18).
/// </summary>
/// <remarks>
/// The plaintext secret is returned exactly once at creation. Only the lookup
/// <see cref="Prefix"/> and a SHA-256 <see cref="KeyHash"/> are persisted; neither
/// is ever returned by an endpoint, logged, or written to the audit log.
/// </remarks>
public class ApiKey
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Owning organization. A key can only ever act within this tenant.</summary>
    public Guid OrganizationId { get; set; }

    /// <summary>Human label chosen by the creator.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// First 16 characters of the secret (<c>avl_live_7Qk2mZ</c>). Unique and the
    /// only indexed lookup path, so key resolution is O(1) and never scans.
    /// </summary>
    public string Prefix { get; set; } = string.Empty;

    /// <summary>SHA-256 hex digest of the full secret. Never exposed.</summary>
    public string KeyHash { get; set; } = string.Empty;

    /// <summary>Algorithm identifier for <see cref="KeyHash"/>; currently always <c>sha256</c>.</summary>
    public string HashAlgorithm { get; set; } = ApiKeyHashing.AlgorithmSha256;

    /// <summary>
    /// Granted permissions; a subset of <c>Permissions.All</c> validated at creation
    /// and re-validated (fail-closed) at authorization time (BR-3.4).
    /// </summary>
    public List<string> Scopes { get; set; } = [];

    public ApiKeyEnvironment Environment { get; set; } = ApiKeyEnvironment.Live;

    public ApiKeyStatus Status { get; set; } = ApiKeyStatus.Active;

    public Guid CreatedByUserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Optional absolute expiry; a null value means the key does not expire.</summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>Updated at most once per minute per key (FR-3.18).</summary>
    public DateTime? LastUsedAt { get; set; }

    public DateTime? RevokedAt { get; set; }

    public Guid? RevokedByUserId { get; set; }

    public string? RevokedReason { get; set; }

    /// <summary>Lifetime request counter, flushed from the telemetry pipeline.</summary>
    public long RequestCount { get; set; }

    /// <summary>SHA-256 of the last client IP; never the raw address (privacy, §8.6).</summary>
    public string? LastUsedIpHash { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ApiKeyEnvironment
{
    Live,
    Test,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ApiKeyStatus
{
    Active,
    Revoked,
    Expired,
}

/// <summary>Stable identifiers and constants for the API-key hashing scheme.</summary>
public static class ApiKeyHashing
{
    public const string AlgorithmSha256 = "sha256";
}
