using Aveline.Api.Common.Jobs;

namespace Aveline.Api.Tests;

/// <summary>
/// Issue #179 — scheduled jobs must not run twice across instances (implementation-plan.md §5.2).
/// The in-memory implementation is deterministic and clock-injectable so lease expiry is
/// tested without sleeping.
/// </summary>
public class DistributedJobLockTests
{
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(300);

    [Fact]
    public async Task TryAcquire_FirstCall_ReturnsHandle()
    {
        var sut = new InMemoryDistributedJobLock(Lease);

        await using var handle = await sut.TryAcquireAsync("billing-period-rollover");

        Assert.NotNull(handle);
    }

    [Fact]
    public async Task TryAcquire_WhileHeld_ReturnsNull()
    {
        var sut = new InMemoryDistributedJobLock(Lease);

        await using var first = await sut.TryAcquireAsync("pricing-cache-warmer");
        var second = await sut.TryAcquireAsync("pricing-cache-warmer");

        Assert.NotNull(first);
        Assert.Null(second);
    }

    [Fact]
    public async Task Dispose_ReleasesLock_SoItCanBeReacquired()
    {
        var sut = new InMemoryDistributedJobLock(Lease);

        var first = await sut.TryAcquireAsync("audit-cleanup");
        Assert.NotNull(first);
        await first!.DisposeAsync();

        var second = await sut.TryAcquireAsync("audit-cleanup");
        Assert.NotNull(second);
        await second!.DisposeAsync();
    }

    [Fact]
    public async Task Lease_Expiry_AllowsReacquisition()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var sut = new InMemoryDistributedJobLock(Lease, clock);

        await using var first = await sut.TryAcquireAsync("expiring-job");
        Assert.NotNull(first);

        clock.Advance(Lease + TimeSpan.FromSeconds(1));

        var second = await sut.TryAcquireAsync("expiring-job");
        Assert.NotNull(second);
        await second!.DisposeAsync();
    }

    [Fact]
    public async Task DisposingAStaleHandle_DoesNotReleaseTheCurrentHolder()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var sut = new InMemoryDistributedJobLock(Lease, clock);

        var stale = await sut.TryAcquireAsync("stale-job");
        Assert.NotNull(stale);

        clock.Advance(Lease + TimeSpan.FromSeconds(1));

        await using var current = await sut.TryAcquireAsync("stale-job");
        Assert.NotNull(current);

        await stale!.DisposeAsync();

        // The stale handle must not have removed the current holder's lock.
        var third = await sut.TryAcquireAsync("stale-job");
        Assert.Null(third);
    }

    [Fact]
    public async Task DifferentJobs_DoNotBlockEachOther()
    {
        var sut = new InMemoryDistributedJobLock(Lease);

        await using var a = await sut.TryAcquireAsync("job-a");
        await using var b = await sut.TryAcquireAsync("job-b");

        Assert.NotNull(a);
        Assert.NotNull(b);
    }

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delta) => _now += delta;
    }
}
