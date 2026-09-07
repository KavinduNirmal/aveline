namespace Aveline.Api.Common.MultiTenancy;

/// <summary>
/// Defines an entity belonging to a specific boutique organization (multi-tenant isolation).
/// </summary>
public interface ITenantEntity
{
    /// <summary>
    /// Foreign key referencing the boutique organization (shop) that owns this entity.
    /// </summary>
    Guid OrganizationId { get; set; }
}
