using Aveline.Api.Modules.Organizations.Services;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aveline.Api.Tests;

public class DistributedInvitationCodeStoreTests
{
    private readonly DistributedInvitationCodeStore _store;
    private readonly IDistributedCache _cache;

    public DistributedInvitationCodeStoreTests()
    {
        _cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        _store = new DistributedInvitationCodeStore(_cache, NullLogger<DistributedInvitationCodeStore>.Instance);
    }

    [Fact]
    public async Task StoreAndGet_RoundTrips()
    {
        var id = Guid.NewGuid();

        await _store.StoreAsync("CODE1", id, TimeSpan.FromHours(24));

        Assert.Equal(id, await _store.GetAsync("CODE1"));
    }

    [Fact]
    public async Task Get_UnknownCode_ReturnsNull()
    {
        Assert.Null(await _store.GetAsync("NOPE"));
    }

    [Fact]
    public async Task Remove_DeletesCode()
    {
        var id = Guid.NewGuid();
        await _store.StoreAsync("CODE2", id, TimeSpan.FromHours(24));

        await _store.RemoveAsync("CODE2");

        Assert.Null(await _store.GetAsync("CODE2"));
    }

    [Fact]
    public async Task Store_ExpiresAfterTtl()
    {
        var id = Guid.NewGuid();
        await _store.StoreAsync("SHORT", id, TimeSpan.FromMilliseconds(80));

        // Still present before TTL.
        Assert.Equal(id, await _store.GetAsync("SHORT"));

        await Task.Delay(200);

        Assert.Null(await _store.GetAsync("SHORT"));
    }

    [Fact]
    public async Task Store_CorruptValue_ReturnsNull()
    {
        await _cache.SetStringAsync("invite:code:BAD", "not-a-guid", new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1),
        });

        Assert.Null(await _store.GetAsync("BAD"));
    }
}
