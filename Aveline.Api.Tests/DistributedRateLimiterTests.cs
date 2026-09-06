using Aveline.Api.Infrastructure.RateLimiting;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aveline.Api.Tests;

public class DistributedRateLimiterTests
{
    private readonly DistributedRateLimiter _limiter;

    public DistributedRateLimiterTests()
    {
        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        _limiter = new DistributedRateLimiter(cache, NullLogger<DistributedRateLimiter>.Instance);
    }

    [Fact]
    public async Task AllowsUpToLimit_ThenBlocks()
    {
        const int limit = 3;

        Assert.True(await _limiter.TryAllowAsync("ip:1.2.3.4", limit, TimeSpan.FromMinutes(1)));
        Assert.True(await _limiter.TryAllowAsync("ip:1.2.3.4", limit, TimeSpan.FromMinutes(1)));
        Assert.True(await _limiter.TryAllowAsync("ip:1.2.3.4", limit, TimeSpan.FromMinutes(1)));
        Assert.False(await _limiter.TryAllowAsync("ip:1.2.3.4", limit, TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public async Task DifferentScopes_AreIndependent()
    {
        const int limit = 1;

        Assert.True(await _limiter.TryAllowAsync("ip:1.2.3.4", limit, TimeSpan.FromMinutes(1)));
        Assert.False(await _limiter.TryAllowAsync("ip:1.2.3.4", limit, TimeSpan.FromMinutes(1)));

        // A different client IP is unaffected.
        Assert.True(await _limiter.TryAllowAsync("ip:5.6.7.8", limit, TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public async Task NonPositiveLimit_AlwaysBlocks()
    {
        Assert.False(await _limiter.TryAllowAsync("ip:1.2.3.4", 0, TimeSpan.FromMinutes(1)));
    }
}
