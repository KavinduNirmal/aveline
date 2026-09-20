using System.Collections.Frozen;
using System.Security.Claims;
using Aveline.Api.Modules.ApiAccess.Authentication;
using Aveline.Api.Modules.Statistics.Telemetry;

namespace Aveline.Api.Tests;

/// <summary>
/// Phase 1 of the Business KPIs plan (§5.5, DR-7): the attribution fallback. A Clerk token
/// carries Clerk's native <c>user_…</c>/<c>org_…</c> ids while Aveline stores GUIDs, so a
/// synchronous in-memory <see cref="IClaimIdentityMap"/> resolves them off the request path.
/// A Guid claim must still win without the map being consulted.
/// </summary>
public class RequestPrincipalTests
{
    private const string ClerkUserId = "user_2abcDEFghiJKL";
    private const string ClerkOrgId = "org_2xyzQRS";

    private static ClaimsPrincipal Authenticated(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, authenticationType: "Bearer"));

    private static ClaimIdentityMap Map(
        IDictionary<string, Guid>? users = null,
        IDictionary<string, Guid>? organizations = null)
    {
        var map = new ClaimIdentityMap();
        map.Replace(
            users is null
                ? FrozenDictionary<string, Guid>.Empty
                : users.ToFrozenDictionary(StringComparer.Ordinal),
            organizations is null
                ? FrozenDictionary<string, Guid>.Empty
                : organizations.ToFrozenDictionary(StringComparer.Ordinal));
        return map;
    }

    // ── Guid claims still win ─────────────────────────────────────────────────────────────

    [Fact]
    public void AGuidUserIdClaimWinsWithoutConsultingTheMap()
    {
        var userId = Guid.CreateVersion7();
        // The map deliberately answers something else; the Guid claim must take precedence.
        var map = Map(new Dictionary<string, Guid> { [ClerkUserId] = Guid.CreateVersion7() });

        var principal = Authenticated(
            new Claim("user_id", userId.ToString()),
            new Claim(ClaimTypes.NameIdentifier, ClerkUserId));

        var (_, _, resolved) = RequestPrincipal.Resolve(principal, map);

        Assert.Equal(userId, resolved);
        Assert.Equal(0, map.UnresolvedCount);
    }

    [Fact]
    public void AnApiKeyPrincipalIsUnchanged()
    {
        var organizationId = Guid.CreateVersion7();
        var apiKeyId = Guid.CreateVersion7();
        var principal = Authenticated(
            new Claim(ApiKeyClaimTypes.OrganizationId, organizationId.ToString()),
            new Claim(ApiKeyClaimTypes.ApiKeyId, apiKeyId.ToString()));

        var (organization, key, user) = RequestPrincipal.Resolve(principal, Map());

        Assert.Equal(organizationId, organization);
        Assert.Equal(apiKeyId, key);
        Assert.Null(user);
    }

    // ── Clerk-shaped claims resolve through the map ───────────────────────────────────────

    [Fact]
    public void AClerkShapedUserIdResolvesThroughTheMap()
    {
        var userId = Guid.CreateVersion7();
        var map = Map(new Dictionary<string, Guid> { [ClerkUserId] = userId });

        var principal = Authenticated(
            new Claim("user_id", ClerkUserId),
            new Claim(ClaimTypes.NameIdentifier, ClerkUserId),
            new Claim("sub", ClerkUserId));

        var (_, _, resolved) = RequestPrincipal.Resolve(principal, map);

        Assert.Equal(userId, resolved);
        Assert.Equal(0, map.UnresolvedCount);
    }

    [Fact]
    public void ASubOnlyClerkUserIdResolvesThroughTheMap()
    {
        var userId = Guid.CreateVersion7();
        var map = Map(new Dictionary<string, Guid> { [ClerkUserId] = userId });

        var principal = Authenticated(new Claim("sub", ClerkUserId));

        var (_, _, resolved) = RequestPrincipal.Resolve(principal, map);

        Assert.Equal(userId, resolved);
    }

    [Fact]
    public void AClerkShapedOrgIdResolvesThroughTheMap()
    {
        var organizationId = Guid.CreateVersion7();
        var map = Map(organizations: new Dictionary<string, Guid> { [ClerkOrgId] = organizationId });

        var principal = Authenticated(new Claim("org_id", ClerkOrgId));

        var (resolved, _, _) = RequestPrincipal.Resolve(principal, map);

        Assert.Equal(organizationId, resolved);
    }

    [Fact]
    public void BothClerkShapedClaimsResolveTogether()
    {
        var userId = Guid.CreateVersion7();
        var organizationId = Guid.CreateVersion7();
        var map = Map(
            new Dictionary<string, Guid> { [ClerkUserId] = userId },
            new Dictionary<string, Guid> { [ClerkOrgId] = organizationId });

        var principal = Authenticated(
            new Claim("user_id", ClerkUserId),
            new Claim("org_id", ClerkOrgId));

        var (organization, key, resolved) = RequestPrincipal.Resolve(principal, map);

        Assert.Equal(userId, resolved);
        Assert.Equal(organizationId, organization);
        Assert.Null(key);
        Assert.Equal(0, map.UnresolvedCount);
    }

    [Fact]
    public void ATrueGuidOrgIdClaimIsUnaffectedByTheMap()
    {
        var organizationId = Guid.CreateVersion7();
        var map = Map(organizations: new Dictionary<string, Guid> { [ClerkOrgId] = Guid.CreateVersion7() });

        var principal = Authenticated(new Claim("org_id", organizationId.ToString()));

        var (resolved, _, _) = RequestPrincipal.Resolve(principal, map);

        Assert.Equal(organizationId, resolved);
    }

    // ── Failure is a null, never a throw ──────────────────────────────────────────────────

    [Fact]
    public void AnUnknownClerkIdReturnsNullAndCountsAsUnresolved()
    {
        var map = Map(new Dictionary<string, Guid> { [ClerkUserId] = Guid.CreateVersion7() });
        var principal = Authenticated(new Claim("user_id", "user_never_seen"));

        var (_, _, resolved) = RequestPrincipal.Resolve(principal, map);

        Assert.Null(resolved);
        Assert.Equal(1, map.UnresolvedCount);
    }

    [Fact]
    public void AnUnknownClerkOrgIdIsNotCountedAsAnUnresolvedUser()
    {
        var map = Map();
        var principal = Authenticated(new Claim("org_id", "org_never_seen"));

        var (resolved, _, _) = RequestPrincipal.Resolve(principal, map);

        Assert.Null(resolved);
        Assert.Equal(0, map.UnresolvedCount);
    }

    [Fact]
    public void AnEmptyMapReturnsNullsAndDoesNotThrow()
    {
        var principal = Authenticated(
            new Claim("user_id", ClerkUserId),
            new Claim("org_id", ClerkOrgId));

        var (organization, key, user) = RequestPrincipal.Resolve(principal, Map());

        Assert.Null(organization);
        Assert.Null(key);
        Assert.Null(user);
    }

    [Fact]
    public void AnUnauthenticatedPrincipalIsUnchanged()
    {
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());

        var (organization, key, user) = RequestPrincipal.Resolve(anonymous, Map());

        Assert.Null(organization);
        Assert.Null(key);
        Assert.Null(user);
    }

    [Fact]
    public void AMalformedGuidClaimIsUnchanged()
    {
        var map = Map(new Dictionary<string, Guid> { ["not-a-guid"] = Guid.CreateVersion7() });
        var principal = Authenticated(
            new Claim("user_id", "not-a-guid"),
            new Claim("org_id", "also-not-a-guid"));

        var (organization, _, user) = RequestPrincipal.Resolve(principal, map);

        // "not-a-guid" is not a Clerk id either; the map answers, but the claim was malformed
        // rather than Clerk-shaped, so a Guid parse failure with no Clerk prefix stays null.
        Assert.Null(user);
        Assert.Null(organization);
    }

    // ── Backwards compatibility ───────────────────────────────────────────────────────────

    [Fact]
    public void TheSingleArgumentOverloadStillResolvesGuidClaims()
    {
        var userId = Guid.CreateVersion7();
        var organizationId = Guid.CreateVersion7();
        var principal = Authenticated(
            new Claim("user_id", userId.ToString()),
            new Claim("org_id", organizationId.ToString()));

        var (organization, key, user) = RequestPrincipal.Resolve(principal);

        Assert.Equal(userId, user);
        Assert.Equal(organizationId, organization);
        Assert.Null(key);
    }

    // ── The map itself ────────────────────────────────────────────────────────────────────

    [Fact]
    public void ReplaceIsAtomicForReaders()
    {
        var map = new ClaimIdentityMap();
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();

        map.Replace(
            new Dictionary<string, Guid> { [ClerkUserId] = first }.ToFrozenDictionary(StringComparer.Ordinal),
            FrozenDictionary<string, Guid>.Empty);

        Assert.Equal(first, map.ResolveUserId(ClerkUserId));

        map.Replace(
            new Dictionary<string, Guid> { [ClerkUserId] = second }.ToFrozenDictionary(StringComparer.Ordinal),
            FrozenDictionary<string, Guid>.Empty);

        Assert.Equal(second, map.ResolveUserId(ClerkUserId));
    }

    [Fact]
    public void ResolveReturnsNullForNullOrEmptyInputWithoutCounting()
    {
        var map = Map();

        Assert.Null(map.ResolveUserId(null));
        Assert.Null(map.ResolveUserId(""));
        Assert.Null(map.ResolveOrganizationId(null));
        Assert.Null(map.ResolveOrganizationId(""));
        Assert.Equal(0, map.UnresolvedCount);
    }
}
