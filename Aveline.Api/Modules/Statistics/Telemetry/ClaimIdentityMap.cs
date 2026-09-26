using System.Collections.Frozen;

namespace Aveline.Api.Modules.Statistics.Telemetry;

/// <summary>
/// Maps the opaque external ids a Clerk token carries onto the GUIDs Aveline stores.
/// A token's <c>user_id</c>/<c>sub</c> is Clerk's <c>user_…</c> and its <c>org_id</c> is
/// <c>org_…</c>; the canonical ids are <c>Users.Id</c> and <c>Organizations.Id</c>.
/// </summary>
/// <remarks>
/// A synchronous, allocation-free read: <see cref="ApiTelemetryMiddleware"/> runs on the
/// response path and must not perform I/O (FR-6.3). The contents are refreshed by
/// <see cref="ClaimIdentityMapRefresher"/>, never by a request.
/// </remarks>
public interface IClaimIdentityMap
{
    /// <summary>Resolves a Clerk user id (<c>user_…</c>) to the Aveline user GUID.</summary>
    Guid? ResolveUserId(string? clerkId);

    /// <summary>Resolves a Clerk organization id (<c>org_…</c>) to the Aveline organization GUID.</summary>
    Guid? ResolveOrganizationId(string? clerkOrgId);

    /// <summary>
    /// Requests whose Clerk id was present but absent from the map. This is the undercount
    /// signal: it is exposed on the active-users data-quality block rather than silently
    /// producing a low DAU.
    /// </summary>
    long UnresolvedCount { get; }
}

/// <summary>
/// The process-local implementation. Two <see cref="FrozenDictionary{TKey,TValue}"/>s are
/// swapped whole, so a reader never observes a half-built map and no lock is taken on the
/// request path.
/// </summary>
public sealed class ClaimIdentityMap : IClaimIdentityMap
{
    private FrozenDictionary<string, Guid> _users = FrozenDictionary<string, Guid>.Empty;
    private FrozenDictionary<string, Guid> _organizations = FrozenDictionary<string, Guid>.Empty;
    private long _unresolved;

    public long UnresolvedCount => Interlocked.Read(ref _unresolved);

    public Guid? ResolveUserId(string? clerkId)
    {
        // Only a Clerk-shaped id is a map lookup. A malformed Guid claim must stay null rather
        // than accidentally matching a dictionary key.
        if (!IsClerkShape(clerkId, "user_"))
        {
            return null;
        }

        if (_users.TryGetValue(clerkId!, out var id))
        {
            return id;
        }

        // Present but unknown: an undercount, not a gap.
        Interlocked.Increment(ref _unresolved);
        return null;
    }

    public Guid? ResolveOrganizationId(string? clerkOrgId)
    {
        if (!IsClerkShape(clerkOrgId, "org_"))
        {
            return null;
        }

        return _organizations.TryGetValue(clerkOrgId!, out var id) ? id : null;
    }

    private static bool IsClerkShape(string? value, string prefix) =>
        !string.IsNullOrEmpty(value)
        && value.StartsWith(prefix, StringComparison.Ordinal)
        && value.Length > prefix.Length;

    /// <summary>
    /// Replaces both maps in one write. Called only by <see cref="ClaimIdentityMapRefresher"/>
    /// after a successful full read, so a failed refresh leaves the previous contents intact.
    /// </summary>
    internal void Replace(
        FrozenDictionary<string, Guid> users,
        FrozenDictionary<string, Guid> organizations)
    {
        _users = users;
        _organizations = organizations;
    }
}
