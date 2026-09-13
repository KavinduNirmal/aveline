namespace Aveline.Api.Modules.Statistics.Services;

/// <summary>
/// The per-period counter store behind quota. The production implementation is Redis with a
/// single atomic Lua <c>INCRBY</c>; a process-local fallback keeps local development and the
/// test suite working without Redis, mirroring <c>InMemoryDistributedJobLock</c>.
/// </summary>
public interface IQuotaCounterStore
{
    /// <summary>Atomically adds <paramref name="amount"/> and returns the new value.</summary>
    Task<long> IncrementAsync(string key, long amount, TimeSpan ttl, CancellationToken cancellationToken = default);

    Task<long> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Sets a marker only if absent, for once-per-period warning/exhaustion events.</summary>
    Task<bool> TrySetFlagAsync(string key, TimeSpan ttl, CancellationToken cancellationToken = default);
}
