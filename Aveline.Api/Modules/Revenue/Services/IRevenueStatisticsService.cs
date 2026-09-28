using Aveline.Api.Modules.Revenue.DTOs;

namespace Aveline.Api.Modules.Revenue.Services;

/// <summary>A half-open UTC window. `From` is inclusive, `To` is exclusive.</summary>
public sealed record RevenueWindow(DateTime From, DateTime To);

/// <summary>The ledger register's query: a window plus its page.</summary>
public sealed record RevenueLedgerQuery(RevenueWindow Window, int Page, int PageSize);

/// <summary>
/// The outcome of a validated read: either a value, or the message the caller should be told.
/// </summary>
/// <remarks>
/// Modelled on the business-KPI family's `Build(...)`-returns-`(Query?, IResult?)` shape, but as a
/// value the service can hand back so validation and computation stay one call and the endpoint
/// layer carries no rules.
/// </remarks>
public readonly record struct RevenueResult<T>(T? Value, string? Message)
{
    public bool IsValid => Message is null;

    public static RevenueResult<T> Ok(T value) => new(value, null);

    public static RevenueResult<T> Invalid(string message) => new(default, message);
}

/// <summary>The revenue read surface (S-50…S-55).</summary>
public interface IRevenueStatisticsService
{
    /// <summary>S-50: the paged income ledger, with the window's totals and reconciliation on it.</summary>
    Task<RevenueResult<IncomeLedgerPageDto>> GetLedgerAsync(
        RevenueLedgerQuery query, CancellationToken cancellationToken = default);

    /// <summary>S-51: per-organization derived, verified and net revenue over a window.</summary>
    Task<RevenueResult<RevenueAccountsDto>> GetAccountsAsync(
        RevenueWindow window, CancellationToken cancellationToken = default);

    /// <summary>S-52: MRR, ARR, ARPU and the paying-organization count.</summary>
    Task<RevenueResult<RevenueOverviewDto>> GetOverviewAsync(
        RevenueWindow window, CancellationToken cancellationToken = default);

    /// <summary>S-53: derived, verified and refunded per bucket.</summary>
    Task<RevenueResult<RevenueTimeseriesDto>> GetTimeseriesAsync(
        RevenueWindow window, string? granularity, CancellationToken cancellationToken = default);

    /// <summary>S-54: collection rate per period.</summary>
    Task<RevenueResult<RevenueCollectionsDto>> GetCollectionsAsync(
        RevenueWindow window, string? granularity, CancellationToken cancellationToken = default);

    /// <summary>S-55: Blossom top-up pack sales.</summary>
    Task<RevenueResult<RevenueBlossomSalesDto>> GetBlossomSalesAsync(
        RevenueWindow window, string? granularity, CancellationToken cancellationToken = default);
}
