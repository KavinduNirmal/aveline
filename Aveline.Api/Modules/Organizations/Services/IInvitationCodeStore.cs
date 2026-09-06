namespace Aveline.Api.Modules.Organizations.Services;

/// <summary>
/// Stores the transient mapping from a one-time invitation <c>code</c> to its invitation
/// record. Backed by the distributed cache (Redis in production, in-memory in dev/tests),
/// so the mapping auto-expires after a short TTL. The Postgres
/// <see cref="Repositories.IInvitationRepository"/> hash lookup remains as a fallback.
/// </summary>
public interface IInvitationCodeStore
{
    /// <summary>Persists <paramref name="invitationId"/> for <paramref name="code"/> until expiry.</summary>
    Task StoreAsync(string code, Guid invitationId, TimeSpan ttl, CancellationToken cancellationToken = default);

    /// <summary>Returns the invitation id for a code, or <c>null</c> if unknown/expired.</summary>
    Task<Guid?> GetAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>Removes a code (one-time redemption). Safe to call when absent.</summary>
    Task RemoveAsync(string code, CancellationToken cancellationToken = default);
}
