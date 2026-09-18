using Aveline.Api.Modules.Organizations.Models;

namespace Aveline.Api.Modules.Admin.DTOs;

/// <summary>An organization as returned to the Aveline-team admin search (FR-4.8).</summary>
public sealed record AdminOrganizationDto(
    Guid Id,
    string Name,
    string Slug,
    string? ClerkOrgId,
    Guid OwnerUserId,
    string PlanTier,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt)
{
    public static AdminOrganizationDto From(Organization organization) => new(
        organization.Id,
        organization.Name,
        organization.Slug,
        organization.ClerkOrgId,
        organization.OwnerUserId,
        organization.PlanTier.ToString(),
        organization.IsActive,
        organization.CreatedAt,
        organization.UpdatedAt);
}

/// <summary>A page of organizations at the §A.4 pagination convention.</summary>
public sealed record PagedAdminOrganizations(
    IReadOnlyList<AdminOrganizationDto> Items,
    int Page,
    int PageSize,
    int Total);
