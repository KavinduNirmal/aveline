using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Infrastructure.Eventing;
using Aveline.Api.Modules.Audit.Models;
using Aveline.Api.Modules.Audit.Services;
using Aveline.Api.Modules.Billing.Domain;
using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.CustomerConcierge.Models;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Payments.Domain;
using Aveline.Api.Modules.Payments.Services;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.Billing.Services;

/// <summary>
/// Default <see cref="ISubscriptionService"/>. Plan changes are validated against the
/// entitlement catalog and applied through the ledger so the balance stays auditable.
/// </summary>
/// <remarks>
/// <para>
/// <b>The payment collaborators are optional on purpose.</b> They price and raise the
/// <c>SubscriptionProration</c> charge an immediate upgrade owes (plan §9.3 F3), which is a
/// follow-up obligation rather than part of the tier change: a deployment that has not wired the
/// payment module still changes plans, and the unit cases that only exercise pricing keep a
/// seven-argument construction.
/// </para>
/// </remarks>
public sealed class SubscriptionService(
    AppDbContext db,
    IEntitlementResolver entitlementResolver,
    IBlossomService blossomService,
    IEventBus eventBus,
    IAuditService auditService,
    ISubscriptionPriceResolver subscriptionPriceResolver,
    ILogger<SubscriptionService> logger,
    IProrationCalculator? prorationCalculator = null,
    IPaymentIntentService? paymentIntents = null,
    TimeProvider? clock = null,
    IPaymentProviderFactory? paymentProviders = null) : ISubscriptionService
{
    private const string BlossomsKey = UsageTrackerService.MonthlyBlossomsKey;
    private const string StaffKey = "staff.max";
    private const string CustomersKey = "customers.active.max";

    private TimeProvider Clock => clock ?? TimeProvider.System;

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

        // The price the tenant is leaving is read before the upsert re-prices the subscription for
        // the target tier (P1): it is the other half of the proration delta.
        var existingSubscription = await db.OrganizationSubscriptions
            .FirstOrDefaultAsync(s => s.OrganizationId == command.OrganizationId, cancellationToken);
        var previousPriceLkr = existingSubscription?.PriceLkr ?? 0m;

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

        // Plan §9.3 F3 / decision Q2: the tier, the price and the higher allowance are already
        // committed, and the charge follows as an invoice-like obligation. Nothing below may roll
        // the plan change back.
        var proration = effectiveImmediate && isUpgrade
            ? await TryCreateProrationChargeAsync(
                command, subscription, previousPriceLkr, periodEnd, cancellationToken)
            : ProrationCharge.None;

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
                prorationPaymentIntentId = proration.PaymentIntentId,
                prorationAmountLkr = proration.AmountLkr,
            },
            cancellationToken: cancellationToken);

        await auditService.RecordAsync(new AuditEntryRequest(
            Action: "org.plan.changed",
            EntityType: nameof(OrganizationSubscription),
            EntityId: subscription.Id.ToString(),
            OrganizationId: command.OrganizationId,
            ActorUserId: command.ActorUserId,
            Before: new { planTier = currentTier.ToString() },
            After: new
            {
                planTier = targetTier.ToString(),
                effective = effectiveImmediate,
                prorationPaymentIntentId = proration.PaymentIntentId,
                prorationAmountLkr = proration.AmountLkr,
            },
            Reason: command.Reason), cancellationToken);

        logger.LogInformation(
            "Plan changed. orgId={OrganizationId} from={FromTier} to={ToTier} immediate={Immediate} "
            + "delta={Delta} prorationIntentId={ProrationIntentId} prorationAmountLkr={ProrationAmount}",
            command.OrganizationId, currentTier, targetTier, effectiveImmediate, blossomDelta,
            proration.PaymentIntentId, proration.AmountLkr);

        return new PlanChangeResult(
            ToView(subscription), blossomDelta, remaining, proration.PaymentIntentId, proration.AmountLkr);
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

        // Plan §9.5(a): the provider is told **before** anything is persisted. A cancellation that
        // does not reach the provider is the worst outcome the service can have — the tenant keeps
        // being charged while Aveline believes the subscription is ending — so a refusal is a hard
        // failure (`502 payment-provider-error`) and no local cancellation is written.
        if (subscription is not null
            && !string.IsNullOrWhiteSpace(subscription.ExternalSubscriptionId))
        {
            var provider = ResolveSubscriptionProvider(subscription);
            await provider.CancelSubscriptionAsync(
                subscription.ExternalSubscriptionId, atPeriodEnd: true, cancellationToken);
        }

        var (periodStart, periodEnd) = CurrentPeriod();
        subscription ??= await UpsertSubscriptionAsync(
            organizationId, organization.PlanTier, periodStart, periodEnd, immediate: false, cancellationToken);

        subscription.CancelAtPeriodEnd = true;
        subscription.CancelledAt = Clock.GetUtcNow().UtcDateTime;
        subscription.UpdatedAt = Clock.GetUtcNow().UtcDateTime;

        await db.SaveChangesAsync(cancellationToken);
        return ToView(subscription);
    }

    /// <summary>
    /// The adapter that owns a subscription's provider-side agreement. The key stored on the row is
    /// used rather than the configured one, so an agreement created under one provider can still be
    /// cancelled after the configuration changes (decision D5's resolve-by-stored-key).
    /// </summary>
    private IPaymentProvider ResolveSubscriptionProvider(OrganizationSubscription subscription)
    {
        if (paymentProviders is null)
        {
            throw new PaymentProviderNotConfiguredException(
                "No payment provider is registered, so a provider-side subscription cannot be cancelled.");
        }

        return string.IsNullOrWhiteSpace(subscription.ExternalProvider)
            ? paymentProviders.Active
            : paymentProviders.Resolve(subscription.ExternalProvider);
    }

    /// <summary>
    /// Clears a scheduled cancellation (plan §9.5(c)). The local flag is only half the story: a
    /// provider-side recurring agreement that is still set to cancel at the period end must be
    /// restored too, and the SPI can only express that through
    /// <see cref="IPaymentProvider.CreateOrUpdateSubscriptionAsync"/>. A provider whose
    /// <c>SupportsCancelAtPeriodEnd</c> capability is false cannot be asked at all, so the request is
    /// refused with the documented <c>501 payment-provider-capability-missing</c> rather than
    /// clearing a flag the provider will still act on.
    /// </summary>
    public async Task<SubscriptionView> ResumeAsync(
        Guid organizationId, CancellationToken cancellationToken = default)
    {
        await GetOrganizationAsync(organizationId, cancellationToken);
        var subscription = await db.OrganizationSubscriptions
            .FirstOrDefaultAsync(s => s.OrganizationId == organizationId, cancellationToken);

        if (subscription is null || !subscription.CancelAtPeriodEnd)
        {
            throw new BlossomValidationException("The subscription is not scheduled for cancellation.");
        }

        // Plan §9.5(c): the un-cancel path is only meaningful where the provider can be asked. A
        // provider without `SupportsCancelAtPeriodEnd` cannot withdraw its own scheduled
        // cancellation, so clearing the local flag alone would leave the tenant being cancelled
        // anyway; the request is refused instead with the documented 501.
        var provider = ResolveSubscriptionProvider(subscription);
        if (!provider.Capabilities.SupportsCancelAtPeriodEnd)
        {
            throw new PaymentProviderNotSupportedException(
                $"The payment provider '{provider.Key}' cannot withdraw a scheduled cancellation.");
        }

        if (!string.IsNullOrWhiteSpace(subscription.ExternalSubscriptionId))
        {
            // The SPI has no dedicated "resume" call, so the recurring agreement is re-established
            // through the create-or-update contract. The id is the existing agreement's, so this is
            // an update rather than a second subscription.
            await provider.CreateOrUpdateSubscriptionAsync(
                new CreateProviderSubscriptionRequest(
                    subscription.ExternalSubscriptionId,
                    organizationId.ToString(),
                    new Money(
                        (long)decimal.Round(
                            subscription.PriceLkr * 100m, 0, MidpointRounding.AwayFromZero),
                        "LKR"),
                    subscription.BillingCycle == BillingCycle.Annual ? "year" : "month",
                    $"resume:{subscription.OrganizationId:N}:{subscription.CurrentPeriodStart:o}",
                    AllowedPaymentMethodTypes: null),
                cancellationToken);
        }

        subscription.CancelAtPeriodEnd = false;
        subscription.CancelledAt = null;
        subscription.UpdatedAt = Clock.GetUtcNow().UtcDateTime;

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Subscription cancellation withdrawn. orgId={OrganizationId} tier={Tier}",
            organizationId, subscription.PlanTier);

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

    /// <summary>The proration charge a plan change left behind, if any (plan §9.3 F3).</summary>
    /// <param name="PaymentIntentId">The created intent, or null when none could be created.</param>
    /// <param name="AmountLkr">The amount the charge was struck for, or null when nothing is owed.</param>
    private sealed record ProrationCharge(Guid? PaymentIntentId, decimal? AmountLkr)
    {
        public static ProrationCharge None { get; } = new(null, null);
    }

    /// <summary>
    /// Creates the invoice-like <c>SubscriptionProration</c> charge an immediate upgrade owes
    /// (plan §9.3 F3). The provider prices it where its <c>SupportsProration</c> capability says it
    /// can; otherwise <see cref="Domain.PlanChangeProration.Local"/> does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why a failure here still returns a result.</b> Decision Q2 keeps the higher allowance
    /// immediate: the tier, the price and the Blossom delta are already committed by the caller, so
    /// a provider that cannot be reached leaves the charge outstanding rather than reverting a plan
    /// the tenant is already consuming. The amount is still reported, so the obligation is visible
    /// in the response, the event, and the audit row.
    /// </para>
    /// <para>
    /// <see cref="PaymentProviderNotSupportedException"/> is deliberately not caught: a provider
    /// that advertises <c>SupportsProration</c> without implementing
    /// <see cref="IProrationProvider"/> is an adapter contract hole, and the documented
    /// <c>501 payment-provider-capability-missing</c> is surfaced rather than swallowed.
    /// </para>
    /// </remarks>
    private async Task<ProrationCharge> TryCreateProrationChargeAsync(
        ChangePlanCommand command,
        OrganizationSubscription subscription,
        decimal previousPriceLkr,
        DateTime periodEnd,
        CancellationToken cancellationToken)
    {
        if (prorationCalculator is null || paymentIntents is null)
        {
            return ProrationCharge.None;
        }

        var newPriceLkr = subscription.PriceLkr;
        if (newPriceLkr <= previousPriceLkr)
        {
            // The resolved price did not rise, so there is nothing to prorate. This is also the
            // whole of the downgrade rule: no money moves.
            return ProrationCharge.None;
        }

        var at = Clock.GetUtcNow().UtcDateTime;
        var request = new ProrationRequest(
            previousPriceLkr, newPriceLkr, DateOnly.FromDateTime(periodEnd), DateOnly.FromDateTime(at));

        ProrationQuote quote;
        try
        {
            quote = prorationCalculator.Compute(request);
        }
        catch (PaymentProviderNotSupportedException)
        {
            throw;
        }
        catch (PaymentDomainException exception)
        {
            logger.LogWarning(
                exception,
                "The proration charge for a plan change could not be priced; the plan change stands "
                + "and the obligation is unrecorded. orgId={OrganizationId} previousPriceLkr={Previous} "
                + "newPriceLkr={New} errorCode={ErrorCode}",
                command.OrganizationId, previousPriceLkr, newPriceLkr, exception.ErrorCode);
            return ProrationCharge.None;
        }

        if (quote.AmountLkr <= 0m)
        {
            // A change struck on the period's last day owes nothing, and Money refuses a
            // non-positive charge. There is no obligation to record.
            return ProrationCharge.None;
        }

        try
        {
            var intent = await paymentIntents.CreateAsync(
                new CreatePaymentIntentCommand(
                    command.OrganizationId,
                    PaymentPurpose.SubscriptionProration,
                    Money.Lkr(quote.AmountLkr),
                    $"Plan change to {subscription.PlanTier} prorated for the remainder of the period.",
                    SkuCode: null,
                    BlossomQuantity: null,
                    IdempotencyKey: ProrationKey(command, subscription),
                    CreatedByUserId: command.ActorUserId,
                    BillingPeriodStart: subscription.CurrentPeriodStart,
                    BillingPeriodEnd: periodEnd,
                    PlanTier: subscription.PlanTier,
                    BillingCycle: subscription.BillingCycle.ToString()),
                cancellationToken);

            logger.LogInformation(
                "Plan change proration charge created. orgId={OrganizationId} intentId={IntentId} "
                + "amountLkr={Amount} source={Source} previousPriceLkr={Previous} newPriceLkr={New}",
                command.OrganizationId, intent.PaymentIntentId, quote.AmountLkr, quote.Source,
                previousPriceLkr, newPriceLkr);

            return new ProrationCharge(intent.PaymentIntentId, quote.AmountLkr);
        }
        catch (PaymentProviderNotSupportedException)
        {
            throw;
        }
        catch (PaymentDomainException exception)
        {
            logger.LogWarning(
                exception,
                "The proration charge for a plan change could not be created; the plan change stands "
                + "and the charge remains outstanding. orgId={OrganizationId} amountLkr={Amount} "
                + "errorCode={ErrorCode}",
                command.OrganizationId, quote.AmountLkr, exception.ErrorCode);

            return new ProrationCharge(null, quote.AmountLkr);
        }
    }

    /// <summary>
    /// Deterministic per organisation, period and target tier, so a retried change that carries no
    /// client <c>Idempotency-Key</c> still resolves to the charge it already created instead of
    /// making a second one. The client's own key is prefixed rather than reused so a plan-change
    /// replay and a differently-purposed charge can never share an identity.
    /// </summary>
    private static string ProrationKey(ChangePlanCommand command, OrganizationSubscription subscription) =>
        string.IsNullOrWhiteSpace(command.IdempotencyKey)
            ? $"proration:{command.OrganizationId:N}:"
              + $"{subscription.CurrentPeriodStart:o}:{subscription.PlanTier}"
            : $"proration:{command.IdempotencyKey}";

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

        // Seed is free by definition, whatever the price book happens to contain. Every other tier
        // is priced from the plan-allowance book (G9, FR-1.11). A missing row resolves to null and
        // is deliberately **not** coerced to zero: the column is left at its existing value (zero
        // for a new subscription), so an unpriced plan writes no `Derived` charge — exactly how
        // every subscription behaved before this slice — while a real zero-priced row is a free plan.
        if (tier == PlanTier.Seed)
        {
            subscription.PriceLkr = 0m;
        }
        else if (await subscriptionPriceResolver.ResolveAsync(
                     organizationId, tier, subscription.BillingCycle, at: null, cancellationToken)
                 is { } resolvedPriceLkr)
        {
            subscription.PriceLkr = resolvedPriceLkr;
        }

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

    private (DateTime PeriodStart, DateTime PeriodEnd) CurrentPeriod()
    {
        var now = Clock.GetUtcNow().UtcDateTime;
        var start = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        return (start, start.AddMonths(1));
    }
}
