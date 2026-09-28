namespace Aveline.Api.Modules.Billing.DTOs;

/// <summary>
/// E-11 · One purchasable Blossom top-up pack, as offered by the tenant Billing section.
/// </summary>
/// <remarks>
/// The list exists so the top-up dialog cannot hardcode a SKU: it is built from exactly the same
/// effective price-book lookup the purchase route uses
/// (<c>IPricingService.ListPriceEntriesAsync(TopUpPack, planTier: null, organizationId: null)</c>
/// filtered to <c>Status == Active</c>), so a pack the catalogue shows cannot be rejected at
/// purchase and a pack the purchase accepts cannot be missing from the list.
///
/// <c>PriceLkr</c> is the published list price from the price book, in LKR — the price book stores
/// LKR and there is no currency column to read. No payment provider is connected, so purchasing a
/// pack records a <b>grant</b>, never a charge (D8).
/// </remarks>
public sealed record TopUpPackDto(
    string SkuCode,
    decimal BlossomQuantity,
    decimal PriceLkr,
    string Currency);

/// <summary>
/// E-12 · One billing period as the tenant may read it: the period's Blossom account, its plan at
/// the time, and the top-ups that landed in it.
/// </summary>
/// <remarks>
/// Two fields exist for the honesty rule the sibling admin ledger established (C-4):
/// <c>OrganizationSubscription.PriceLkr</c> (and the snapshot's copy of it) is **never assigned**
/// anywhere in the product, so it is permanently the column default <c>0m</c>. "No row ⇒ null, else
/// the column" would therefore print <c>LKR 0</c> as a plan price for every existing subscription.
/// Instead <see cref="PlanListPriceLkr"/> is <c>null</c> whenever the stored price is zero, and
/// <see cref="SubscriptionPricesConfigured"/> says whether a real price was configured — so
/// <c>null</c> reads as "not configured", never as "free".
///
/// <see cref="PlanTier"/> and <see cref="HasSubscriptionRow"/> come from the day's
/// <c>OrganizationSubscriptionSnapshot</c> (or, for the current period, the subscription row), not
/// from <c>Organization.PlanTier</c>: an organization always has a plan tier, but it may never have
/// had a billing row, and reporting the former as the latter would invent a subscription.
/// </remarks>
public sealed record BillingPeriodDto(
    DateTime PeriodStart,
    DateTime PeriodEnd,
    bool IsClosed,
    string? PlanTier,
    bool HasSubscriptionRow,
    decimal MonthlyBlossomLimit,
    decimal BlossomGranted,
    decimal BlossomAdjusted,
    decimal BlossomUsed,
    decimal BlossomRemaining,
    decimal? PlanListPriceLkr,
    bool SubscriptionPricesConfigured,
    decimal TopUpBlossoms,
    int TopUpCount);
