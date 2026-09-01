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
    public const string Associate = "associate";
    public const string Manager = "manager";
    public const string Owner = "owner";

    public const string OrgAssociate = "org:associate";
    public const string OrgManager = "org:manager";
    public const string OrgOwner = "org:owner";
    public const string OrgAdmin = "org:admin";
    public const string OrgMember = "org:member";
}
