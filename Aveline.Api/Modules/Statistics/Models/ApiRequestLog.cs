namespace Aveline.Api.Modules.Statistics.Models;

/// <summary>
/// A sampled raw request record for forensics and slow-request diagnosis (S-31).
/// Declaratively range-partitioned by day on <see cref="OccurredAt"/>; the partitioning
/// DDL is applied by the hand-edited M7 migration because EF Core cannot express it
/// (domain-model.md §10.2).
/// </summary>
/// <remarks>
/// Never stores request or response bodies, query strings, or raw IP addresses
/// (FR-5.9, §8.6): only <see cref="ClientIpHash"/> and <see cref="UserAgentHash"/>.
/// </remarks>
public class ApiRequestLog
{
    /// <summary>Identity, part of the partitioned composite primary key.</summary>
    public long Id { get; set; }

    /// <summary>Partition key (day granularity).</summary>
    public DateTime OccurredAt { get; set; }

    public Guid? OrganizationId { get; set; }

    public Guid? ApiKeyId { get; set; }

    public Guid? UserId { get; set; }

    public string RouteTemplate { get; set; } = string.Empty;

    public string HttpMethod { get; set; } = string.Empty;

    public short StatusCode { get; set; }

    public int DurationMs { get; set; }

    public int RequestBytes { get; set; }

    public int ResponseBytes { get; set; }

    /// <summary>The <c>X-Request-Id</c> value (FR-6.10).</summary>
    public string? RequestId { get; set; }

    public Guid? TraceId { get; set; }

    /// <summary>SHA-256 of the client IP; the raw address is never stored.</summary>
    public string? ClientIpHash { get; set; }

    /// <summary>SHA-256 of the user agent.</summary>
    public string? UserAgentHash { get; set; }

    public string? ErrorCode { get; set; }

    /// <summary>e.g. <c>Customer</c>, <c>Order</c>.</summary>
    public string? ResourceType { get; set; }

    /// <summary>So a slow request links to the entity it touched.</summary>
    public string? ResourceId { get; set; }
}
