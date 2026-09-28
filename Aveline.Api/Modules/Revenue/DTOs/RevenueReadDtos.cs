namespace Aveline.Api.Modules.Revenue.DTOs;

using Aveline.Api.Modules.Payments;

/// <summary>The window a revenue read was computed over, echoed so the client never re-derives it.</summary>
public sealed record RevenueWindowDto(
    DateTime From,
    DateTime To,
    string Granularity,
    string TimeZone,
    int BucketCount);

/// <summary>
/// The fifth data-quality vocabulary — money (S-50).
/// </summary>
/// <remarks>
/// Deliberately **not** a reuse of <c>BusinessDataQualityDto</c>: attribution and backfill say
/// nothing about whether a price was configured or whether a receipt was verified. The three facts
/// this exists to state, so no caller has to infer them:
///
/// - <see cref="RevenueProviderSettlementAvailable"/> is `false` only while the configured provider
///   is the `manual` adapter, which settles nothing by itself: **no figure here is settled money**
///   until an operator confirms it. It becomes `true` for a provider client that settles charges.
/// - <see cref="SubscriptionPricesConfigured"/> is `false` while `PriceLkr` is unassigned, so MRR is
///   `null` rather than a `0` that reads as "we earn nothing".
/// - <see cref="DerivedEntriesUnverified"/> is the count of billed-but-uncollected entries. That gap
///   is the most important number on the surface, and it is **not** an error.
/// </remarks>
public sealed record IncomeDataQualityDto(
    bool RevenueProviderSettlementAvailable,
    bool SubscriptionPricesConfigured,
    int DerivedEntriesUnverified,
    DateTime CheckedAt,
    IReadOnlyList<string> Notes)
{
    /// <summary>
    /// Every source answered and every measure is instrumented. The provider fact is read from the
    /// payment configuration rather than assumed, so the factory and the live read cannot disagree.
    /// </summary>
    public static IncomeDataQualityDto Clean(DateTime checkedAt, PaymentsOptions payments) => new(
        RevenueProviderSettlementAvailable: payments.ProviderSettlesMoney,
        SubscriptionPricesConfigured: true,
        DerivedEntriesUnverified: 0,
        CheckedAt: checkedAt,
        Notes: []);
}

/// <summary>Window totals and the reconciliation identity (S-50).</summary>
public sealed record RevenueReconciliationDto(
    decimal DerivedTotal,
    decimal VerifiedTotal,
    decimal UnverifiedGap,
    decimal RefundTotal,
    decimal NetVerified,
    bool IsBalanced);

/// <summary>One page of the income ledger register, with the window's totals on it.</summary>
public sealed record IncomeLedgerPageDto(
    RevenueWindowDto Window,
    IReadOnlyList<IncomeLedgerEntryDto> Items,
    int Total,
    int Page,
    int PageSize,
    RevenueReconciliationDto Reconciliation,
    IncomeDataQualityDto DataQuality);

/// <summary>One organization's revenue over a window (S-51).</summary>
public sealed record RevenueAccountItemDto(
    Guid OrganizationId,
    string Name,
    string PlanTier,
    decimal DerivedTotal,
    decimal VerifiedTotal,
    decimal RefundTotal,
    decimal NetVerified,
    decimal UnverifiedGap);

public sealed record RevenueAccountsDto(
    RevenueWindowDto Window,
    IReadOnlyList<RevenueAccountItemDto> Items,
    int Total,
    RevenueReconciliationDto Reconciliation,
    IncomeDataQualityDto DataQuality);

/// <summary>
/// MRR, ARR and ARPU (S-52).
/// </summary>
/// <remarks>
/// Every measure is nullable, and **`null` is not `0`**: it means the measure could not be computed.
/// A `PayingOrganizations` of `0` makes `Arpu` `null` rather than a divide-by-zero, and an
/// unassigned `PriceLkr` makes all three `null` with `subscriptionPricesConfigured: false`.
///
/// These are **list-price scheduled revenue**, not recognised or collected revenue. The response
/// states `revenueProviderSettlementAvailable`, and while that is `false` the console must not
/// label them "collected".
/// </remarks>
public sealed record RevenueOverviewDto(
    DateTime AsOf,
    decimal? Mrr,
    decimal? Arr,
    decimal? Arpu,
    int PayingOrganizations,
    int ActiveSubscriptions,
    IncomeDataQualityDto DataQuality);

/// <summary>One bucket of the revenue timeseries (S-53). Three separate series, never merged.</summary>
public sealed record RevenueTimeseriesPointDto(
    DateTime BucketStart,
    bool IsPartial,
    decimal Derived,
    decimal Verified,
    decimal Refunded);

public sealed record RevenueTimeseriesDto(
    RevenueWindowDto Window,
    IReadOnlyList<RevenueTimeseriesPointDto> Series,
    IncomeDataQualityDto DataQuality);

/// <summary>One period's collection rate (S-54).</summary>
public sealed record RevenueCollectionPointDto(
    DateTime BucketStart,
    bool IsPartial,
    decimal DerivedTotal,
    decimal VerifiedTotal,
    decimal RefundedTotal,
    /// <summary>Percentage, or `null` when nothing was billed. Never `0` for "no rate".</summary>
    decimal? CollectionRate,
    decimal Outstanding);

public sealed record RevenueCollectionsDto(
    RevenueWindowDto Window,
    IReadOnlyList<RevenueCollectionPointDto> Series,
    IncomeDataQualityDto DataQuality);

/// <summary>Blossom top-up pack sales over a window (S-55).</summary>
public sealed record RevenueBlossomSalesDto(
    RevenueWindowDto Window,
    /// <summary>Referenced purchases only. An unreferenced grant writes no income row at all.</summary>
    int PacksSold,
    decimal BlossomsGranted,
    decimal ListPriceLkr,
    decimal VerifiedLkr,
    /// <summary>Percentage of list price collected, or `null` when nothing was listed.</summary>
    decimal? Conversion,
    /// <summary>Grants with no payment reference, so the difference from `PacksSold` is visible.</summary>
    int GrantedWithoutReference,
    IncomeDataQualityDto DataQuality);
