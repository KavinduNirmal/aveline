using System.Collections.Concurrent;

namespace Aveline.Api.Infrastructure.Eventing;

/// <summary>
/// In-process fallback <see cref="IEventBus"/> used when no Redis connection string is
/// configured (e.g. the unit/integration test suite). Handlers registered in the same
/// process receive published events synchronously; nothing is delivered across processes.
/// This mirrors the in-memory distributed-cache fallback used by <c>CacheConfiguration</c>.
/// </summary>
public sealed class InMemoryEventBus : IEventBus
{
    private readonly ConcurrentDictionary<string, List<Func<EventEnvelope, CancellationToken, Task>>> _handlers = new();

    public Task PublishAsync(
        string eventType,
        Guid? organizationId,
        object? payload,
        Guid? traceId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);

        var envelope = EventEnvelope.Create(eventType, organizationId, payload, traceId);
        if (_handlers.TryGetValue(eventType, out var handlers))
        {
            Func<EventEnvelope, CancellationToken, Task>[] snapshot;
            lock (handlers)
            {
                snapshot = handlers.ToArray();
            }

            foreach (var handler in snapshot)
            {
                handler(envelope, cancellationToken).GetAwaiter().GetResult();
            }
        }

        return Task.CompletedTask;
    }

    public Task SubscribeAsync(
        string eventType,
        Func<EventEnvelope, CancellationToken, Task> handler,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentNullException.ThrowIfNull(handler);

        _handlers.AddOrUpdate(
            eventType,
            _ => new List<Func<EventEnvelope, CancellationToken, Task>> { handler },
            (_, existing) =>
            {
                lock (existing)
                {
                    existing.Add(handler);
                }
                return existing;
            });

        return Task.CompletedTask;
    }

    public Task UnsubscribeAsync(
        string eventType,
        Func<EventEnvelope, CancellationToken, Task> handler,
        CancellationToken cancellationToken = default)
    {
        if (_handlers.TryGetValue(eventType, out var handlers))
        {
            lock (handlers)
            {
                handlers.Remove(handler);
                if (handlers.Count == 0)
                {
                    _handlers.TryRemove(eventType, out _);
                }
            }
        }

        return Task.CompletedTask;
    }
}
