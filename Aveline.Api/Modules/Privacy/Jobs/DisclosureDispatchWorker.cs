using Aveline.Api.Common.Jobs;
using Aveline.Api.Modules.Privacy.Services;

namespace Aveline.Api.Modules.Privacy.Jobs;

/// <summary>
/// Drains <see cref="IDisclosureDispatchQueue"/> and sends each first-contact disclosure (plan
/// §4.4, §15 Q-1). It exists because the webhook must not be held open for a Meta round-trip: the
/// webhook enqueues and answers 200, and this worker - which is not on any request path - does the
/// send.
/// </summary>
/// <remarks>
/// <para>
/// <b>It creates its own DI scope per intent.</b> The disclosure service holds the scoped
/// <c>AppDbContext</c> (the trap Pr2 documented in <c>IntegrationsModule</c>), so resolving it from
/// the root scope would either throw at a scope-validation build or capture a disposed context. The
/// only singleton dependencies here are the scope factory, the queue reader and the job lock.
/// </para>
/// <para>
/// <b>Per-customer distributed lock.</b> In-process queueing means one API instance owns a given
/// intent, so the lock is defence in depth: it keeps a future fan-out queue (or a manually replayed
/// intent) from racing a second instance. Losing the lock is a no-op, not an error, because the
/// holder is sending the same disclosure.
/// </para>
/// <para>
/// <b>It also drains the opt-out acknowledgement channel</b> (plan §11 item 4.5). The two channels
/// are drained concurrently so a backlog of disclosures cannot delay the one message a customer who
/// just opted out must receive. A process without
/// <see cref="IOptOutAcknowledgementService"/> registered (a unit test, or a host that has not
/// enabled the privacy surface) logs and skips acknowledgement intents rather than failing to start.
/// </para>
/// <para>
/// Never logs a message body or an unmasked number. Failures are logged and swallowed so a provider
/// outage cannot kill the drain loop.
/// </para>
/// </remarks>
public sealed class DisclosureDispatchWorker : BackgroundService
{
    /// <summary>The base of the per-customer distributed lock name.</summary>
    public const string JobName = "privacy-disclosure";

    /// <summary>The base of the per-customer acknowledgement lock name.</summary>
    public const string AcknowledgementJobName = "privacy-opt-out-ack";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDisclosureDispatchQueueReader _queue;
    private readonly IDistributedJobLock _jobLock;
    private readonly ILogger<DisclosureDispatchWorker> _logger;

    public DisclosureDispatchWorker(
        IServiceScopeFactory scopeFactory,
        IDisclosureDispatchQueueReader queue,
        IDistributedJobLock jobLock,
        ILogger<DisclosureDispatchWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _queue = queue;
        _jobLock = jobLock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.WhenAll(
            DrainDisclosuresAsync(stoppingToken),
            DrainAcknowledgementsAsync(stoppingToken));
    }

    private async Task DrainDisclosuresAsync(CancellationToken stoppingToken)
    {
        await foreach (var intent in _queue.ReadAllAsync(stoppingToken))
        {
            await ProcessAsync(intent, stoppingToken);
        }
    }

    private async Task DrainAcknowledgementsAsync(CancellationToken stoppingToken)
    {
        await foreach (var intent in _queue.ReadAcknowledgementsAsync(stoppingToken))
        {
            await ProcessAcknowledgementAsync(intent, stoppingToken);
        }
    }

    /// <summary>
    /// Processes one intent. Public so a test can drive a single pass without starting the host or
    /// racing the drain loop.
    /// </summary>
    public async Task ProcessAsync(DisclosureIntent intent, CancellationToken cancellationToken = default)
    {
        var lockName = $"{JobName}:{intent.OrganizationId:D}:{intent.CustomerId:D}";

        try
        {
            await using var handle = await _jobLock.TryAcquireAsync(
                lockName, cancellationToken: cancellationToken);
            if (handle is null)
            {
                _logger.LogInformation(
                    "Disclosure intent skipped: another instance holds the lock. organizationId={OrganizationId} customerId={CustomerId}",
                    intent.OrganizationId, intent.CustomerId);
                return;
            }

            // A fresh scope per intent: the disclosure service and its AppDbContext are scoped.
            using var scope = _scopeFactory.CreateScope();
            var dispatch = scope.ServiceProvider.GetRequiredService<IDisclosureDispatchService>();
            var result = await dispatch.DispatchAsync(
                intent.OrganizationId, intent.CustomerId, intent.ToE164, cancellationToken);

            if (result.Outcome is DisclosureDispatchOutcome.NotConfigured or DisclosureDispatchOutcome.Failed)
            {
                _logger.LogWarning(
                    "Disclosure intent did not result in a send; the next inbound message retries. "
                    + "organizationId={OrganizationId} customerId={CustomerId} outcome={Outcome}",
                    intent.OrganizationId, intent.CustomerId, result.Outcome);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The drain loop must survive a bad intent; the customer's next message is the retry.
            _logger.LogError(
                ex,
                "Disclosure intent failed. organizationId={OrganizationId} customerId={CustomerId}",
                intent.OrganizationId, intent.CustomerId);
        }
    }

    /// <summary>
    /// Processes one opt-out acknowledgement intent. Public for the same reason
    /// <see cref="ProcessAsync"/> is: a test drives a single pass deterministically.
    /// </summary>
    public async Task ProcessAcknowledgementAsync(
        OptOutAcknowledgementIntent intent, CancellationToken cancellationToken = default)
    {
        var lockName =
            $"{AcknowledgementJobName}:{intent.OrganizationId:D}:{intent.CustomerId:D}";

        try
        {
            await using var handle = await _jobLock.TryAcquireAsync(
                lockName, cancellationToken: cancellationToken);
            if (handle is null)
            {
                _logger.LogInformation(
                    "Opt-out acknowledgement skipped: another instance holds the lock. organizationId={OrganizationId}",
                    intent.OrganizationId);
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetService<IOptOutAcknowledgementService>();
            if (service is null)
            {
                // A host without the privacy surface must still start; nothing to do here.
                _logger.LogDebug(
                    "Opt-out acknowledgement intent ignored: no acknowledgement service is registered.");
                return;
            }

            var result = await service.SendOnceAsync(
                intent.OrganizationId, intent.CustomerId, intent.ToE164, intent.OrganizationName,
                cancellationToken);

            if (result.Outcome is OptOutAcknowledgementOutcome.Failed
                or OptOutAcknowledgementOutcome.GateUnavailable)
            {
                _logger.LogWarning(
                    "Opt-out acknowledgement did not send. organizationId={OrganizationId} outcome={Outcome}",
                    intent.OrganizationId, result.Outcome);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The drain loop must survive a bad intent.
            _logger.LogError(
                ex,
                "Opt-out acknowledgement intent failed. organizationId={OrganizationId}",
                intent.OrganizationId);
        }
    }
}
