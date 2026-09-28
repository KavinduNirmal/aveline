namespace Aveline.Api.Modules.Home.DTOs;

/// <summary>One docket on Home's focus deck.</summary>
/// <remarks>
/// Shaped to keep <c>FocusTask</c> as the client's wire type (features/home/README.md):
/// the ask, its context, when it is due and the verb that clears it. <c>sourceKey</c>
/// names the underlying fact so a dismissal survives the feed being recomputed, and
/// <c>contentHash</c> binds that dismissal to the content that was actually decided.
/// </remarks>
public sealed record HomeFeedItemDto(
    string Id,
    string SourceKey,
    string Domain,
    string Title,
    string Detail,
    DateTime? DueAtUtc,
    string? TimeLabel,
    string ActionLabel,
    string DoneMessage,
    string ContentHash,
    HomeFeedCapsDto Caps);

/// <summary>What the caller may do with a docket.</summary>
public sealed record HomeFeedCapsDto(bool CanComplete, bool CanAssign);

/// <summary>The organization-local day the feed was derived for.</summary>
public sealed record HomeFeedWindowDto(string LocalDate, string TimeZone);

/// <summary>
/// Which sources were readable and non-empty, so an absent domain is explained
/// rather than mistaken for a measured zero.
/// </summary>
public sealed record HomeFeedDataQualityDto(
    bool WardrobeAvailable,
    bool PatronAvailable,
    bool CommerceAvailable,
    bool LogisticsAvailable);

/// <summary>Authoritative counts of the same filter set the items came from.</summary>
public sealed record HomeFeedCountsDto(
    int Total,
    int Overdue,
    IReadOnlyDictionary<string, int> ByDomain);

public sealed record HomeFeedDto(
    DateTime GeneratedAt,
    HomeFeedWindowDto Window,
    HomeFeedDataQualityDto DataQuality,
    IReadOnlyList<HomeFeedItemDto> Items,
    HomeFeedCountsDto Counts);

/// <summary>
/// A human decision about one derived docket. Idempotent by nature: dismissing
/// twice is the same end state.
/// </summary>
public sealed record DismissFocusTaskRequest(
    string SourceKey,
    string Domain,
    string Decision,
    string? ContentHash,
    string? Note);

public sealed record FocusDismissalResponseDto(
    Guid DismissalId,
    string SourceKey,
    string Domain,
    string Decision,
    DateTime DismissedAtUtc,
    HomeFeedCountsDto Counts);
