using System.Net;
using System.Text;
using System.Text.Json;
using Aveline.Api.Common.Jobs;
using Aveline.Api.Modules.Billing.Endpoints;
using Aveline.Api.Modules.Billing.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aveline.Api.Tests;

/// <summary>
/// Disposition §2.1 — the idempotency lease must fail closed: when the lock store is
/// unreachable the request is refused rather than executed without the exactly-once guard,
/// and an in-flight original is reported as a conflict.
/// </summary>
public class IdempotencyEndpointFilterTests
{
    private const string Key = "lease-test-key";

    [Fact]
    public async Task LeaseStoreFailure_BlocksTheRequestWith503()
    {
        var context = CreateContext();
        var service = new StubIdempotencyService();
        var filter = CreateFilter(service, new StubLock(StubLockMode.Throw));
        var nextCalled = false;

        var result = await filter.InvokeAsync(
            new TestInvocationContext(context),
            _ =>
            {
                nextCalled = true;
                return ValueTask.FromResult<object?>(Results.Ok(new { ok = true }));
            });

        var (status, body) = await ExecuteAsync((IResult)result!, context);

        Assert.False(nextCalled, "the endpoint must not run when the lease store is unavailable");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        Assert.Equal("idempotency-unavailable", body.GetProperty("code").GetString());
        Assert.Equal(0, service.SaveCalls);
    }

    [Fact]
    public async Task LeaseTimeout_Returns409InFlightAndDoesNotExecute()
    {
        var context = CreateContext();
        var service = new StubIdempotencyService();
        var filter = CreateFilter(service, new StubLock(StubLockMode.NeverAcquire));
        var nextCalled = false;

        var result = await filter.InvokeAsync(
            new TestInvocationContext(context),
            _ =>
            {
                nextCalled = true;
                return ValueTask.FromResult<object?>(Results.Ok(new { ok = true }));
            });

        var (status, body) = await ExecuteAsync((IResult)result!, context);

        Assert.False(nextCalled, "the endpoint must not run while another request holds the key");
        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal("idempotency-key-in-flight", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task GrantedLease_ExecutesTheEndpointOnceAndStoresTheResponse()
    {
        var context = CreateContext();
        var service = new StubIdempotencyService();
        var filter = CreateFilter(service, new StubLock(StubLockMode.Grant));
        var nextCalls = 0;

        var result = await filter.InvokeAsync(
            new TestInvocationContext(context),
            _ =>
            {
                nextCalls++;
                return ValueTask.FromResult<object?>(Results.Ok(new { ok = true }));
            });

        Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.EmptyHttpResult>(result);
        Assert.Equal(1, nextCalls);
        Assert.Equal(1, service.SaveCalls);
        Assert.Equal("POST", service.LastSavedMethod);
    }

    [Fact]
    public async Task MissingKey_Returns400WithoutTouchingTheLease()
    {
        var context = CreateContext(includeKey: false);
        var filter = CreateFilter(new StubIdempotencyService(), new StubLock(StubLockMode.Grant));

        var result = await filter.InvokeAsync(
            new TestInvocationContext(context),
            _ => ValueTask.FromResult<object?>(Results.Ok()));

        var (status, body) = await ExecuteAsync((IResult)result!, context);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("idempotency-key-required", body.GetProperty("code").GetString());
    }

    private static IdempotencyEndpointFilter CreateFilter(
        IIdempotencyService service, IDistributedJobLock locks)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Zero wait so the timeout path resolves immediately in the test.
                ["Billing:IdempotencyLeaseWaitSeconds"] = "0",
            })
            .Build();

        return new IdempotencyEndpointFilter(
            service, locks, NullLogger<IdempotencyEndpointFilter>.Instance, configuration);
    }

    private static DefaultHttpContext CreateContext(bool includeKey = true)
    {
        var context = new DefaultHttpContext();
        // Results.Json resolves JsonOptions from RequestServices when the IResult executes.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        context.RequestServices = services.BuildServiceProvider();
        context.Request.Method = "POST";
        context.Request.Path = "/api/v1/admin/orgs/00000000-0000-0000-0000-000000000001/blossoms/credit";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{\"amount\":100}"));
        context.Response.Body = new MemoryStream();
        if (includeKey)
        {
            context.Request.Headers[IdempotencyEndpointFilter.HeaderName] = Key;
        }

        return context;
    }

    private static async Task<(HttpStatusCode Status, JsonElement Body)> ExecuteAsync(
        IResult result, HttpContext context)
    {
        await result.ExecuteAsync(context);
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var text = await new StreamReader(context.Response.Body).ReadToEndAsync();
        return ((HttpStatusCode)context.Response.StatusCode, JsonDocument.Parse(text).RootElement);
    }

    private sealed class TestInvocationContext(HttpContext httpContext) : EndpointFilterInvocationContext
    {
        public override HttpContext HttpContext { get; } = httpContext;

        public override IList<object?> Arguments { get; } = new List<object?>();

        public override T GetArgument<T>(int index) => (T)Arguments[index]!;
    }

    private enum StubLockMode
    {
        Grant,
        NeverAcquire,
        Throw,
    }

    private sealed class StubLock(StubLockMode mode) : IDistributedJobLock
    {
        public Task<IAsyncDisposable?> TryAcquireAsync(
            string jobName, TimeSpan? leaseDuration = null, CancellationToken cancellationToken = default)
            => mode switch
            {
                StubLockMode.Grant => Task.FromResult<IAsyncDisposable?>(new Handle()),
                StubLockMode.NeverAcquire => Task.FromResult<IAsyncDisposable?>(null),
                _ => throw new InvalidOperationException("lease store unavailable"),
            };

        private sealed class Handle : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class StubIdempotencyService : IIdempotencyService
    {
        public int SaveCalls { get; private set; }

        public string? LastSavedMethod { get; private set; }

        public Task<IdempotencyReplay?> TryReplayAsync(
            Guid? organizationId, string endpoint, string httpMethod, string idempotencyKey,
            string requestHash, DateTime at, CancellationToken cancellationToken = default)
            => Task.FromResult<IdempotencyReplay?>(null);

        public Task SaveAsync(
            Guid? organizationId, string endpoint, string httpMethod, string idempotencyKey,
            string requestHash, short responseStatus, string responseBodyJson, DateTime createdAt,
            Guid? actorUserId = null, Guid? apiKeyId = null, CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            LastSavedMethod = httpMethod;
            return Task.CompletedTask;
        }

        public Task<int> DeleteExpiredAsync(DateTime asOf, CancellationToken cancellationToken = default)
            => Task.FromResult(0);
    }
}
