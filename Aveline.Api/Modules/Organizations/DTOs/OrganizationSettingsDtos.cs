namespace Aveline.Api.Modules.Organizations.DTOs;

/// <summary>
/// Partial update of an organization's profile and settings (FR-4.1). A null field is
/// left unchanged; an empty string clears a nullable field.
/// </summary>
public sealed record UpdateOrganizationSettingsRequest(
    string? Name = null,
    string? Slug = null,
    string? Address = null,
    string? PhoneNumber = null,
    string? Description = null,
    string? LogoUrl = null,
    string? BrandVoice = null,
    string? BusinessRules = null,
    string? PreferredColorsFabrics = null,
    string? CustomerPreferences = null,
    string? BillingEmail = null,
    string? ContactEmail = null,
    string? Currency = null,
    string? TimeZone = null);

/// <summary>The owner-facing settings block plus the AI-context configuration (FR-4.2).</summary>
public sealed record OrganizationSettingsDto(
    Guid Id,
    string Name,
    string Slug,
    string? Address,
    string? PhoneNumber,
    string? Description,
    string? LogoUrl,
    string? BrandVoice,
    string? BusinessRules,
    string? PreferredColorsFabrics,
    string? CustomerPreferences,
    string? BillingEmail,
    string? ContactEmail,
    string Currency,
    string TimeZone,
    bool IsActive,
    DateTime? SuspendedAt)
{
    public static OrganizationSettingsDto From(Models.Organization org) => new(
        org.Id,
        org.Name,
        org.Slug,
        org.Address,
        org.PhoneNumber,
        org.Description,
        org.LogoUrl,
        org.BrandVoice,
        org.BusinessRules,
        org.PreferredColorsFabrics,
        org.CustomerPreferences,
        org.BillingEmail,
        org.ContactEmail,
        org.Currency,
        org.TimeZone,
        org.IsActive,
        org.SuspendedAt);
}

/// <summary>A member of an organization as returned to owners/managers (FR-3.1).</summary>
public sealed record OrganizationMemberDto(
    Guid UserId,
    string Email,
    string FirstName,
    string LastName,
    string? DisplayName,
    string? ProfileImageUrl,
    string BoutiqueRole,
    string Status,
    DateTime JoinedAt);

/// <summary>A page of organization members.</summary>
public sealed record PagedOrganizationMembers(
    IReadOnlyList<OrganizationMemberDto> Items,
    int Page,
    int PageSize,
    int Total);

/// <summary>Request to change a member's boutique role (FR-3.3).</summary>
public sealed record ChangeMemberRoleRequest(string BoutiqueRole);
