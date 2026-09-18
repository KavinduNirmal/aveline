using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Infrastructure.Eventing;
using Aveline.Api.Modules.Audit.Models;
using Aveline.Api.Modules.Audit.Services;
using Aveline.Api.Modules.Billing.Domain;
using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.CustomerConcierge.Models;
using Aveline.Api.Modules.Organizations.Models;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.Billing.Services;

/// <summary>
/// Default <see cref="ISubscriptionService"/>. Plan changes are validated against the
/// entitlement catalog and applied through the ledger so the balance stays auditable.
/// </summary>
public sealed class SubscriptionService(
    AppDbContext db,
    IEntitlementResolver entitlementResolver,
    IBlossomService blossomService,
    IEventBus eventBus,
    IAuditService auditService,
    ILogger<SubscriptionService> logger) : ISubscriptionService
{
    private const string BlossomsKey = UsageTrackerService.MonthlyBlossomsKey;
    private const string StaffKey = "staff.max";
    private const string CustomersKey = "customers.active.max";

    public async Task<SubscriptionView> GetSubscriptionAsync(
        Guid organizationId, CancellationToken cancellationToken = default)
    {
        var organization = await GetOrganizationAsync(organizationId, cancellationToken);
        var subscription = await db.OrganizationSubscriptions
            .FirstOrDefaultAsync(s => s.OrganizationId == organizationId, cancellationToken);

        if (subscription is null)
        {
            var (periodStart, periodEnd) = CurrentPeriod();
            var seats = (int)await entitlementResolver.GetDecimalAsync(
                organizationId, StaffKey, 1m, at: null, cancellationToken);

            return new SubscriptionView(
                organizationId,
                organization.PlanTier.ToString(),
                BillingCycle.Monthly.ToString(),
                "None",
                periodStart,
                periodEnd,
                seats,
                0m,
                "LKR",
                false,
                null,
                null);
        }

        return ToView(subscription);
    }

    public async Task<PlanChangeResult> ChangePlanAsync(
        ChangePlanCommand command, CancellationToken cancellationToken = default)
    {
        var organization = await GetOrganizationAsync(command.OrganizationId, cancellationToken);
        var currentTier = organization.PlanTier;
        var targetTier = command.PlanTier;

        if (currentTier == targetTier)
        {
            throw new NoOpPlanChangeException();
        }

        var isUpgrade = targetTier > currentTier;
        var effectiveImmediate = string.Equals(
            command.Effective ?? (isUpgrade ? "immediate" : "nextPeriod"),
            "immediate",
            StringComparison.OrdinalIgnoreCase);

        var currentLimit = await entitlementResolver.GetDecimalAsync(
            command.OrganizationId, BlossomsKey, 150m, at: null, cancellationToken);
        var targetLimit = await ResolveTierLimitAsync(
            command.OrganizationId, targetTier, currentLimit, cancellationToken);

        if (!isUpgrade && effectiveImmediate)
        {
            var violations = await EvaluateDowngradeAsync(
                command.OrganizationId, targetTier, targetLimit, cancellationToken);
            if (violations.Count > 0)
            {
                throw new PlanLimitViolationException(violations);
            }
        }

        var (periodStart, periodEnd) = CurrentPeriod();
        var subscription = await UpsertSubscriptionAsync(
            command.OrganizationId, targetTier, periodStart, periodEnd, effectiveImmediate, cancellationToken);

        var blossomDelta = 0m;
        var remaining = 0m;

        if (effectiveImmediate)
        {
            organization.PlanTier = targetTier;
            organization.UpdatedAt = DateTime.UtcNow;

            blossomDelta = targetLimit - currentLimit;
            var entryType = blossomDelta > 0
                ? BlossomLedgerEntryType.PlanUpgradeProration
                : BlossomLedgerEntryType.PlanDowngradeAdjustment;

            var reason = command.Reason
                ?? $"Plan changed from {currentTier} to {targetTier} for the current period.";

            await blossomService.ApplyPlanChangeAsync(new ApplyPlanChangeCommand(
                command.OrganizationId,
                blossomDelta,
                entryType,
                reason,
                command.ActorUserId,
                command.IdempotencyKey,
                command.IdempotencyScope), cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);

        var balance = await blossomService.GetBalanceAsync(command.OrganizationId, cancellationToken);
        remaining = balance.BlossomRemaining;

        await eventBus.PublishAsync(
            "org.plan.changed",
            command.OrganizationId,
            new
            {
                fromTier = currentTier.ToString(),
                toTier = targetTier.ToString(),
                effective = effectiveImmediate ? "immediate" : "nextPeriod",
                blossomDelta,
            },
            cancellationToken: cancellationToken);

        await auditService.RecordAsync(new AuditEntryRequest(
            Action: "org.plan.changed",
            EntityType: nameof(OrganizationSubscription),
            EntityId: subscription.Id.ToString(),
            OrganizationId: command.OrganizationId,
            ActorUserId: command.ActorUserId,
            Before: new { planTier = currentTier.ToString() },
            After: new { planTier = targetTier.ToString(), effective = effectiveImmediate },
            Reason: command.Reason), cancellationToken);

        logger.LogInformation(
            "Plan changed. orgId={OrganizationId} from={FromTier} to={ToTier} immediate={Immediate} delta={Delta}",
            command.OrganizationId, currentTier, targetTier, effectiveImmediate, blossomDelta);

        return new PlanChangeResult(ToView(subscription), blossomDelta, remaining);
    }

    public async Task<SubscriptionView> CancelAsync(
        Guid organizationId, string? reason, CancellationToken cancellationToken = default)
    {
        var organization = await GetOrganizationAsync(organizationId, cancellationToken);
        var subscription = await db.OrganizationSubscriptions
            .FirstOrDefaultAsync(s => s.OrganizationId == organizationId, cancellationToken);

        if (subscription is not null && subscription.CancelAtPeriodEnd)
        {
            throw new BlossomValidationException("The subscription is already scheduled for cancellation.");
        }

        var (periodStart, periodEnd) = CurrentPeriod();
        subscription ??= await UpsertSubscriptionAsync(
            organizationId, organization.PlanTier, periodStart, periodEnd, immediate: false, cancellationToken);

        subscription.CancelAtPeriodEnd = true;
        subscription.CancelledAt = DateTime.UtcNow;
        subscription.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return ToView(subscription);
    }

    public async Task<IReadOnlyList<EntitlementItemView>> GetEntitlementsAsync(
        Guid organizationId, CancellationToken cancellationToken = default)
    {
        await GetOrganizationAsync(organizationId, cancellationToken);
        var resolved = await entitlementResolver.GetAllAsync(organizationId, at: null, cancellationToken);

        return resolved.Values
            .OrderBy(value => value.Key, StringComparer.Ordinal)
            .Select(value => new EntitlementItemView(
                value.Key,
                value.ValueType.ToString(),
                value.ValueType switch
                {
                    EntitlementValueType.Boolean => value.Flag,
                    EntitlementValueType.String => value.Text,
                    _ => value.Number,
                },
                value.Source,
                value.EffectiveFrom))
            .ToArray();
    }

    public async Task<EntitlementUsageResult> GetEntitlementUsageAsync(
        Guid organizationId, CancellationToken cancellationToken = default)
    {
        var organization = await GetOrganizationAsync(organizationId, cancellationToken);

        var balance = await blossomService.GetBalanceAsync(organizationId, cancellationToken);
        var staffCount = await db.OrganizationMemberships
            .CountAsync(m => m.OrganizationId == organizationId && m.Status == MembershipStatus.Active,
                cancellationToken);
        var cutoff = DateTime.UtcNow.AddDays(-90);
        var customerCount = await db.CustomerInteractions
            .Where(i => i.OrganizationId == organizationId && i.CreatedAt >= cutoff)
            .Select(i => i.CustomerId)
            .Union(db.Orders.Where(o => o.OrganizationId == organizationId && o.CreatedAt >= cutoff).Select(o => o.CustomerId))
            .Union(db.Customers.Where(c => c.OrganizationId == organizationId && (c.UpdatedAt >= cutoff || c.CreatedAt >= cutoff)).Select(c => c.Id))
            .Distinct()
            .CountAsync(cancellationToken);

        var blossomAllowed = await entitlementResolver.GetDecimalAsync(
            organizationId, BlossomsKey, balance.MonthlyBlossomLimit, at: null, cancellationToken);
        var staffAllowed = await entitlementResolver.GetDecimalAsync(
            organizationId, StaffKey, 1m, at: null, cancellationToken);
        var customerAllowed = await entitlementResolver.GetDecimalAsync(
            organizationId, CustomersKey, 0m, at: null, cancellationToken);

        return new EntitlementUsageResult(
            [
                Build(BlossomsKey, balance.BlossomUsed, blossomAllowed, hardLimit: false),
                Build(StaffKey, staffCount, staffAllowed, hardLimit: true),
                Build(CustomersKey, customerCount, customerAllowed, hardLimit: true),
            ],
            DateTime.UtcNow,
            MaterialisedCounts: true);
    }

    private static EntitlementUsageView Build(string key, decimal observed, decimal allowed, bool hardLimit) =>
        new(
            key,
            observed,
            allowed,
            allowed <= 0 ? 0m : Math.Round(observed / allowed * 100m, 2),
            hardLimit);

    private async Task<decimal> ResolveTierLimitAsync(
        Guid organizationId, PlanTier tier, decimal fallback, CancellationToken cancellationToken)
    {
        // A per-organisation override replaces the tier value whatever the tier (BR-2.15),
        // so it also defines the effective allowance during a plan change (M-20).
        var resolved = await entitlementResolver.GetAsync(
            organizationId, BlossomsKey, at: null, cancellationToken);

        if (resolved is { Number: { } overrideValue }
            && string.Equals(resolved.Source, "Override", StringComparison.Ordinal))
        {
            return overrideValue;
        }

        // Otherwise resolve the target tier through the resolver (database rows beat the
        // in-memory catalog) rather than reading the catalog directly.
        return await entitlementResolver.GetTierDecimalAsync(
            tier, BlossomsKey, fallback, at: null, cancellationToken);
    }

    private async Task<IReadOnlyList<PlanLimitViolation>> EvaluateDowngradeAsync(
        Guid organizationId, PlanTier targetTier, decimal targetLimit, CancellationToken cancellationToken)
    {
        var violations = new List<PlanLimitViolation>();

        var balance = await blossomService.GetBalanceAsync(organizationId, cancellationToken);
        if (balance.BlossomUsed > targetLimit)
        {
            violations.Add(new PlanLimitViolation(BlossomsKey, balance.BlossomUsed, targetLimit));
        }

        var targetStaff = PlanEntitlementDefaults.For(targetTier).TryGetValue(StaffKey, out var staffEntitlement)
            ? staffEntitlement.Number ?? 0m
            : 0m;
        var staffCount = await db.OrganizationMemberships
            .CountAsync(m => m.OrganizationId == organizationId && m.Status == MembershipStatus.Active,
                cancellationToken);
        if (staffCount > targetStaff)
        {
            violations.Add(new PlanLimitViolation(StaffKey, staffCount, targetStaff));
        }

        var targetCustomers = PlanEntitlementDefaults.For(targetTier)
            .TryGetValue(CustomersKey, out var customerEntitlement)
                ? customerEntitlement.Number ?? 0m
                : 0m;
        var customerCount = await db.Customers
            .CountAsync(c => c.OrganizationId == organizationId, cancellationToken);
        if (customerCount > targetCustomers)
        {
            violations.Add(new PlanLimitViolation(CustomersKey, customerCount, targetCustomers));
        }

        return violations;
    }

    private async Task<OrganizationSubscription> UpsertSubscriptionAsync(
        Guid organizationId, PlanTier tier, DateTime periodStart, DateTime periodEnd,
        bool immediate, CancellationToken cancellationToken)
    {
        var subscription = await db.OrganizationSubscriptions
            .FirstOrDefaultAsync(s => s.OrganizationId == organizationId, cancellationToken);

        var seats = (int)(PlanEntitlementDefaults.For(tier).TryGetValue(StaffKey, out var staff)
            ? staff.Number ?? 1m
            : 1m);

        if (subscription is null)
        {
            subscription = new OrganizationSubscription
            {
                OrganizationId = organizationId,
                CurrentPeriodStart = periodStart,
                CurrentPeriodEnd = periodEnd,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };
            db.OrganizationSubscriptions.Add(subscription);
        }

        subscription.PlanTier = tier;
        subscription.SeatsIncluded = seats;
        subscription.Status = SubscriptionStatus.Active;
        subscription.CurrentPeriodStart = periodStart;
        subscription.CurrentPeriodEnd = periodEnd;
        subscription.UpdatedAt = DateTime.UtcNow;

        // For a next-period change the tier is recorded here and applied at rollover; the
        // organisation's live tier is only changed when the change is immediate (the
        // immediate flag is handled by the caller).
        return subscription;
    }

    private async Task<Modules.Organizations.Models.Organization> GetOrganizationAsync(
        Guid organizationId, CancellationToken cancellationToken)
    {
        var organization = await db.Organizations
            .FirstOrDefaultAsync(o => o.Id == organizationId, cancellationToken);

        return organization
            ?? throw new BlossomOrganizationNotFoundException(organizationId);
    }

    private static SubscriptionView ToView(OrganizationSubscription subscription) => new(
        subscription.OrganizationId,
        subscription.PlanTier.ToString(),
        subscription.BillingCycle.ToString(),
        subscription.Status.ToString(),
        subscription.CurrentPeriodStart,
        subscription.CurrentPeriodEnd,
        subscription.SeatsIncluded,
        subscription.PriceLkr,
        "LKR",
        subscription.CancelAtPeriodEnd,
        subscription.CancelledAt,
        subscription.ExternalProvider);

    private static (DateTime PeriodStart, DateTime PeriodEnd) CurrentPeriod()
    {
        var now = DateTime.UtcNow;
        var start = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        return (start, start.AddMonths(1));
    }
}
