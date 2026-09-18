using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Billing.Repositories;
using Aveline.Api.Modules.Billing.Services;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Tests;

/// <summary>
/// Issue #192 — idempotency replay semantics (FR-2.7, BR-2.8, BR-2.9).
/// </summary>
public class IdempotencyStoreTests
{
    private static readonly DateTime Now = new(2026, 9, 15, 0, 0, 0, DateTimeKind.Utc);

    private readonly AppDbContext _context;

    public IdempotencyStoreTests()
    {
        _context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"Idempotency_{Guid.NewGuid()}")
            .Options);
    }

    private IdempotencyService CreateService() => new(new IdempotencyRepository(_context));

    [Fact]
    public async Task TryReplayAsync_UnknownKey_ReturnsNull()
    {
        var service = CreateService();

        var replay = await service.TryReplayAsync(
            Guid.CreateVersion7(), "POST /x", "POST", "key-1", "hash-a", Now);

        Assert.Null(replay);
    }

    [Fact]
    public async Task TryReplayAsync_MatchingHash_ReturnsStoredResponse()
    {
        var orgId = Guid.CreateVersion7();
        var service = CreateService();
        await service.SaveAsync(orgId, "POST /x", "POST", "key-1", "hash-a", 201, "{\"ok\":true}", Now);

        var replay = await service.TryReplayAsync(orgId, "POST /x", "POST", "key-1", "hash-a", Now);

        Assert.NotNull(replay);
        Assert.Equal(201, replay!.Status);
        Assert.Equal("{\"ok\":true}", replay.Body);
    }

    [Fact]
    public async Task TryReplayAsync_DifferentHash_ThrowsKeyReuse()
    {
        var orgId = Guid.CreateVersion7();
        var service = CreateService();
        await service.SaveAsync(orgId, "POST /x", "POST", "key-1", "hash-a", 201, "{}", Now);

        await Assert.ThrowsAsync<IdempotencyKeyReuseException>(() =>
            service.TryReplayAsync(orgId, "POST /x", "POST", "key-1", "hash-b", Now));
    }

    [Fact]
    public async Task TryReplayAsync_FailedResponse_IsNotReplayed()
    {
        var orgId = Guid.CreateVersion7();
        var service = CreateService();
        await service.SaveAsync(
            orgId, "POST /x", "POST", "key-failed", "hash-a", 409,
            "{\"code\":\"insufficient-balance\"}", Now);

        var replay = await service.TryReplayAsync(orgId, "POST /x", "POST", "key-failed", "hash-a", Now);

        Assert.Null(replay);
    }

    [Fact]
    public async Task TryReplayAsync_ExpiredRecord_IsTreatedAsUnknown()
    {
        var orgId = Guid.CreateVersion7();
        var service = CreateService();
        await service.SaveAsync(orgId, "POST /x", "POST", "key-1", "hash-a", 201, "{}", Now.AddHours(-25));

        var replay = await service.TryReplayAsync(orgId, "POST /x", "POST", "key-1", "hash-a", Now);

        Assert.Null(replay);
    }

    [Fact]
    public async Task TryReplayAsync_DifferentHttpMethod_IsNotAReplay()
    {
        var orgId = Guid.CreateVersion7();
        var service = CreateService();
        await service.SaveAsync(orgId, "POST /x", "POST", "key-1", "hash-a", 201, "{}", Now);

        // H-1(d): the unique key includes HttpMethod, so a DELETE with the same key and
        // endpoint is a distinct operation and must not replay the POST response.
        var replay = await service.TryReplayAsync(orgId, "POST /x", "DELETE", "key-1", "hash-a", Now);

        Assert.Null(replay);
    }

    [Fact]
    public void ComputeHash_IsStableAndCanonicalisesWhitespace()
    {
        var a = IdempotencyService.ComputeHash("{\"amount\":250.0,\"reason\":\"x\"}");
        var b = IdempotencyService.ComputeHash("{\n  \"amount\":250.0,\n  \"reason\":\"x\"\n}");
        var c = IdempotencyService.ComputeHash("{\"amount\":251.0,\"reason\":\"x\"}");

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.Equal(64, a.Length);
    }

    [Fact]
    public async Task DeleteExpiredAsync_RemovesOnlyExpiredRecords()
    {
        var service = CreateService();
        await service.SaveAsync(Guid.CreateVersion7(), "POST /x", "POST", "fresh", "h", 200, "{}", Now);
        await service.SaveAsync(Guid.CreateVersion7(), "POST /x", "POST", "stale", "h", 200, "{}", Now.AddHours(-25));

        var deleted = await service.DeleteExpiredAsync(Now);

        Assert.Equal(1, deleted);
        Assert.Equal(1, await _context.IdempotencyRecords.CountAsync());
    }
}
