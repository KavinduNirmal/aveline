namespace Aveline.Api.Common.Jobs;

/// <summary>
/// A best-effort distributed mutex for scheduled background jobs. A multi-instance
/// deployment must not run the same job concurrently (implementation-plan.md §5.2).
/// </summary>
public interface IDistributedJobLock
{
    /// <summary>
    /// Attempts to acquire the named job's lock. Returns a handle that releases the lock
    /// when disposed, or <c>null</c> when another holder owns it.
    /// </summary>
    Task<IAsyncDisposable?> TryAcquireAsync(
        string jobName,
        TimeSpan? leaseDuration = null,
        CancellationToken cancellationToken = default);
}
