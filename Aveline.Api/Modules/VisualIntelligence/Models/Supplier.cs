using System;
using System.Collections.Generic;

namespace Aveline.Api.Modules.VisualIntelligence.Models;

public class Supplier
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrgId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public Dictionary<string, object>? ContactInfo { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public string? ApiEndpoint { get; set; }
    public decimal? MinimumOrder { get; set; }
    public int? DeliveryTimeDays { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // Backward compatibility alias
    public string Name
    {
        get => SupplierName;
        set => SupplierName = value;
    }
}
