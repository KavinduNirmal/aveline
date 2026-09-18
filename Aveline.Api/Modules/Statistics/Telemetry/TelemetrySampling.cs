namespace Aveline.Api.Modules.Statistics.Telemetry;

/// <summary>
/// The raw-log sampling decision (BR-6.3): 100 % of non-2xx and of slow requests, plus a
/// configurable fraction of the rest. Rollups are never sampled.
/// </summary>
public static class TelemetrySampling
{
    public static bool ShouldPersistRaw(
        short statusCode, int durationMs, int slowRequestMs, double successSampleRate, double roll)
    {
        if (statusCode < 200 || statusCode >= 300)
        {
            return true;
        }

        if (durationMs > slowRequestMs)
        {
            return true;
        }

        return roll < successSampleRate;
    }
}
