namespace Aveline.Api.Modules.Statistics.Models;

/// <summary>
/// Durable per-period quota counters. Redis holds the live counter; this table is written
/// at period boundaries and on shutdown (S-30, domain-model.md §7.3).
/// </summary>
public class ApiQuotaUsage
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; set; }

    /// <summary><c>NULL</c> = the organisation-level quota.</summary>
    public Guid? ApiKeyId { get; set; }

    /// <summary><c>api.requests.monthly</c>, <c>api.requests.perMinute</c>, …</summary>
    public string MetricKey { get; set; } = string.Empty;

    public DateTime PeriodStart { get; set; }

    public DateTime PeriodEnd { get; set; }

    /// <summary><c>NULL</c> = unlimited.</summary>
    public long? LimitValue { get; set; }

    public long UsedValue { get; set; }

    /// <summary>Set once when the warning threshold is crossed.</summary>
    public DateTime? WarnedAt { get; set; }

    /// <summary>Set once when the limit is reached.</summary>
    public DateTime? ExhaustedAt { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
