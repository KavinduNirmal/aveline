using Aveline.Api.Common.Jobs;
using Aveline.Api.Modules.Privacy.Jobs;
using Aveline.Api.Modules.Privacy.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aveline.Api.Tests;

/// <summary>
/// Item 3.5, the background half (plan §15 Q-1): the webhook enqueues a durable-intent object and
/// returns; a worker drains it in its own DI scope (Pr2's trap: the disclosure service holds a
/// scoped <c>AppDbContext</c>, so it must never be resolved from the root scope) under the
/// distributed job lock.
/// </summary>
public class DisclosureDispatchWorkerTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly Guid Customer = Guid.NewGuid();

    private sealed class RecordingDispatchService : IDisclosureDispatchService
    {
        public List<DisclosureIntent> Calls { get; } = [];

        public Task<DisclosureDispatchResult> DispatchAsync(
            Guid organizationId, Guid customerId, string toE164, CancellationToken cancellationToken = default)
        {
            Calls.Add(new DisclosureIntent(organizationId, customerId, toE164));
            return Task.FromResult(new DisclosureDispatchResult(DisclosureDispatchOutcome.Sent, "wamid.1"));
        }
    }

    private sealed class RecordingJobLock : IDistributedJobLock
    {
        public List<string> Acquired { get; } = [];

        public bool Held { get; set; }

        public Task<IAsyncDisposable?> TryAcquireAsync(
            string jobName, TimeSpan? leaseDuration = null, CancellationToken cancellationToken = default)
        {
            Acquired.Add(jobName);
            return Task.FromResult(Held ? null : (IAsyncDisposable?)new NoopHandle());
        }

        private sealed class NoopHandle : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private static (DisclosureDispatchWorker Worker, RecordingDispatchService Dispatch, RecordingJobLock Lock)
        CreateWorker()
    {
        var dispatch = new RecordingDispatchService();
        var jobLock = new RecordingJobLock();

        var services = new ServiceCollection();
        services.AddScoped<IDisclosureDispatchService>(_ => dispatch);
        var provider = services.BuildServiceProvider();

        var queue = new DisclosureDispatchQueue();
        var worker = new DisclosureDispatchWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            queue,
            jobLock,
            NullLogger<DisclosureDispatchWorker>.Instance);

        return (worker, dispatch, jobLock);
    }

    [Fact]
    public async Task ProcessAsync_DispatchesTheIntentUnderThePerCustomerLock()
    {
        var (worker, dispatch, jobLock) = CreateWorker();

        await worker.ProcessAsync(new DisclosureIntent(Org, Customer, "+94771234567"));

        var call = Assert.Single(dispatch.Calls);
        Assert.Equal(Org, call.OrganizationId);
        Assert.Equal(Customer, call.CustomerId);
        Assert.Equal("+94771234567", call.ToE164);

        // The lock is per customer, so two different customers are never serialized against each
        // other while two instances still cannot double-send one customer's disclosure.
        var lockName = Assert.Single(jobLock.Acquired);
        Assert.Contains(Org.ToString("D"), lockName, StringComparison.Ordinal);
        Assert.Contains(Customer.ToString("D"), lockName, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_WhenAnotherInstanceHoldsTheLock_DoesNotDispatch()
    {
        var (worker, dispatch, jobLock) = CreateWorker();
        jobLock.Held = true;

        await worker.ProcessAsync(new DisclosureIntent(Org, Customer, "+94771234567"));

        Assert.Empty(dispatch.Calls);
    }

    [Fact]
    public async Task ProcessAsync_WhenTheDispatchThrows_DoesNotPropagate()
    {
        var services = new ServiceCollection();
        services.AddScoped<IDisclosureDispatchService, ThrowingDispatchService>();
        var provider = services.BuildServiceProvider();
        var worker = new DisclosureDispatchWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new DisclosureDispatchQueue(),
            new RecordingJobLock(),
            NullLogger<DisclosureDispatchWorker>.Instance);

        // A provider failure must not kill the drain loop; the next inbound message retries.
        var act = () => worker.ProcessAsync(new DisclosureIntent(Org, Customer, "+94771234567"));

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task TheQueue_RoundTripsAnIntent()
    {
        var queue = new DisclosureDispatchQueue();
        var intent = new DisclosureIntent(Org, Customer, "+94771234567");

        var accepted = await queue.EnqueueAsync(intent);
        Assert.True(accepted);

        await foreach (var read in queue.ReadAllAsync(CancellationToken.None))
        {
            Assert.Equal(intent, read);
            break;
        }
    }

    [Fact]
    public async Task TheQueue_RefusesNewIntentsWhenFullInsteadOfDroppingSilently()
    {
        var queue = new DisclosureDispatchQueue(capacity: 1);

        Assert.True(await queue.EnqueueAsync(new DisclosureIntent(Org, Customer, "+94771234567")));
        // Nothing has drained the first item, so the second is refused rather than displacing it.
        Assert.False(await queue.EnqueueAsync(new DisclosureIntent(Org, Customer, "+94771234568")));
    }

    private sealed class ThrowingDispatchService : IDisclosureDispatchService
    {
        public Task<DisclosureDispatchResult> DispatchAsync(
            Guid organizationId, Guid customerId, string toE164, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("disclosure store unavailable");
    }
}
