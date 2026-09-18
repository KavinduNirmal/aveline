namespace Aveline.Api.Modules.CustomerConcierge.DTOs;

/// <summary>One client in the tenant-facing book.</summary>
public sealed record CustomerBookItemDto(
    Guid CustomerId,
    string? FullName,
    string? Nickname,
    string? Level,
    string Status,
    string PhoneNumber,
    DateTime? LastVisitAtUtc,
    int VisitCount,
    decimal TotalSpent);

/// <summary>
/// The book's page envelope. One call returns the whole narrowing (the alphabet
/// index has to reach every letter it offers), so <c>pageSize</c> defaults high.
/// </summary>
public sealed record CustomerBookResponseDto(
    IReadOnlyList<CustomerBookItemDto> Items,
    int Total,
    int Page,
    int PageSize);

/// <summary>
/// A client worth surfacing on Home, with the reason they are in the row.
/// </summary>
/// <remarks>
/// There is deliberately no read/unread flag: the schema has no per-user marker,
/// and a dot that can never clear is worse than no dot (decision D2(a)).
/// </remarks>
public sealed record CustomerHighlightDto(
    Guid CustomerId,
    string Name,
    string? Level,
    string Activity,
    DateTime? LastActivityAtUtc);

public sealed record CustomerHighlightsResponseDto(IReadOnlyList<CustomerHighlightDto> Items);

/// <summary>A walk-in created at the counter. A name is enough to start.</summary>
public sealed record CreateWalkInCustomerRequest(
    string FullName,
    string? PhoneNumber,
    string? Nickname,
    string? Source);

public sealed record CustomerCreatedDto(
    Guid CustomerId,
    string? FullName,
    string? Level,
    string Status,
    string ConsentStatus,
    DateTime CreatedAtUtc,
    Guid? DuplicateOfCustomerId);

/// <summary>
/// Records a counter interaction. A visit is an inbound in-person interaction; a
/// message on another channel is recorded but does not count as one.
/// </summary>
public sealed record RecordCustomerInteractionRequest(
    DateTime OccurredAtUtc,
    string Channel,
    string? Direction,
    string? Note,
    decimal? PurchaseTotal);

/// <summary>
/// The result of recording an interaction, including the counters after it.
/// </summary>
/// <remarks>
/// <c>blossomsCharged</c> is always <c>0</c>: consumption is an
/// <c>AiUsageRecord</c> written after a completed agent workflow, and no rule in
/// the product debits a Blossom for a customer visit. The field is returned so the
/// client cannot invent a charge later.
/// </remarks>
public sealed record VisitReceiptDto(
    Guid VisitId,
    Guid CustomerId,
    DateTime OccurredAtUtc,
    string Channel,
    bool CountedAsVisit,
    int VisitCountAfter,
    DateTime? LastVisitAtUtcAfter,
    string TierAfter,
    decimal BlossomsCharged);
