using System;

namespace Aveline.Api.Modules.VisualIntelligence.DTOs;

public class UpdateSourcingRequestDto
{
    public string? Category { get; set; }
    public string? Color { get; set; }
    public string? Description { get; set; }
    public decimal? TargetPrice { get; set; }
    public decimal? EstimatedCost { get; set; }
    public decimal? ProposedMarkup { get; set; }
    public Guid? SupplierId { get; set; }
    public string? SupplierName { get; set; }
    public string? ClientName { get; set; }
    public string? ReferenceImageUrl { get; set; }
    public string? Urgency { get; set; }
    public int? QuantityNeeded { get; set; }
    public string? Status { get; set; }
}
