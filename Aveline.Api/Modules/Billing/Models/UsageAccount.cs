using System.Text.Json.Serialization;

namespace Aveline.Api.Modules.Billing.Models;

/// <summary>
/// The status of a usage account / billing period.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum UsageAccountStatus
{
    /// <summary>The account is active and accepting AI requests.</summary>
    Active,

    /// <summary>The account has been suspended (e.g. payment failure — future enforcement).</summary>
    Suspended,

    /// <summary>The account has been permanently closed.</summary>
    Closed,
}

/// <summary>
/// A mutable ledger row representing an organisation's Blossom balance for one billing period.
/// </summary>
/// <remarks>
/// One active row exists per organisation per billing period.
/// <c>BlossomUsed</c> is incremented atomically as part of the same transaction that
/// inserts an <see cref="AiUsageRecord"/>. This allows O(1) balance reads without
/// aggregating the full usage record log.
///
/// Enforcement (blocking requests when <c>BlossomRemaining</c> reaches zero) is deferred
/// to the subscription/payment slice. See ADR-010.
/// </remarks>
public class UsageAccount
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; set; }

    /// <summary>UTC start of the billing period (inclusive).</summary>
    public DateTime PeriodStart { get; set; }

    /// <summary>UTC end of the billing period (exclusive).</summary>
    public DateTime PeriodEnd { get; set; }

    /// <summary>
    /// The monthly Blossom allowance copied from the organisation's plan at the time
    /// this period was created. Stored here so historical periods remain accurate even
    /// if the plan changes.
    /// </summary>
    public decimal MonthlyBlossomLimit { get; set; }

    /// <summary>Running total of Blossoms consumed in this period.</summary>
    public decimal BlossomUsed { get; set; }

    /// <summary>
    /// Cached remaining balance: <c>MonthlyBlossomLimit - BlossomUsed</c>.
    /// Updated on every usage record write. May temporarily be negative if the limit
    /// is not yet enforced.
    /// </summary>
    public decimal BlossomRemaining { get; set; }

    /// <summary>
    /// Count of active customers at last update.
    /// An active customer is one who has had an interaction, order, or profile update
    /// within the last 90 days (see ADR-010). Not enforced in this slice.
    /// </summary>
    public int ActiveCustomerCount { get; set; }

    /// <summary>Count of active staff members at last update.</summary>
    public int StaffCount { get; set; }

    public UsageAccountStatus Status { get; set; } = UsageAccountStatus.Active;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
