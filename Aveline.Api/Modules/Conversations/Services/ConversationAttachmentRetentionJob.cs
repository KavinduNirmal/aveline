using Aveline.Api.Common.Jobs;
using Aveline.Api.Modules.Conversations.Attachments;
using Aveline.Api.Modules.Conversations.Models;
using Aveline.Api.Modules.Conversations.Repositories;
using Aveline.Api.Modules.Media;
using Microsoft.Extensions.Configuration;

namespace Aveline.Api.Modules.Conversations.Services;

/// <summary>
/// S7: permanently deletes bound conversation attachments older than
/// <c>Conversations:AttachmentRetentionDays</c> (default 7), releasing the remote asset before the
/// row disappears.
/// </summary>
/// <remarks>
/// <para>
/// <b>Pinned semantics</b> (strategy §10 item 7 — the implementation input this unit must pin,
/// not re-open):
/// </para>
/// <list type="bullet">
/// <item>
/// <b>The window is measured from the attachment's own creation</b>
/// (<see cref="MessageAttachment.CreatedAtUtc"/>) — neither the conversation's last activity nor
/// the message's timestamp. The attachment is the item Q1's answer drops ("conversation items are
/// dropped after 7 days"), and its creation is the moment the customer's bytes entered the
/// system. Anchoring on the conversation instead would let an active thread hold every image it
/// ever received, which is exactly the unbounded conversation tier S7 exists to remove.
/// </item>
/// <item>
/// <b>A live conversation is swept.</b> Retention is per attachment, never per conversation: no
/// later message resets an older attachment's clock. A boutique therefore permanently loses a
/// customer's photo one week after it arrived. The window is configuration
/// (<c>Conversations:AttachmentRetentionDays</c>), not a constant, and the job can be stopped
/// outright — the S7 rollback (strategy §5.1).
/// </item>
/// <item>
/// <b>The catalog is exempt.</b> A catalog image is an <c>InventoryImage</c>, not conversation
/// content, and this job never selects one: it reads only <see cref="MessageAttachment"/>. The
/// catalog's own delete path is S8's, and its retention is bounded by the item cap rather than by
/// this policy.
/// </item>
/// </list>
/// <para>
/// <b>Bound only, and separate from the orphan sweep.</b> Rows without a <c>MessageId</c> remain
/// <see cref="AttachmentSweepJob"/>'s responsibility (24 h); the two jobs have different triggers
/// and are deliberately not merged (strategy §5.3).
/// </para>
/// <para>
/// <b>Store before row.</b> Every asset is released through <see cref="IAttachmentStore.DeleteAsync"/>
/// before its row is removed — the ordering <see cref="AttachmentSweepJob"/> established
/// (<c>AttachmentSweepJob.cs:74-78</c>). A store failure skips that row and keeps it for the next
/// run, rather than dropping the row and leaving an asset nothing names (risk R23).
/// </para>
/// <para>
/// <b>Privacy interaction (strategy §6 item 8).</b> This job is one of the two callers of the same
/// store deletion the privacy plan's erasure path will use
/// (<c>privacy-consent-data-deletion-implementation.ignore.md</c> Q-3): the erasure path and this
/// retention path must both call <see cref="IAttachmentStore.DeleteAsync"/>, and neither may be
/// written assuming the other does not exist. The erasure path itself is deliberately not built
/// here.
/// </para>
/// </remarks>
public sealed class ConversationAttachmentRetentionJob(
    IServiceScopeFactory scopeFactory,
    IDistributedJobLock jobLock,
    ILogger<ConversationAttachmentRetentionJob> logger) : BackgroundService
{
    /// <summary>The name the distributed job lock is taken under.</summary>
    public const string JobName = "conversation-attachment-retention";

    /// <summary>The retention window key; the startup validator refuses a non-positive value.</summary>
    public const string RetentionDaysConfigurationKey = MediaOptionsValidator.AttachmentRetentionDaysKey;

    /// <summary>The bounded-per-run ceiling key; absent means <see cref="DefaultMaxPerRun"/>.</summary>
    public const string MaxPerRunConfigurationKey = "Conversations:AttachmentRetentionMaxPerRun";

    /// <summary>The documented default window, in days (strategy §3.4).</summary>
    public const int DefaultRetentionDays = MediaOptionsValidator.DefaultAttachmentRetentionDays;

    /// <summary>
    /// How many attachments one pass may release, following
    /// <c>CustomerSalonBackfill.DefaultMaxCustomers</c>: a bounded pass cannot monopolise the
    /// database, and a backlog drains across runs.
    /// </summary>
    public const int DefaultMaxPerRun = 500;

    /// <summary>How often the retention pass runs.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (!stoppingToken.IsCancellationRequested)
        {
            await RunLockedAsync(stoppingToken);

            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                {
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Runs one pass under the distributed lock. Returns 0 when another holder has the lock, so a
    /// second instance is a no-op rather than a concurrent delete.
    /// </summary>
    public async Task<int> RunLockedAsync(CancellationToken cancellationToken = default)
    {
        await using var handle = await jobLock.TryAcquireAsync(JobName, cancellationToken: cancellationToken);
        if (handle is null)
        {
            return 0;
        }

        try
        {
            return await RunAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A retention pass that fails must not take the host down; the next tick retries.
            logger.LogError(exception, "Conversation attachment retention failed.");
            return 0;
        }
    }

    /// <summary>One retention pass, public so a test can drive it deterministically.</summary>
    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var attachments = scope.ServiceProvider.GetRequiredService<IMessageAttachmentRepository>();
        var store = scope.ServiceProvider.GetRequiredService<IAttachmentStore>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        // The startup validator refuses a non-positive window, so the clamp is defence in depth
        // rather than the policy.
        var days = Math.Max(1, configuration.GetValue(
            RetentionDaysConfigurationKey, DefaultRetentionDays));
        var maxPerRun = Math.Max(1, configuration.GetValue(
            MaxPerRunConfigurationKey, DefaultMaxPerRun));

        var cutoff = DateTime.UtcNow.AddDays(-days);
        var expired = await attachments.ListBoundForRetentionAsync(cutoff, maxPerRun, cancellationToken);
        if (expired.Count == 0)
        {
            return 0;
        }

        // The store is told before the row disappears (AttachmentSweepJob.cs:74-78). A provider
        // failure skips that row: its row is kept so the next run retries, rather than being
        // removed and leaving a remote asset with nothing naming it.
        var released = new List<MessageAttachment>(expired.Count);
        foreach (var attachment in expired)
        {
            try
            {
                await store.DeleteAsync(attachment, cancellationToken);
                released.Add(attachment);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(
                    exception,
                    "Could not release attachment {AttachmentId}; its row is kept for the next run.",
                    attachment.Id);
            }
        }

        if (released.Count == 0)
        {
            return 0;
        }

        var removed = await attachments.DeleteRangeAsync(released, cancellationToken);
        logger.LogInformation("Released {Count} expired conversation attachment(s).", removed);
        return removed;
    }
}
