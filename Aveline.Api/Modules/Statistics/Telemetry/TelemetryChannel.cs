using System.Threading.Channels;
using Microsoft.Extensions.Options;

namespace Aveline.Api.Modules.Statistics.Telemetry;

/// <summary>
/// The bounded, single-reader buffer between the middleware and
/// <see cref="ApiTelemetryWriter"/> (BR-6.4). When full the oldest sample is dropped and
/// <see cref="DroppedSamples"/> is incremented; telemetry is best-effort by design.
/// </summary>
public sealed class TelemetryChannel
{
    private readonly Channel<ApiRequestSample> _channel;
    private long _pending;
    private long _dropped;

    public TelemetryChannel(IOptions<TelemetryOptions> options)
        : this(options.Value.BufferCapacity)
    {
    }

    public TelemetryChannel(int capacity)
    {
        Capacity = capacity > 0 ? capacity : 1;
        _channel = Channel.CreateBounded<ApiRequestSample>(new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    public int Capacity { get; }

    /// <summary>Samples dropped because the buffer was full.</summary>
    public long DroppedSamples => Interlocked.Read(ref _dropped);

    /// <summary>Enqueues a sample. Never throws for a non-completed channel.</summary>
    public bool TryEnqueue(in ApiRequestSample sample)
    {
        var pending = Interlocked.Increment(ref _pending);
        if (pending > Capacity)
        {
            // DropOldest evicted one already-buffered sample to make room.
            Interlocked.Increment(ref _dropped);
            Interlocked.Decrement(ref _pending);
        }

        return _channel.Writer.TryWrite(sample);
    }

    public bool TryRead(out ApiRequestSample sample)
    {
        if (_channel.Reader.TryRead(out sample))
        {
            Interlocked.Decrement(ref _pending);
            return true;
        }

        sample = default;
        return false;
    }

    public ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken) =>
        _channel.Reader.WaitToReadAsync(cancellationToken);
}
