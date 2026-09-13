using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Infrastructure.Eventing;
using Aveline.Api.Modules.Audit.Repositories;
using Aveline.Api.Modules.Audit.Services;
using Aveline.Api.Modules.Billing.Domain;
using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Billing.Repositories;
using Aveline.Api.Modules.Billing.Services;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Aveline.Api.Tests;

/// <summary>
/// Issue #199 — PostgreSQL-backed invariants the in-memory provider cannot exercise:
/// lost-update freedom, the xmin concurrency token, the filtered unique idempotency
/// index and the balance CHECK.
/// </summary>
public class LedgerPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("pgvector/pgvector:pg16")
        .WithDatabase("aveline_test")
        .WithUsername("aveline")
        .WithPassword("aveline_test_pw")
        .Build();

    private DbContextOptions<AppDbContext> _options = null!;
    private Guid _orgId;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(
                _postgres.GetConnectionString(),
                npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .Options;

        await using var context = new AppDbContext(_options);
        await context.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS vector");
        await context.Database.MigrateAsync();
        _orgId = await SeedOrganizationAsync(context);
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private static async Task<Guid> SeedOrganizationAsync(AppDbContext context)
    {
        var ownerId = Guid.CreateVersion7();
        context.Users.Add(new User
        {
            Id = ownerId, ClerkId = $"pg_{ownerId:N}", Email = "pg@aveline.lk", FirstName = "Pg", LastName = "Owner",
            Username = $"pg_{ownerId:N}", UserRole = "owner", OrganizationRole = "org:boutique_owner",
        });
        var org = new Organization { Name = "Pg Boutique", Slug = $"pg-{ownerId:N}", OwnerUserId = ownerId };
        context.Organizations.Add(org);
        await context.SaveChangesAsync();
        return org.Id;
    }

    private BlossomService CreateService(AppDbContext context)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Billing:MaxAdjustmentBlossoms"] = "100000" })
            .Build();

        return new BlossomService(
            new BlossomLedgerRepository(context),
            new UsageRepository(context),
            new EntitlementResolver(new EntitlementRepository(context)),
            new InMemoryEventBus(),
            new AuditService(
                new AuditRepository(context), new AuditRedactor(),
                new HttpContextAccessor(), NullLogger<AuditService>.Instance),
            configuration,
            NullLogger<BlossomService>.Instance);
    }

    [Fact]
    public async Task TwentyParallelCredits_ProduceTheExactArithmeticSum()
    {
        await using (var seed = new AppDbContext(_options))
        {
            await CreateService(seed).GetBalanceAsync(_orgId);
        }

        var credits = Enumerable.Range(0, 20).Select(index => Task.Run(async () =>
        {
            await using var context = new AppDbContext(_options);
            await CreateService(context).CreditAsync(new CreditBlossomsCommand(
                _orgId, 10m, $"Parallel credit number {index}.", null,
                BlossomSourceKind.Admin, null, Guid.CreateVersion7(), null, null));
        }));

        await Task.WhenAll(credits);

        await using var verify = new AppDbContext(_options);
        var balance = await CreateService(verify).GetBalanceAsync(_orgId);

        Assert.Equal(200m, balance.BlossomGranted);
        Assert.Equal(350m, balance.BlossomRemaining); // 150 Seed allowance + 200
        Assert.Equal(20, await verify.BlossomLedgerEntries.CountAsync());
    }

    [Fact]
    public async Task XminConcurrencyToken_RaisesOnStaleUpdate()
    {
        await using (var seed = new AppDbContext(_options))
        {
            await CreateService(seed).GetBalanceAsync(_orgId);
        }

        await using var first = new AppDbContext(_options);
        await using var second = new AppDbContext(_options);

        var firstAccount = await first.UsageAccounts.SingleAsync(a => a.OrganizationId == _orgId);
        var secondAccount = await second.UsageAccounts.SingleAsync(a => a.OrganizationId == _orgId);

        firstAccount.BlossomUsed += 1m;
        firstAccount.BlossomRemaining -= 1m;
        await first.SaveChangesAsync();

        secondAccount.BlossomUsed += 1m;
        secondAccount.BlossomRemaining -= 1m;

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task FilteredUniqueIdempotencyIndex_RejectsDuplicateKey()
    {
        await using var context = new AppDbContext(_options);
        await CreateService(context).GetBalanceAsync(_orgId);

        var accountId = (await context.UsageAccounts.SingleAsync(a => a.OrganizationId == _orgId)).Id;

        context.BlossomLedgerEntries.Add(Grant(_orgId, accountId, "key-1"));
        await context.SaveChangesAsync();

        context.BlossomLedgerEntries.Add(Grant(_orgId, accountId, "key-1"));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        var postgres = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal("23505", postgres.SqlState);
    }

    [Fact]
    public async Task BalanceCheck_RejectsDesynchronisedProjection()
    {
        await using var context = new AppDbContext(_options);
        await CreateService(context).GetBalanceAsync(_orgId);

        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            context.Database.ExecuteSqlRawAsync(
                "UPDATE \"UsageAccounts\" SET \"BlossomRemaining\" = \"BlossomRemaining\" + 1 WHERE \"OrganizationId\" = {0}",
                _orgId));

        Assert.Equal("23514", exception.SqlState);
    }

    private static BlossomLedgerEntry Grant(Guid orgId, Guid accountId, string key) => new()
    {
        OrganizationId = orgId,
        UsageAccountId = accountId,
        EntryType = BlossomLedgerEntryType.AdminCredit,
        BlossomDelta = 5m,
        BlossomBalanceAfter = 5m,
        Reason = "Idempotency index test grant.",
        IdempotencyKey = key,
        IdempotencyScope = "admin.blossoms.credit",
        CreatedAt = DateTime.UtcNow,
    };
}
