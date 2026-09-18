using Aveline.Api.Modules.CustomerConcierge.DTOs;
using Aveline.Api.Modules.CustomerConcierge.Models;
using Aveline.Api.Modules.CustomerConcierge.Repositories;

namespace Aveline.Api.Modules.CustomerConcierge.Services;

/// <summary>
/// Deterministic loyalty/tier progression. The pure <see cref="RecommendStatus"/> rule is
/// data-driven (no hidden state) so it is trivially unit-testable; <see cref="RecomputeAsync"/>
/// loads a customer, derives the tier and persists it (or applies an explicit owner override).
/// </summary>
public class CustomerLoyaltyService(ICustomerRepository customers) : ICustomerLoyaltyService
{
    public const decimal ReturningSpendThreshold = 10000m;
    public const decimal VipSpendThreshold = 50000m;
    public const int VipMinVisits = 5;
    public const int ReturningMinVisits = 2;
    public static readonly TimeSpan DormancyAfter = TimeSpan.FromDays(90);

    public const string StatusNew = "new";
    public const string StatusReturning = "returning";
    public const string StatusVip = "vip";
    public const string StatusDormant = "dormant";

    /// <summary>The statuses an owner may set explicitly (never 'deleted').</summary>
    public static readonly IReadOnlySet<string> OverridableStatuses = new HashSet<string>(
        [StatusNew, StatusReturning, StatusVip, StatusDormant]);

    public async Task<CustomerStatusDto?> RecomputeAsync(
        Guid orgId,
        Guid customerId,
        string? overrideStatus = null,
        CancellationToken cancellationToken = default)
    {
        var customer = await customers.GetAsync(orgId, customerId, cancellationToken);
        if (customer is null)
        {
            return null;
        }

        if (overrideStatus is not null)
        {
            if (!OverridableStatuses.Contains(overrideStatus))
            {
                throw new ArgumentException(
                    $"Invalid customer status '{overrideStatus}'. Allowed: {string.Join(", ", OverridableStatuses)}.",
                    nameof(overrideStatus));
            }

            customer.Status = overrideStatus;
        }
        else
        {
            customer.Status = RecommendStatus(
                customer.TotalSpent, customer.VisitCount, customer.LastVisitAt, DateTime.UtcNow);
        }

        await customers.SaveAsync(customer, cancellationToken);
        return new CustomerStatusDto(customer.Id, customer.Status);
    }

    /// <summary>
    /// Derives a customer tier from spend/visits/last-visit. A customer is <c>dormant</c> once
    /// inactive for <see cref="DormancyAfter"/> (no visit within the window counts as never
    /// active -> not dormant). Otherwise the tier upgrades with activity/spend.
    /// </summary>
    public static string RecommendStatus(
        decimal totalSpent,
        int visitCount,
        DateTime? lastVisitAt,
        DateTime utcNow)
    {
        if (lastVisitAt is { } last && utcNow - last >= DormancyAfter)
        {
            return StatusDormant;
        }

        if (totalSpent > VipSpendThreshold && visitCount >= VipMinVisits)
        {
            return StatusVip;
        }

        if (totalSpent > ReturningSpendThreshold || visitCount >= ReturningMinVisits)
        {
            return StatusReturning;
        }

        return StatusNew;
    }
}
