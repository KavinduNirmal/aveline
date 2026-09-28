using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Payments.Domain;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.Payments.Services;

/// <summary>
/// The durable half of plan §6.6's intent-expiry row. Until Phase 7 the read derived <c>Expired</c>
/// from <c>ExpiresAt &lt; now</c>; this sweep writes the state once so the row, the client poll and
/// reconciliation all agree without each re-deriving it.
/// </summary>
public interface IPaymentIntentExpiryService
{
    /// <summary>
    /// Marks every unsettled intent past its <c>ExpiresAt</c> as <c>Expired</c> and returns how many
    /// rows moved. Safe to run repeatedly: an already-terminal row is not a candidate.
    /// </summary>
    Task<int> SweepAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// The sweep's rule, in one place: an intent whose expiry is in the past, which has never settled,
/// and which is still waiting on the customer, becomes <c>Expired</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>What is deliberately excluded.</b> A <c>Succeeded</c> intent is settled money and an expiry
/// timestamp is not a reason to take it back; <c>Failed</c>, <c>Cancelled</c> and <c>Expired</c> are
/// already terminal. <c>SettledAt == null</c> is restated even though the status set already implies
/// it, because it is the invariant the rule is about and the check constraint ties the two together.
/// </para>
/// <para>
/// <b>No resurrection.</b> The sweep only ever writes the terminal <c>Expired</c> state, and the
/// settlement path refuses to settle a terminal intent (<c>SettleSucceededAsync</c>'s state guard),
/// so a late provider webhook cannot bring the charge back to life. That is asserted by
/// <c>PaymentIntentExpirySweepTests.ASweptIntent_IsNotSettledByALateSucceededWebhook</c>.
/// </para>
/// <para>
/// The clock is the injected <see cref="TimeProvider"/> rather than <c>DateTime.UtcNow</c>, so the
/// boundary is testable without waiting.
/// </para>
/// </remarks>
public sealed class PaymentIntentExpiryService(AppDbContext db, TimeProvider clock)
    : IPaymentIntentExpiryService
{
    public async Task<int> SweepAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;

        var expiring = await db.PaymentIntents
            .Where(intent => intent.ExpiresAt != null && intent.ExpiresAt <= now)
            .Where(intent => intent.SettledAt == null)
            .Where(intent => intent.Status == PaymentProviderStatus.RequiresAction
                          || intent.Status == PaymentProviderStatus.Processing)
            .ToListAsync(cancellationToken);

        foreach (var intent in expiring)
        {
            intent.Status = PaymentProviderStatus.Expired;
            intent.UpdatedAt = now;
        }

        if (expiring.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return expiring.Count;
    }
}
