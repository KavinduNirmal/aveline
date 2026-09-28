using Aveline.Api.Modules.Billing.Services;

namespace Aveline.Api.Modules.Billing.DTOs;

/// <summary>
/// One metered allowance: what is used, what the plan allows, and what is left.
/// </summary>
/// <param name="Key">The entitlement key the limit came from (<c>staff.max</c>, <c>customers.active.max</c>).</param>
/// <param name="Used">The observed consumption right now.</param>
/// <param name="Limit">The resolved entitlement limit for this organisation's plan.</param>
/// <param name="Remaining">What is left, floored at zero: being over a limit is not negative room.</param>
/// <param name="PercentUsed">Consumption as a percentage of the limit, or zero when the limit is zero.</param>
/// <param name="IsHardLimit">
/// Whether exceeding the limit is blocked (staff, customers) or merely metered (Blossoms).
/// </param>
public sealed record TenantAllowanceDto(
    string Key,
    decimal Used,
    decimal Limit,
    decimal Remaining,
    decimal PercentUsed,
    bool IsHardLimit);

/// <summary>
/// A single organisation's own account position: its Blossom balance and its standing against the
/// plan's staff and customer limits.
/// </summary>
/// <remarks>
/// <para>
/// Built for the agent service, so Aveline can answer "how many Blossoms do I have left?" with the
/// same numbers the boutique sees in its own dashboard. That agreement is the reason the Blossom
/// half is a <see cref="BlossomBalanceDto"/>: it is the projection the Blossom meter already
/// renders (<c>GET /orgs/{organizationId}/blossoms/balance</c>), so a second, divergent balance
/// formula cannot appear here.
/// </para>
/// <para>
/// <see cref="Blossoms"/>'s <c>PlanTier</c> is the billing period's tier snapshot, not
/// necessarily the live subscription tier. It is carried unchanged from the balance projection
/// rather than re-derived, so this endpoint stays a read of existing services.
/// </para>
/// </remarks>
/// <param name="OrganizationId">The owning organisation (tenant scope).</param>
/// <param name="Blossoms">The authoritative balance projection for the current period.</param>
/// <param name="Staff">Seat allowance against <c>staff.max</c>.</param>
/// <param name="Customers">Customer allowance against <c>customers.active.max</c>.</param>
/// <param name="CustomerCountBasis">
/// How <see cref="Customers"/>'s <c>Used</c> was counted. Carried as data so an answer can say
/// "active customers" rather than implying a total the count does not represent.
/// </param>
/// <param name="AsOf">When the snapshot was read.</param>
public sealed record TenantUsageSnapshotDto(
    Guid OrganizationId,
    BlossomBalanceDto Blossoms,
    TenantAllowanceDto Staff,
    TenantAllowanceDto Customers,
    string CustomerCountBasis,
    bool BlossomsAreLow,
    DateTime AsOf)
{
    /// <summary>
    /// The basis for the customer count, mirroring the window
    /// <c>SubscriptionService.GetEntitlementUsageAsync</c> filters on.
    /// </summary>
    public const string ActiveCustomerBasis =
        "customers active in the last 90 days (a customer interaction, an order, or a profile update)";

    /// <summary>
    /// Whether the Blossom balance has reached the low-water line.
    /// </summary>
    /// <remarks>
    /// The rule is the one the clients already apply
    /// (<c>frontend/aveline_mobile/lib/features/home/domain/blossom_usage.dart</c>): remaining at or
    /// below the threshold's share of the period allowance, where the allowance is the monthly
    /// limit plus grants less adjustments - not the bare limit, because a top-up raises what
    /// "20% left" means. It is judged here rather than in the agent so the agent carries no billing
    /// arithmetic of its own.
    /// </remarks>
    public static bool IsLowBalance(BlossomBalance balance, decimal lowBalanceThresholdPercent)
    {
        var allowance = balance.MonthlyBlossomLimit + balance.BlossomGranted - balance.BlossomAdjusted;
        return allowance > 0m && balance.BlossomRemaining <= allowance * lowBalanceThresholdPercent / 100m;
    }

    /// <summary>
    /// Project one entitlement usage row onto an allowance, assembling the remaining amount here
    /// because the entitlement layer reports observed-versus-allowed rather than a remainder.
    /// </summary>
    public static TenantAllowanceDto FromEntitlement(EntitlementUsageView view) => new(
        view.Key,
        view.Observed,
        view.Allowed,
        view.Allowed <= 0 ? 0m : Math.Max(0m, view.Allowed - view.Observed),
        view.PercentUsed,
        view.HardLimit);

    /// <summary>
    /// Compose the snapshot from the two services that already own the numbers.
    /// </summary>
    /// <param name="organizationId">The organisation being read.</param>
    /// <param name="balance">The authoritative balance, from <c>IBlossomService.GetBalanceAsync</c>.</param>
    /// <param name="usage">Entitlement usage, from <c>ISubscriptionService.GetEntitlementUsageAsync</c>.</param>
    /// <param name="lowBalanceThresholdPercent">The configured low-water mark, as the meter uses it.</param>
    /// <param name="asOf">The read time.</param>
    public static TenantUsageSnapshotDto From(
        Guid organizationId,
        BlossomBalance balance,
        EntitlementUsageResult usage,
        decimal lowBalanceThresholdPercent,
        DateTime asOf) => new(
        organizationId,
        BlossomBalanceDto.From(balance, lowBalanceThresholdPercent),
        FromEntitlement(Require(usage, "staff.max")),
        FromEntitlement(Require(usage, "customers.active.max")),
        ActiveCustomerBasis,
        IsLowBalance(balance, lowBalanceThresholdPercent),
        asOf);

    /// <summary>
    /// The one entitlement row with <paramref name="key"/>, failing loudly when it is absent.
    /// </summary>
    /// <remarks>
    /// A missing key means the entitlement catalog changed without this projection following, and
    /// an absent seat count reported as zero would be a wrong answer rather than a missing one.
    /// </remarks>
    private static EntitlementUsageView Require(
        EntitlementUsageResult usage, string key) =>
        usage.Items.FirstOrDefault(item => item.Key == key)
        ?? throw new InvalidOperationException(
            $"Entitlement usage did not report '{key}'; the snapshot cannot be built.");
}
