using System;

namespace Aveline.Api.Modules.VisualIntelligence.Models;

public class CustomerMatch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrgId { get; set; }
    public Guid CustomerId { get; set; }
    public Guid ItemId { get; set; }
    public decimal MatchConfidence { get; set; }
    public string MatchReason { get; set; } = string.Empty;
    public bool EmployeeActed { get; set; } = false;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // Backward compatibility aliases
    public double MatchScore
    {
        get => (double)MatchConfidence;
        set => MatchConfidence = (decimal)value;
    }

    public string Reason
    {
        get => MatchReason;
        set => MatchReason = value;
    }

    public InventoryItem? Item { get; set; }
}
