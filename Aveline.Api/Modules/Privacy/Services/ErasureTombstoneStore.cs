using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.CustomerConcierge.Services;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.Privacy.Services;

/// <summary>
/// EF implementation of <see cref="IConsentTombstoneStore"/> over
/// <c>PrivacyErasureTombstones</c> (plan §15 Q-4). It answers only the existence question; the
/// terminal status (<c>revoked</c>) is fixed by the erasure that wrote the row.
/// </summary>
public sealed class ErasureTombstoneStore : IConsentTombstoneStore
{
    private readonly AppDbContext _db;

    public ErasureTombstoneStore(AppDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<bool> WasErasedAsync(
        Guid organizationId,
        string phoneE164,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phoneE164))
        {
            return false;
        }

        var phoneHash = PhoneFingerprint.Of(phoneE164);

        // Explicit organisation predicate: there is no EF global tenant filter (R-17), so the
        // tenant boundary is stated here rather than assumed.
        return await _db.PrivacyErasureTombstones
            .AnyAsync(
                t => t.OrganizationId == organizationId && t.PhoneHash == phoneHash,
                cancellationToken);
    }
}
