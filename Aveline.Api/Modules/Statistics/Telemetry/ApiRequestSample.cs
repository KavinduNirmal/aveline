namespace Aveline.Api.Modules.Statistics.Telemetry;

/// <summary>
/// One measured request, produced by <see cref="ApiTelemetryMiddleware"/> and enqueued on
/// the bounded <see cref="TelemetryChannel"/> (FR-6.1, FR-6.3).
/// </summary>
/// <remarks>
/// A <see langword="readonly struct"/> so enqueueing allocates nothing. It deliberately
/// carries no path, query string, body or raw IP address (FR-6.2, §8.6).
/// </remarks>
public readonly struct ApiRequestSample
{
    public DateTime OccurredAt { get; init; }

    /// <summary><c>NULL</c> = unattributed (BR-6.1).</summary>
    public Guid? OrganizationId { get; init; }

    public Guid? ApiKeyId { get; init; }

    public Guid? UserId { get; init; }

    /// <summary>Route template only — never the raw path (FR-6.2).</summary>
    public required string RouteTemplate { get; init; }

    public required string HttpMethod { get; init; }

    public short StatusCode { get; init; }

    public int DurationMs { get; init; }

    public int RequestBytes { get; init; }

    public int ResponseBytes { get; init; }

    public string? RequestId { get; init; }

    public Guid? TraceId { get; init; }

    /// <summary>SHA-256 of the client IP; the raw address is never carried.</summary>
    public string? ClientIpHash { get; init; }

    public string? UserAgentHash { get; init; }

    public string? ErrorCode { get; init; }

    public string? ResourceType { get; init; }

    public string? ResourceId { get; init; }

    /// <summary>
    /// <c>true</c> when this sample must also be written to the sampled raw log (BR-6.3).
    /// The rollup is never sampled, so this only governs <c>ApiRequestLogs</c>.
    /// </summary>
    public bool ShouldPersistRaw { get; init; }
}
