using System;
using System.Collections.Generic;

namespace Aveline.Api.Modules.VisualIntelligence.Models;

public class SourcingRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrgId { get; set; }
    public Guid? CustomerId { get; set; }
    public string? ReferenceImageUrl { get; set; }
    public string? ItemDescription { get; set; }
    public Guid? SupplierId { get; set; }
    public decimal? EstimatedCost { get; set; }
    public decimal? ProposedMarkup { get; set; }
    public decimal? ProposedPrice { get; set; }
    public string Status { get; set; } = "pending";
    public Guid? CreatedBy { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }

    // Backward compatibility aliases
    public string Category { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public string Description
    {
        get => ItemDescription ?? string.Empty;
        set => ItemDescription = value;
    }
    public decimal? TargetPrice
    {
        get => ProposedPrice;
        set => ProposedPrice = value;
    }
}
