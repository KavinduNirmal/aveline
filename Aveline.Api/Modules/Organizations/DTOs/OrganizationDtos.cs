using Aveline.Api.Modules.Billing.Models;

namespace Aveline.Api.Modules.Organizations.DTOs;

/// <summary>
/// Public boutique profile used to render the tenant dashboard header and identity.
/// Intentionally excludes AI-context fields (BrandVoice, BusinessRules, etc.) which are
/// internal concierge configuration and not part of the owner-facing dashboard.
/// </summary>
public sealed record OrganizationProfileDto(
    Guid Id,
    string Name,
    string Slug,
    string? ClerkOrgId,
    Guid OwnerUserId,
    string? Address,
    string? PhoneNumber,
    string? Description,
    string? LogoUrl,
    PlanTier PlanTier,
    bool HasCompletedOnboarding,
    DateTime CreatedAt);

/// <summary>
/// A caller's membership within a boutique, including the resolved org identity so the
/// frontend can derive a tenant slug without a second lookup.
/// </summary>
public sealed record OrganizationMembershipView(
    Guid OrganizationId,
    string OrganizationName,
    string Slug,
    string BoutiqueRole,
    string Status);

/// <summary>
/// A boutique profile paired with the requesting user's membership in it. The membership
/// is null when the caller belongs to no (active) membership for the boutique, which the
/// tenant dashboard uses to gate access.
/// </summary>
public sealed record OrganizationProfileWithMembershipDto(
    OrganizationProfileDto Organization,
    OrganizationMembershipView? Membership);
