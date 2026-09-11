using System.Security.Cryptography;
using System.Text;

namespace Aveline.Api.Modules.Statistics.Telemetry;

/// <summary>
/// Hashes the client-controlled dimensions the raw log is allowed to keep. Raw IP
/// addresses and user agents are never stored (FR-5.9, §8.6).
/// </summary>
public static class MetricDimensionHasher
{
    /// <summary>SHA-256 of <c>salt|ip</c>, lowercase hex, or <c>null</c> for a blank IP.</summary>
    public static string? HashIp(string? ipAddress, string? salt)
        => string.IsNullOrWhiteSpace(ipAddress) ? null : Hash($"{salt}|{ipAddress}");

    /// <summary>SHA-256 of the user agent, lowercase hex.</summary>
    public static string? HashUserAgent(string? userAgent)
        => string.IsNullOrWhiteSpace(userAgent) ? null : Hash(userAgent);

    /// <summary>Lowercase hex SHA-256 with no IP or agent content retained.</summary>
    public static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
