using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.CustomerConcierge.Models;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.Privacy.Services;

/// <summary>
/// A customer row found by normalized phone, across every organisation. The privacy flow needs the
/// organisation and the customer id only; it never reads a name, an email or message content.
/// </summary>
/// <param name="OrganizationId">The boutique that holds the row.</param>
/// <param name="CustomerId">The boutique's customer id.</param>
public sealed record PhoneSubject(Guid OrganizationId, Guid CustomerId);

/// <summary>
/// The identity-by-phone lookup behind a global opt-out (plan §5.4, Option 1). It is a separate
/// interface in this module on purpose: the cross-organisation read is a privacy-flow concern, and
/// widening <c>ICustomerRepository</c> would put an untenant-scoped query on the surface every other
/// caller uses.
/// </summary>
public interface IPhoneSubjectLocator
{
    /// <summary>
    /// Returns every active customer row whose phone matches <paramref name="phoneE164"/>, in any
    /// organisation. Empty when the number is unknown, which the caller must not distinguish from a
    /// known one on the wire.
    /// </summary>
    Task<IReadOnlyList<PhoneSubject>> FindAllByPhoneAsync(
        string phoneE164, CancellationToken cancellationToken = default);
}

/// <summary>EF implementation of <see cref="IPhoneSubjectLocator"/>.</summary>
public sealed class PhoneSubjectLocator : IPhoneSubjectLocator
{
    private readonly AppDbContext _db;

    public PhoneSubjectLocator(AppDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<IReadOnlyList<PhoneSubject>> FindAllByPhoneAsync(
        string phoneE164, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneE164);

        // The comparison is on the exact stored string, which is the same contract
        // CustomerRepository.GetByPhoneAsync uses, so only rows the phone identifies are returned.
        // There is no EF global tenant filter (R-17), so "every organisation" is expressed by the
        // absence of an organization predicate rather than by a filter that does not exist. The
        // soft-delete filter still applies, matching every other customer read.
        return await _db.Customers
            .Where(c => c.PhoneNumber == phoneE164)
            .Select(c => new PhoneSubject(c.OrganizationId, c.Id))
            .ToListAsync(cancellationToken);
    }
}

/// <summary>
/// The revoker behind both customer surfaces (plan §5.4). One method expresses the whole decision,
/// so an org-scoped opt-out and a global opt-out cannot drift.
/// </summary>
public interface IConsentRevoker
{
    /// <summary>
    /// Revokes consent for the customer (or every customer row the phone identifies) and writes the
    /// consent audit and the audit-log row for each affected organisation.
    /// </summary>
    Task<ConsentRevocationResult> RevokeAsync(
        ConsentRevocationRequest request, CancellationToken cancellationToken = default);
}

/// <summary>One revocation request. <see cref="Scope"/> is the only thing that changes the blast radius.</summary>
/// <param name="OrganizationId">The organisation the request arrived for.</param>
/// <param name="CustomerId">The customer row in that organisation, when one is known.</param>
/// <param name="PhoneE164">
/// The normalised number. Required for <see cref="ConsentRevocationScope.All"/> and for identifying
/// the customer when <paramref name="CustomerId"/> is null.
/// </param>
/// <param name="Scope"><c>org</c> (exactly this organisation) or <c>all</c> (every organisation).</param>
/// <param name="Actor">Who is acting, for the audit trail.</param>
/// <param name="Reason">A bounded, non-personal reason for the audit entry.</param>
public sealed record ConsentRevocationRequest(
    Guid OrganizationId,
    Guid? CustomerId,
    string? PhoneE164,
    ConsentRevocationScope Scope,
    ConsentActor Actor,
    string? Reason = null);

/// <summary>The blast radius of a revocation.</summary>
public enum ConsentRevocationScope
{
    /// <summary>Exactly one organisation's row.</summary>
    Org,

    /// <summary>Every organisation's row for the same phone (plan §5.4, Option 1).</summary>
    All,
}

/// <summary>Who is revoking, and how the audit row records them.</summary>
/// <param name="Kind">
/// One of <see cref="ConsentActorKinds"/>. The staff path and the customer path must differ, which
/// is the whole point of having two endpoints.
/// </param>
/// <param name="Source">One of <see cref="ConsentSources"/>, stored on the consent row.</param>
/// <param name="UserId">The staff user, when a person acted.</param>
/// <param name="ActorRef">A hashed phone or a clerk id; never a raw identifier.</param>
/// <param name="IpHash">SHA-256 of the client address; never the address.</param>
/// <param name="UserAgent">The client's user-agent, truncated by the audit service.</param>
public sealed record ConsentActor(
    string Kind,
    string Source,
    Guid? UserId = null,
    string? ActorRef = null,
    string? IpHash = null,
    string? UserAgent = null)
{
    /// <summary>No person acted: a system rule, a backfill or a test.</summary>
    public static ConsentActor System { get; } = new(ConsentActorKinds.System, ConsentSources.System);
}

/// <summary>The canonical actor kinds for <c>ConsentAuditEntry.ActorKind</c>.</summary>
public static class ConsentActorKinds
{
    /// <summary>A customer acting for themselves through the OTP flow.</summary>
    public const string Customer = "Customer";

    /// <summary>A staff member acting from the boutique console.</summary>
    public const string User = "User";

    /// <summary>No person acted.</summary>
    public const string System = "System";

    /// <summary>The agent service acted through the internal surface.</summary>
    public const string InternalService = "InternalService";
}

/// <summary>The canonical <c>ConsentSource</c> values.</summary>
public static class ConsentSources
{
    /// <summary>The OTP-verified opt-out flow.</summary>
    public const string OtpLink = "otp_link";

    /// <summary>A staff member, from the console or the agent service.</summary>
    public const string Staff = "staff";

    /// <summary>A direct API caller with no human in the loop.</summary>
    public const string Api = "api";

    /// <summary>A system rule, such as a retention sweep.</summary>
    public const string System = "system";
}

/// <summary>What one revocation did.</summary>
/// <param name="RowsRevoked">How many consent rows now read <c>revoked</c>.</param>
/// <param name="OrganizationsAffected">The distinct organisations touched.</param>
/// <param name="EffectiveAtUtc">The single instant stamped on every row.</param>
/// <param name="Targets">The (organisation, customer) pairs the scope resolved to.</param>
public sealed record ConsentRevocationResult(
    int RowsRevoked,
    IReadOnlyList<Guid> OrganizationsAffected,
    DateTime EffectiveAtUtc,
    IReadOnlyList<PhoneSubject> Targets)
{
    /// <summary>The wire form of the scope.</summary>
    public static string ScopeName(ConsentRevocationScope scope) =>
        scope == ConsentRevocationScope.All ? "all" : "org";

    /// <summary>
    /// The customer id the acknowledgement is attributed to: the issuing organisation's row when one
    /// exists, otherwise the first row the scope touched. Null when nothing was revoked - there is
    /// nobody to acknowledge.
    /// </summary>
    public Guid? AcknowledgementCustomerId(Guid organizationId)
        => Targets.FirstOrDefault(t => t.OrganizationId == organizationId)?.CustomerId
           ?? Targets.FirstOrDefault()?.CustomerId;
}
