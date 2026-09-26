namespace Aveline.Api.Modules.Commerce.DTOs;

/// <summary>
/// The tenant dashboard's summary for one window.
/// </summary>
/// <remarks>
/// Deliberately flat, and **every absent number is `null`, never `0`**. A KPI strip that renders
/// `LKR 0` for a figure the server could not compute is stating a fact about the shop that nobody
/// established; `0` is a measurement and `null` is the absence of one.
/// </remarks>
public sealed record TenantDashboardSummaryDto(
    string Window,
    DateTime WindowFrom,
    DateTime WindowTo,
    DateTime GeneratedAt,
    string Currency,
    TenantSalesKpisDto Sales,
    TenantCashKpisDto Cash,
    TenantCustomerKpisDto Customers,
    TenantCatalogKpisDto Catalog,
    TenantTeamKpisDto Team,
    TenantUsageKpisDto Usage,
    TenantOperationsKpisDto Operations,
    TenantDashboardDataQualityDto DataQuality)
{
    /// <summary>Set when the request was refused, so the endpoint can map it to a 400.</summary>
    public string? InvalidReason { get; init; }

    public static TenantDashboardSummaryDto Invalid(string reason) => new(
        Window: string.Empty,
        WindowFrom: default,
        WindowTo: default,
        GeneratedAt: DateTime.UtcNow,
        Currency: "LKR",
        Sales: TenantSalesKpisDto.Empty,
        Cash: TenantCashKpisDto.Empty,
        Customers: TenantCustomerKpisDto.Empty,
        Catalog: TenantCatalogKpisDto.Empty,
        Team: TenantTeamKpisDto.Empty,
        Usage: TenantUsageKpisDto.Empty,
        Operations: TenantOperationsKpisDto.Empty,
        DataQuality: TenantDashboardDataQualityDto.Empty)
    {
        InvalidReason = reason,
    };
}

/// <summary>
/// Sales KPIs. Billed value, not cash: these come from orders, which say what was sold rather than
/// what was collected.
/// </summary>
public sealed record TenantSalesKpisDto(
    decimal? GrossOrderValue,
    int OrderCount,
    decimal? AverageOrderValue,
    decimal? DiscountTotal,
    decimal? MarginAmount,
    decimal? MarginPercent)
{
    /// <summary>
    /// False when any contributing order's line items carry a zero `WholesaleCost`. That cost is
    /// caller-supplied rather than read from the inventory record, so a margin built on it is only
    /// as trustworthy as what somebody typed — and the flag is how a reader finds out.
    /// </summary>
    public bool MarginCostsComplete { get; init; }

    public static TenantSalesKpisDto Empty => new(null, 0, null, null, null, null);
}

/// <summary>Cash KPIs, kept as three separate figures so they can never be conflated.</summary>
public sealed record TenantCashKpisDto(
    decimal? Collected,
    decimal? Outstanding,
    decimal? Refunded,
    int RefundCount)
{
    public static TenantCashKpisDto Empty => new(null, null, null, 0);
}

public sealed record TenantCustomerKpisDto(
    int ActiveCount,
    int TotalCount,
    int NewInWindow,
    int RepeatCount,
    int InactivityThresholdDays)
{
    public static TenantCustomerKpisDto Empty => new(0, 0, 0, 0, 90);
}

public sealed record TenantCatalogKpisDto(
    int ItemCount,
    int LowStockCount,
    int OutOfStockCount,
    decimal? StockValueAtCost)
{
    public static TenantCatalogKpisDto Empty => new(0, 0, 0, null);
}

public sealed record TenantTeamKpisDto(
    int ActiveSeats,
    int AllowedSeats,
    int PendingInvitations,
    IReadOnlyDictionary<string, int> RoleBreakdown)
{
    public static TenantTeamKpisDto Empty =>
        new(0, 0, 0, new Dictionary<string, int>());
}

public sealed record TenantUsageKpisDto(
    decimal? BlossomUsed,
    decimal? MonthlyBlossomLimit,
    decimal? BlossomRemaining,
    decimal? PercentUsed)
{
    public static TenantUsageKpisDto Empty => new(null, null, null, null);
}

/// <summary>
/// Operational counts. Each reuses the predicate its owning module already uses, so two screens
/// cannot disagree about the same number.
/// </summary>
public sealed record TenantOperationsKpisDto(
    int PendingApprovals,
    int OpenConversations,
    int ScheduledDeliveries)
{
    public static TenantOperationsKpisDto Empty => new(0, 0, 0);
}

/// <summary>
/// The tenant dashboard's own data-quality vocabulary — the seventh in the API, named field by field
/// rather than reused from the agent, home-feed, business or revenue families.
/// </summary>
public sealed record TenantDashboardDataQualityDto(
    bool PaymentRowsPresent,
    bool OrderCostsComplete,
    bool IncomeLedgerBackfilled,
    bool UsageMetricsAvailable,
    bool RefundAndOutstandingExcludedFromCollected,
    DateTime CheckedAt,
    IReadOnlyList<string> Notes)
{
    public static TenantDashboardDataQualityDto Empty => new(
        false, false, false, false, true, DateTime.UtcNow, []);

    public TenantDashboardDataQualityDto WithNote(string note) =>
        this with { Notes = [.. Notes, note] };
}

/// <summary>
/// The **reduced takings read** (E-13) — what every boutique role may see.
/// </summary>
/// <remarks>
/// It carries **exactly** two money figures and no more: `Collected` (verified money minus refunds)
/// and `BilledUnconfirmed` (derived billed value). There is deliberately no margin, no per-customer
/// or per-operator split, no ledger row and no series — those stay behind `reports:view` on the full
/// summary. The two-basis rule survives the reduction, so there is never one unlabelled earnings
/// number even here.
/// </remarks>
public sealed record TenantTakingsDto(
    string Window,
    DateTime WindowFrom,
    DateTime WindowTo,
    DateTime GeneratedAt,
    string Currency,
    decimal? Collected,
    decimal? BilledUnconfirmed,
    bool PaymentRowsPresent,
    bool LedgerBackfilled,
    TenantDashboardDataQualityDto DataQuality)
{
    public string? InvalidReason { get; init; }

    public static TenantTakingsDto Invalid(string reason) => new(
        string.Empty, default, default, DateTime.UtcNow, "LKR", null, null, false, false,
        TenantDashboardDataQualityDto.Empty)
    {
        InvalidReason = reason,
    };
}

/// <summary>One dense bucket of the revenue series.</summary>
public sealed record TenantRevenueBucketDto(
    DateTime BucketStart,
    decimal? GrossOrderValue,
    decimal? Collected,
    decimal? Refunded,
    bool IsPartial);

/// <summary>
/// The revenue series (E-2), dense over the window.
/// </summary>
/// <remarks>
/// A bucket with no orders carries `null` values rather than `0`, so the chart can render a gap as a
/// gap: `connectNulls` must be false, because a line drawn straight through a missing measurement
/// reports a value the server never produced.
/// </remarks>
public sealed record TenantRevenueSeriesDto(
    string Bucket,
    DateTime WindowFrom,
    DateTime WindowTo,
    bool WindowCapped,
    IReadOnlyList<TenantRevenueBucketDto> Points)
{
    public string? InvalidReason { get; init; }

    public static TenantRevenueSeriesDto Invalid(string reason) => new(
        string.Empty, default, default, false, [])
    {
        InvalidReason = reason,
    };
}

/// <summary>One best-selling piece in the window.</summary>
public sealed record TenantTopItemDto(
    string ItemName,
    int Quantity,
    decimal? Revenue);

public sealed record TenantTopItemsDto(
    DateTime WindowFrom,
    DateTime WindowTo,
    int Limit,
    IReadOnlyList<TenantTopItemDto> Items)
{
    public string? InvalidReason { get; init; }

    public static TenantTopItemsDto Invalid(string reason) => new(
        default, default, 0, [])
    {
        InvalidReason = reason,
    };
}
