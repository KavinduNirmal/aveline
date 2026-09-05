using System.Security.Claims;
using Aveline.Api.Authorization;

namespace Aveline.Api.Tests;

/// <summary>
/// Verifies <see cref="RoleClaimNormalizer"/> promotes the Clerk role claims
/// (<c>user_role</c>, <c>org_role</c>) into standard role claims.
/// </summary>
public class RoleClaimNormalizerTests
{
    private static ClaimsPrincipal PrincipalWith(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "test"));

    private static string[] PromotedRoles(ClaimsPrincipal principal) =>
        principal.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray();

    [Fact]
    public void Promotes_UserRole_And_OrgRole()
    {
        var principal = PrincipalWith(
            new Claim("sub", "user_123"),
            new Claim("user_role", "staff"),
            new Claim("org_role", "org:boutique_supervisor"));

        RoleClaimNormalizer.PromoteRoleClaims(principal);

        Assert.Equal(new[] { "staff", "org:boutique_supervisor" }, PromotedRoles(principal));
    }

    [Fact]
    public void Promotes_Only_When_RoleClaim_Present()
    {
        var principal = PrincipalWith(new Claim("user_role", "admin"));

        RoleClaimNormalizer.PromoteRoleClaims(principal);

        Assert.Equal(new[] { "admin" }, PromotedRoles(principal));
    }

    [Fact]
    public void Leaves_Original_Claims_Intact()
    {
        var principal = PrincipalWith(new Claim("user_role", "owner"));

        RoleClaimNormalizer.PromoteRoleClaims(principal);

        Assert.Equal("owner", principal.FindFirst("user_role")?.Value);
        Assert.Contains(ClaimTypes.Role, principal.Claims.Select(c => c.Type));
    }

    [Fact]
    public void No_RoleClaims_Adds_None()
    {
        var principal = PrincipalWith(new Claim("sub", "user_123"));

        RoleClaimNormalizer.PromoteRoleClaims(principal);

        Assert.Empty(PromotedRoles(principal));
    }

    [Fact]
    public void Existing_RoleClaim_Is_Not_Duplicated()
    {
        var principal = PrincipalWith(
            new Claim(ClaimTypes.Role, "admin"),
            new Claim("user_role", "staff"));

        RoleClaimNormalizer.PromoteRoleClaims(principal);

        Assert.Equal(new[] { "admin", "staff" }, PromotedRoles(principal));
    }

    [Fact]
    public void Empty_Value_Is_Ignored()
    {
        var principal = PrincipalWith(new Claim("user_role", string.Empty));

        RoleClaimNormalizer.PromoteRoleClaims(principal);

        Assert.Empty(PromotedRoles(principal));
    }

    [Fact]
    public void Promotes_RoleClaims_AsLowercaseCanonicalValues()
    {
        var principal = PrincipalWith(new Claim("org_role", "ORG:BOUTIQUE_OWNER"));

        RoleClaimNormalizer.PromoteRoleClaims(principal);

        Assert.Equal(new[] { "org:boutique_owner" }, PromotedRoles(principal));
    }

    [Fact]
    public void Null_Principal_Is_NoOp()
    {
        RoleClaimNormalizer.PromoteRoleClaims(null);
    }

    [Fact]
    public void Principal_Without_Identity_Is_NoOp()
    {
        var principal = new ClaimsPrincipal();

        RoleClaimNormalizer.PromoteRoleClaims(principal);

        Assert.Null(principal.Identity);
    }
}
