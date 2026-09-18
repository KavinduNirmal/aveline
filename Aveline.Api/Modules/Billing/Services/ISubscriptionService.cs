using Aveline.Api.Modules.Billing.Models;

namespace Aveline.Api.Modules.Billing.Services;

public sealed record SubscriptionView(
    Guid OrganizationId,
    string PlanTier,
    string BillingCycle,
    string Status,
    DateTime CurrentPeriodStart,
    DateTime CurrentPeriodEnd,
    int SeatsIncluded,
    decimal PriceLkr,
    string Currency,
    bool CancelAtPeriodEnd,
    DateTime? CancelledAt,
    string? ExternalProvider);

public sealed record PlanLimitViolation(string Key, decimal Observed, decimal Allowed);

public sealed record ChangePlanCommand(
    Guid OrganizationId,
    PlanTier PlanTier,
    string? Effective,
    string? Reason,
    Guid? ActorUserId,
    string? IdempotencyKey,
    string? IdempotencyScope);

public sealed record PlanChangeResult(
    SubscriptionView Subscription, decimal BlossomDelta, decimal BlossomRemaining);

public sealed record EntitlementItemView(
    string Key, string ValueType, object? Value, string Source, DateTime EffectiveFrom);

public sealed record EntitlementUsageView(
    string Key, decimal Observed, decimal Allowed, decimal PercentUsed, bool HardLimit);

public sealed record EntitlementUsageResult(
    IReadOnlyList<EntitlementUsageView> Items, DateTime MaterialisedAt, bool MaterialisedCounts);

/// <summary>Subscription lifecycle, plan changes and entitlement reads (FR-2.5, FR-2.6).</summary>
public interface ISubscriptionService
{
    Task<SubscriptionView> GetSubscriptionAsync(
        Guid organizationId, CancellationToken cancellationToken = default);

    Task<PlanChangeResult> ChangePlanAsync(
        ChangePlanCommand command, CancellationToken cancellationToken = default);

    Task<SubscriptionView> CancelAsync(
        Guid organizationId, string? reason, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EntitlementItemView>> GetEntitlementsAsync(
        Guid organizationId, CancellationToken cancellationToken = default);

    Task<EntitlementUsageResult> GetEntitlementUsageAsync(
        Guid organizationId, CancellationToken cancellationToken = default);
}

/// <summary>The target plan cannot support current usage (409, <c>plan-limit-violation</c>).</summary>
public sealed class PlanLimitViolationException(IReadOnlyList<PlanLimitViolation> violations)
    : BlossomDomainException("The target plan's limits are exceeded by current usage.")
{
    public IReadOnlyList<PlanLimitViolation> Violations { get; } = violations;
}

/// <summary>The requested plan equals the current plan (400, <c>no-op-plan-change</c>).</summary>
public sealed class NoOpPlanChangeException()
    : BlossomDomainException("The organisation is already on the requested plan.");
