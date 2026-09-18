using Aveline.Api.Modules.Billing.Domain;

namespace Aveline.Api.Modules.Billing.Models;

/// <summary>
/// The effective-dated token to Blossom normalisation rate (domain-model.md §3.1).
/// Immutable once it has priced usage; corrections supersede rather than edit (BR-1.7).
/// </summary>
public sealed class BlossomConversionRule
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public BlossomRuleScopeKind ScopeKind { get; set; }

    public string? Provider { get; set; }

    public string? Model { get; set; }

    /// <summary>Normalised units that buy one Blossom. Default matches the legacy formula.</summary>
    public int UnitsPerBlossom { get; set; } = 1000;

    public decimal MinimumChargeBlossoms { get; set; } = 0.1m;

    public BlossomRoundingMode RoundingMode { get; set; } = BlossomRoundingMode.Ceiling;

    public short RoundingDecimals { get; set; } = 1;

    /// <summary>Inclusive start of the effective window.</summary>
    public DateTime EffectiveFrom { get; set; }

    /// <summary>Exclusive end of the effective window; <c>null</c> means open-ended.</summary>
    public DateTime? EffectiveTo { get; set; }

    public BlossomRuleStatus Status { get; set; } = BlossomRuleStatus.Draft;

    /// <summary>Monotonic per <c>(ScopeKind, Provider, Model)</c>.</summary>
    public int Version { get; set; } = 1;

    public string ChangeReason { get; set; } = string.Empty;

    public Guid CreatedByUserId { get; set; }

    public Guid? ApprovedByUserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Optimistic concurrency token mapped to PostgreSQL's <c>xmin</c> system column.
    /// A uint row-version property is auto-detected by the Npgsql provider.
    /// </summary>
    public uint ConcurrencyToken { get; set; }
}
