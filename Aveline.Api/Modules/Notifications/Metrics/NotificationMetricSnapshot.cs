using Aveline.Api.Modules.Notifications.Models;

namespace Aveline.Api.Modules.Notifications.Metrics;

/// <summary>
/// The notification metric family (Slice 7, plan §6.5). Computed as one pure function over rows so
/// the four constraints are testable without a database:
/// <list type="number">
/// <item><c>InboxBacklog</c> counts work items a user has not acted upon — it is NOT S-36's
/// <c>notification_backlog</c>, which counts delivery rows with <c>Status = Pending</c>.</item>
/// <item>The dispatcher increments the delivery counter; nothing is derived at scrape time.</item>
/// <item><c>FcmCredentialConfigured</c> is a gauge, because a missing credential makes push a
/// silent no-op.</item>
/// <item><c>TimeToReadP50Minutes</c> is <c>null</c> below
/// <c>Telemetry:MinSampleForPercentile</c>, and a null gauge is omitted rather than zeroed.</item>
/// </list>
/// </summary>
public sealed record NotificationMetricSnapshot(
    bool FcmCredentialConfigured,
    IReadOnlyDictionary<string, double> DeliverySuccessRateByChannel,
    IReadOnlyDictionary<string, double> InboxReadRateByType,
    IReadOnlyDictionary<string, double> DismissRateByType,
    double? TimeToReadP50Minutes,
    IReadOnlyDictionary<string, long> FailureReasonsByChannelAndClass,
    IReadOnlyDictionary<string, long> VolumeByType,
    long InboxBacklog,
    IReadOnlyDictionary<string, long> PushDispatchFailuresByPlatform)
{
    /// <summary>An empty snapshot: every gauge omitted, the FCM gauge reported.</summary>
    public static NotificationMetricSnapshot Empty(bool fcmCredentialConfigured) => new(
        fcmCredentialConfigured,
        new Dictionary<string, double>(),
        new Dictionary<string, double>(),
        new Dictionary<string, double>(),
        null,
        new Dictionary<string, long>(),
        new Dictionary<string, long>(),
        0,
        new Dictionary<string, long>());

    public static NotificationMetricSnapshot Derive(
        IReadOnlyCollection<NotificationDelivery> deliveries,
        IReadOnlyCollection<UserNotification> inbox,
        IReadOnlyCollection<NotificationRecord> records,
        IReadOnlyCollection<UserDeviceToken> deviceTokens,
        bool fcmCredentialConfigured,
        int minSampleForPercentile)
    {
        ArgumentNullException.ThrowIfNull(deliveries);
        ArgumentNullException.ThrowIfNull(inbox);
        ArgumentNullException.ThrowIfNull(records);

        var recordsById = records.ToDictionary(record => record.Id);

        // Delivery success rate per channel. A channel with no terminal attempt is omitted
        // rather than reported as 0% (BR-7.10).
        var successByChannel = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var group in deliveries.GroupBy(delivery => delivery.Channel))
        {
            var terminal = group.Where(delivery => delivery.Status != DeliveryStatus.Pending).ToArray();
            if (terminal.Length == 0)
            {
                continue;
            }

            successByChannel[ChannelName(group.Key)] =
                (double)terminal.Count(delivery => delivery.Status == DeliveryStatus.Delivered) / terminal.Length;
        }

        // Read and dismiss rates per notification type.
        var readByType = new Dictionary<string, double>(StringComparer.Ordinal);
        var dismissByType = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var group in inbox.GroupBy(item => TypeName(item, recordsById)))
        {
            var rows = group.ToArray();
            readByType[group.Key] = (double)rows.Count(item => item.ReadAt is not null) / rows.Length;
            dismissByType[group.Key] = (double)rows.Count(item => item.DismissedAt is not null) / rows.Length;
        }

        // Time to read, in minutes, for rows that have been read and have a known record time.
        var readDurations = inbox
            .Where(item => item.ReadAt is not null)
            .Select(item => (item.ReadAt!.Value - ItemCreatedAt(item, recordsById)).TotalMinutes)
            .Where(minutes => minutes >= 0)
            .OrderBy(minutes => minutes)
            .ToArray();

        // Below the sample floor the percentile is absent, not zero: a 0 would be the lie the
        // whole plan is written against.
        var p50 = readDurations.Length >= minSampleForPercentile
            ? Percentile(readDurations, 0.5)
            : (double?)null;

        var failureReasons = deliveries
            .Where(delivery => delivery.Status == DeliveryStatus.Failed)
            .GroupBy(delivery => $"{ChannelName(delivery.Channel)}|{NormaliseErrorClass(delivery.ErrorMessage)}")
            .ToDictionary(group => group.Key, group => (long)group.Count(), StringComparer.Ordinal);

        var volumeByType = records
            .GroupBy(record => TypeName(record))
            .ToDictionary(group => group.Key, group => (long)group.Count(), StringComparer.Ordinal);

        // The inbox backlog is work a user has not acted upon; it is a different denominator from
        // S-36's notification_backlog (delivery rows with Status = Pending) and must not be merged.
        var inboxBacklog = inbox.LongCount(item => item.ReadAt is null && item.DismissedAt is null);

        var failedUserIds = deliveries
            .Where(delivery => delivery.Channel == NotificationChannel.Push && delivery.Status == DeliveryStatus.Failed)
            .Select(delivery => delivery.UserId)
            .ToHashSet();

        var pushFailuresByPlatform = deviceTokens
            .Where(token => failedUserIds.Contains(token.UserId))
            .GroupBy(token => token.Platform.ToString())
            .ToDictionary(group => group.Key, group => (long)group.Count(), StringComparer.Ordinal);

        return new NotificationMetricSnapshot(
            fcmCredentialConfigured,
            successByChannel,
            readByType,
            dismissByType,
            p50,
            failureReasons,
            volumeByType,
            inboxBacklog,
            pushFailuresByPlatform);
    }

    /// <summary>A bounded error class: the message is free text and must never become a label.</summary>
    internal static string NormaliseErrorClass(string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            return "unknown";
        }

        var message = errorMessage.ToLowerInvariant();
        if (message.Contains("timeout", StringComparison.Ordinal) || message.Contains("timed out", StringComparison.Ordinal))
        {
            return "timeout";
        }

        if (message.Contains("token", StringComparison.Ordinal))
        {
            return "invalid_token";
        }

        if (message.Contains("auth", StringComparison.Ordinal) || message.Contains("credential", StringComparison.Ordinal))
        {
            return "auth";
        }

        if (message.Contains("unavailable", StringComparison.Ordinal) || message.Contains("connect", StringComparison.Ordinal))
        {
            return "unavailable";
        }

        return "other";
    }

    private static string ChannelName(NotificationChannel channel) => channel.ToString();

    private static string TypeName(UserNotification item, IReadOnlyDictionary<Guid, NotificationRecord> records)
        => records.TryGetValue(item.NotificationRecordId, out var record) ? record.Type.ToString() : "Unknown";

    private static string TypeName(NotificationRecord record) => record.Type.ToString();

    private static DateTime ItemCreatedAt(UserNotification item, IReadOnlyDictionary<Guid, NotificationRecord> records)
        => records.TryGetValue(item.NotificationRecordId, out var record) ? record.CreatedAt : item.CreatedAt;

    /// <summary>Linear-interpolation percentile over an ascending array (matches LatencyBuckets).</summary>
    private static double Percentile(IReadOnlyList<double> ascending, double percentile)
    {
        if (ascending.Count == 0)
        {
            return 0;
        }

        var rank = percentile * (ascending.Count - 1);
        var lower = (int)Math.Floor(rank);
        var upper = (int)Math.Ceiling(rank);
        if (lower == upper)
        {
            return ascending[lower];
        }

        return ascending[lower] + (ascending[upper] - ascending[lower]) * (rank - lower);
    }
}
