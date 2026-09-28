namespace Aveline.Api.Modules.Billing.Domain;

/// <summary>
/// The dunning schedule for a failed subscription renewal (payment plan §9.4 F4, decision Q3).
/// </summary>
/// <remarks>
/// <para>
/// The accepted policy is three retries on days 1, 3 and 7 after the period boundary, with the
/// subscription <c>PastDue</c> throughout, and <c>Expired</c> on day 14. The rollover's own attempt
/// at the boundary is the attempt the retries follow, so a subscription that never settles makes
/// four charge attempts in total before the window closes.
/// </para>
/// <para>
/// The arithmetic lives here rather than in the job so the schedule is one testable fact, and the
/// job only asks "is it due?" and "is it over?" rather than re-encoding day counts.
/// </para>
/// </remarks>
public static class SubscriptionDunningSchedule
{
    /// <summary>Days after the failed boundary on which each retry is due.</summary>
    public static IReadOnlyList<int> RetryDays { get; } = [1, 3, 7];

    /// <summary>The day after the failed boundary on which the subscription expires.</summary>
    public const int ExpiryDay = 14;

    /// <summary>
    /// When the next retry is due, given how many attempts have already been made (the boundary
    /// attempt counts as one). <c>null</c> once every retry has been spent.
    /// </summary>
    public static DateTime? NextAttemptAt(DateTime dunningStartedAt, int attemptsMade)
    {
        var retryIndex = attemptsMade - 1;
        return retryIndex < 0 || retryIndex >= RetryDays.Count
            ? null
            : dunningStartedAt.AddDays(RetryDays[retryIndex]);
    }

    /// <summary>True from day 14 after the failed boundary: the subscription is <c>Expired</c>.</summary>
    public static bool IsExpired(DateTime dunningStartedAt, DateTime now) =>
        now >= dunningStartedAt.AddDays(ExpiryDay);
}
