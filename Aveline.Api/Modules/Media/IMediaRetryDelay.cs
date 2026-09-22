namespace Aveline.Api.Modules.Media;

/// <summary>
/// The backoff delay seam. Production waits on <see cref="TaskDelayMediaRetryDelay"/>; the unit
/// tests record the requested delays and return immediately, so the retry contract is asserted
/// without spending its wall-clock time.
/// </summary>
internal interface IMediaRetryDelay
{
    /// <summary>Waits for <paramref name="delay"/>, honouring the caller's cancellation.</summary>
    Task DelayAsync(TimeSpan delay, CancellationToken ct);
}

/// <summary>The production delay: <see cref="Task.Delay(TimeSpan, CancellationToken)"/>.</summary>
internal sealed class TaskDelayMediaRetryDelay : IMediaRetryDelay
{
    /// <inheritdoc />
    public Task DelayAsync(TimeSpan delay, CancellationToken ct) => Task.Delay(delay, ct);
}
