using Aveline.Api.Modules.Conversations.Attachments;
using Aveline.Api.Modules.Conversations.Repositories;

namespace Aveline.Api.Modules.Conversations.Services;

/// <summary>
/// Deletes attachments that were uploaded but never bound to a message.
/// </summary>
/// <remarks>
/// The two-step upload (store, then send with the ids) means a picker cancelled mid-flight, a
/// send that failed validation, or a client that never came back leaves bytes behind. The TTL is
/// generous enough that a slow composer still binds, and short enough that the table does not
/// grow without bound.
/// </remarks>
public sealed class AttachmentSweepJob : BackgroundService
{
    /// <summary>How long an unbound upload is kept before it is swept.</summary>
    public static readonly TimeSpan Ttl = TimeSpan.FromHours(24);

    /// <summary>How often the sweep runs.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AttachmentSweepJob> _logger;

    public AttachmentSweepJob(
        IServiceScopeFactory scopeFactory,
        ILogger<AttachmentSweepJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                var swept = await SweepAsync(stoppingToken);
                if (swept > 0)
                {
                    _logger.LogInformation("Swept {Count} unbound attachment(s).", swept);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                // A sweep that fails must not take the host down; the next tick retries.
                _logger.LogError(ex, "Attachment sweep failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Deletes every unbound attachment older than the TTL, returning how many.</summary>
    public async Task<int> SweepAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var attachments = scope.ServiceProvider.GetRequiredService<IMessageAttachmentRepository>();
        var store = scope.ServiceProvider.GetRequiredService<IAttachmentStore>();

        var cutoff = DateTime.UtcNow - Ttl;
        var orphans = await attachments.ListOrphansAsync(cutoff, cancellationToken);
        if (orphans.Count == 0)
        {
            return 0;
        }

        // The store is told first, so a provider that keeps bytes elsewhere releases them before
        // the row that names them disappears.
        foreach (var orphan in orphans)
        {
            await store.DeleteAsync(orphan, cancellationToken);
        }

        return await attachments.DeleteOrphansAsync(cutoff, cancellationToken);
    }
}
