namespace Aveline.Api.Modules.Billing.Domain;

/// <summary>
/// Aveline's own mid-cycle proration, used when the active payment provider cannot prorate
/// (plan §9.3 F3 and §8.5: the flag exists "rather than a hardcoded local calculation").
/// </summary>
/// <remarks>
/// <para>
/// <b>The formula.</b> For an upgrade from <c>previous</c> to <c>next</c> LKR per month:
/// </para>
/// <code>
/// remainingDays = clamp(periodEnd - at, 0, daysInMonth(at))
/// amount        = round((next - previous) * remainingDays / daysInMonth(at), 2, AwayFromZero)
/// </code>
/// <para>
/// <c>daysInMonth</c> is the calendar month of the proration date, so a February proration bills
/// over 28 days (29 in a leap year). The day count is a plain date difference, so the period-end
/// day itself is a zero-day remainder and is charged nothing, and a date past the period end is
/// clamped to zero rather than producing a credit. The result is LKR to two decimal places (the
/// <c>numeric(18,2)</c> shape the ledger stores), and <c>AwayFromZero</c> is the rounding mode
/// <c>Money.Lkr</c> uses, so a half-cent cannot round down here and up at the provider boundary.
/// </para>
/// <para>
/// A change that does not raise the price &mdash; a downgrade, or an upgrade to a plan whose
/// resolved price is lower &mdash; is <c>0.00</c>. No money moves for a downgrade, which is the
/// plan's §9.3 rule for both the <c>nextPeriod</c> default and a forced immediate change.
/// </para>
/// </remarks>
public static class PlanChangeProration
{
    /// <summary>
    /// The local fallback proration for a mid-cycle plan change, in LKR. Never negative, and
    /// <c>0.00</c> whenever the new price is not greater than the previous one.
    /// </summary>
    public static decimal Local(
        decimal previousMonthlyPriceLkr, decimal newMonthlyPriceLkr, DateOnly periodEnd, DateOnly at)
    {
        var difference = newMonthlyPriceLkr - previousMonthlyPriceLkr;
        if (difference <= 0m)
        {
            return 0m;
        }

        var daysInMonth = DateTime.DaysInMonth(at.Year, at.Month);
        var remainingDays = Math.Clamp(periodEnd.DayNumber - at.DayNumber, 0, daysInMonth);

        return decimal.Round(
            difference * remainingDays / daysInMonth, 2, MidpointRounding.AwayFromZero);
    }
}
