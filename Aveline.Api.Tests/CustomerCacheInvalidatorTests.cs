using Aveline.Api.Modules.Privacy.Services;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aveline.Api.Tests;

/// <summary>
/// Phase 5 item 5.6 (plan §7.3, R-10): deleting the database row leaves a cached profile behind, so
/// the erasure must invalidate the keys it can name. The profile cache is the Python agent's
/// <c>customer_profile:{customerId}</c>; the API-side lookup cache is keyed by
/// <c>customer:lookup:{orgId}:{name}:{phone}:{email}</c>.
/// </summary>
public class CustomerCacheInvalidatorTests
{
    private static readonly Guid OrgId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static CustomerCacheInvalidator Create(TestDistributedCache cache)
        => new(cache, NullLogger<CustomerCacheInvalidator>.Instance);

    private sealed class ThrowingCache : IDistributedCache
    {
        public byte[]? Get(string key) => throw new InvalidOperationException("Redis is unavailable.");

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
            => throw new InvalidOperationException("Redis is unavailable.");

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
            => throw new InvalidOperationException("Redis is unavailable.");

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
            => throw new InvalidOperationException("Redis is unavailable.");

        public void Refresh(string key) => throw new InvalidOperationException("Redis is unavailable.");

        public Task RefreshAsync(string key, CancellationToken token = default)
            => throw new InvalidOperationException("Redis is unavailable.");

        public void Remove(string key) => throw new InvalidOperationException("Redis is unavailable.");

        public Task RemoveAsync(string key, CancellationToken token = default)
            => throw new InvalidOperationException("Redis is unavailable.");
    }

    [Fact]
    public async Task TheProfileCacheIsEmptyForTheDeletedCustomerAndUntouchedForEveryoneElse()
    {
        var cache = new TestDistributedCache();
        var customerId = Guid.CreateVersion7();
        var otherId = Guid.CreateVersion7();
        await cache.SetStringAsync($"customer_profile:{customerId:D}", "{\"fullName\":\"Sarah\"}");
        await cache.SetStringAsync($"customer_profile:{otherId:D}", "{\"fullName\":\"Nimal\"}");

        var removed = await Create(cache).InvalidateAsync(OrgId, customerId, "+94771234567");

        Assert.True(removed >= 1);
        Assert.Null(await cache.GetStringAsync($"customer_profile:{customerId:D}"));
        Assert.NotNull(await cache.GetStringAsync($"customer_profile:{otherId:D}"));
    }

    [Fact]
    public async Task TheCustomerLookupCacheEntriesForTheErasedIdentityAreRemoved()
    {
        var cache = new TestDistributedCache();
        var customerId = Guid.CreateVersion7();
        // `CustomerService` builds the key from trimmed name/phone and a lower-cased email; the
        // invalidator derives every subset of the values the customer actually has.
        var key = $"customer:lookup:{OrgId}:sarah perera:+94771234567:";
        await cache.SetStringAsync(key, "{\"total\":1}");

        await Create(cache).InvalidateAsync(OrgId, customerId, "+94771234567", "Sarah Perera");

        Assert.Null(await cache.GetStringAsync(key));
    }

    [Fact]
    public async Task AStoreOutageDoesNotFailTheErasureThatAlreadyHappened()
    {
        // The erasure is committed by the time the cache is touched: a Redis outage must not turn a
        // completed deletion into a 500 (the cache is best-effort, the database is authoritative).
        var invalidator = new CustomerCacheInvalidator(
            new ThrowingCache(), NullLogger<CustomerCacheInvalidator>.Instance);

        var outcome = await invalidator.InvalidateAsync(OrgId, Guid.CreateVersion7(), "+94771234567");

        Assert.Equal(0, outcome);
    }
}
