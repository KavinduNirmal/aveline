namespace Aveline.Api.Authorization;

/// <summary>
/// Role values produced by the Clerk "jwt-aveline-v1" template claims:
///   <c>user_role</c> = {{user.public_metadata.role}} (Aveline team role)
///   <c>org_role</c>  = {{org.role}}                 (per-store owner/staff role)
/// Both are promoted to <see cref="System.Security.Claims.ClaimTypes.Role"/> at
/// authentication time, so role checks evaluate both.
/// </summary>
public static class Roles
{
    public const string Staff = "staff";
    public const string CustomerRelations = "customer_relations";
    public const string Moderator = "moderator";
    public const string Admin = "admin";
    public const string Owner = "owner";

    public const string BoutiqueStaff = "org:boutique_staff";
    public const string BoutiqueManager = "org:boutique_manager";
    public const string BoutiqueSupervisor = "org:boutique_supervisor";
    public const string BoutiqueOwner = "org:boutique_owner";

    public static readonly string[] StaffAccess =
    [
        Staff, CustomerRelations, Moderator, Admin, Owner,
        BoutiqueStaff, BoutiqueManager, BoutiqueSupervisor, BoutiqueOwner,
    ];

    public static readonly string[] ManagementAccess =
    [
        Moderator, Admin, Owner,
        BoutiqueManager, BoutiqueSupervisor, BoutiqueOwner,
    ];

    public static readonly string[] OwnershipAccess =
    [
        Owner, BoutiqueOwner,
    ];

    /// <summary>
    /// Aveline team roles that operate the platform console. These are not boutique
    /// tenants, so they never complete tenant onboarding (see OnboardingMiddleware).
    /// Deliberately narrower than <see cref="StaffAccess"/>: <c>staff</c> and
    /// <c>customer_relations</c> remain subject to the onboarding gate, and the
    /// <c>org:*</c> boutique roles always are.
    /// </summary>
    public static readonly string[] OnboardingExemptRoles =
    [
        Moderator, Admin, Owner,
    ];
}
