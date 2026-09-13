using System.Collections.Concurrent;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Infrastructure.Eventing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aveline.Api.Modules.Statistics.Telemetry;

/// <summary>Receives per-key usage from the telemetry pipeline without blocking it.</summary>
public interface IApiKeyUsageSink
{
    void Record(Guid apiKeyId, DateTime lastUsedAt, string? clientIpHash);
}

/// <summary>
/// Amortises API-key usage writes: <c>LastUsedAt</c>, <c>RequestCount</c> and
/// <c>LastUsedIpHash</c> are accumulated in memory and flushed to <c>ApiKeys</c> at most once
/// per minute per key, publishing <c>apikey.lastused</c> (this closes FR-3.18).
/// </summary>
public sealed class ApiKeyUsageAggregator(
    IServiceScopeFactory scopeFactory,
    IEventBus eventBus,
    ILogger<ApiKeyUsageAggregator> logger) : BackgroundService, IApiKeyUsageSink
{
    /// <summary>Flush cadence: at most one write per key per minute (FR-3.18).</summary>
    public static readonly TimeSpan FlushInterval = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<Guid, Usage> _pending = new();

    public void Record(Guid apiKeyId, DateTime lastUsedAt, string? clientIpHash)
    {
        _pending.AddOrUpdate(
            apiKeyId,
            _ => new Usage(1, lastUsedAt, clientIpHash),
            (_, existing) => new Usage(
                existing.Count + 1,
                lastUsedAt > existing.LastUsedAt ? lastUsedAt : existing.LastUsedAt,
                existing.ClientIpHash ?? clientIpHash));
    }

    /// <summary>Flushes all pending usage. Public so tests can drive it deterministically.</summary>
    public async Task<int> FlushAsync(CancellationToken cancellationToken = default)
    {
        if (_pending.IsEmpty)
        {
            return 0;
        }

        var flushed = new List<(Guid ApiKeyId, Guid OrganizationId, long Count, DateTime LastUsedAt)>();
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        foreach (var (apiKeyId, usage) in _pending.ToArray())
        {
            // Only remove when nobody recorded more usage after our snapshot.
            if (!_pending.TryRemove(new KeyValuePair<Guid, Usage>(apiKeyId, usage)))
            {
                continue;
            }

            var key = await db.ApiKeys.FirstOrDefaultAsync(k => k.Id == apiKeyId, cancellationToken);
            if (key is null)
            {
                continue;
            }

            key.RequestCount += usage.Count;
            if (key.LastUsedAt is null || usage.LastUsedAt > key.LastUsedAt)
            {
                key.LastUsedAt = usage.LastUsedAt;
            }

            key.LastUsedIpHash ??= usage.ClientIpHash;
            flushed.Add((key.Id, key.OrganizationId, usage.Count, usage.LastUsedAt));
        }

        if (flushed.Count == 0)
        {
            return 0;
        }

        await db.SaveChangesAsync(cancellationToken);
        foreach (var entry in flushed)
        {
            await PublishAsync(entry, cancellationToken);
        }

        return flushed.Count;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(FlushInterval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                {
                    break;
                }

                await FlushAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "API key usage flush failed.");
            }
        }
    }

    private async Task PublishAsync(
        (Guid ApiKeyId, Guid OrganizationId, long Count, DateTime LastUsedAt) entry,
        CancellationToken cancellationToken)
    {
        try
        {
            await eventBus.PublishAsync(
                "apikey.lastused",
                organizationId: entry.OrganizationId,
                payload: new
                {
                    apiKeyId = entry.ApiKeyId,
                    requestCount = entry.Count,
                    lastUsedAt = entry.LastUsedAt,
                },
                cancellationToken: cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "apikey.lastused could not be published.");
        }
    }

    private sealed record Usage(long Count, DateTime LastUsedAt, string? ClientIpHash);
}
