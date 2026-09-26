using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Commerce.Models;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Aveline.Api.Tests;

/// <summary>
/// ADR-024, Decision 4: a pause is actionable only if it is linked. These are the PostgreSQL-backed
/// invariants the in-memory provider cannot exercise - the migration's <c>NOT NULL</c>, its backfill
/// and the filtered unique index - so they prove the **migration** enforces them rather than the
/// service happening to.
/// </summary>
/// <remarks>
/// The backfill gets its own test by migrating to the *previous* migration, inserting a legacy row
/// with a null thread exactly as it would have existed, and then migrating forward. Without that the
/// only thing the backfill could be checked against is a database that never had the problem.
/// </remarks>
public class ApprovalQueueThreadLinkagePostgresTests : IAsyncLifetime
{
    private const string PreviousMigration = "20260922171904_AddColorHexToInventoryItems";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("pgvector/pgvector:pg16")
        .WithDatabase("aveline_test")
        .WithUsername("aveline")
        .WithPassword("aveline_test_pw")
        .Build();

    private DbContextOptions<AppDbContext> _options = null!;

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
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private async Task<Guid> SeedOrderAsync(AppDbContext context, Guid? orderId = null)
    {
        var ownerId = Guid.CreateVersion7();
        context.Users.Add(new User
        {
            Id = ownerId,
            ClerkId = $"aq_{ownerId:N}",
            Email = "aq@aveline.lk",
            FirstName = "A",
            LastName = "Q",
            Username = $"aq_{ownerId:N}",
            UserRole = "owner",
            OrganizationRole = "org:boutique_owner",
        });

        var org = new Organization
        {
            Name = "Approval Queue Linkage",
            Slug = $"aq-pg-{ownerId:N}",
            OwnerUserId = ownerId,
        };
        context.Organizations.Add(org);

        var order = new Order
        {
            Id = orderId ?? Guid.CreateVersion7(),
            OrganizationId = org.Id,
            CustomerId = Guid.CreateVersion7(),
            CustomerName = "Linkage Customer",
            Status = "pending_approval",
            Subtotal = 75000m,
            Total = 75000m,
            TotalCost = 65000m,
            Margin = 0.1333m,
            CreatedAt = DateTime.UtcNow,
        };
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return order.Id;
    }

    private static ApprovalQueueEntry Entry(Guid orderId, string threadId, string status = "pending") => new()
    {
        Id = Guid.CreateVersion7(),
        OrganizationId = Guid.CreateVersion7(), // overwritten by the caller through the order's org
        OrderId = orderId,
        ApprovalType = "high_value_order",
        Status = status,
        Reason = "Order total exceeds the high-value threshold",
        ThreadId = threadId,
        CreatedAt = DateTime.UtcNow,
    };

    private async Task<(Guid OrgId, Guid OrderId)> SeedAsync(AppDbContext context)
    {
        var orderId = await SeedOrderAsync(context);
        var orgId = await context.Orders.Where(o => o.Id == orderId).Select(o => o.OrganizationId).SingleAsync();
        return (orgId, orderId);
    }

    [Fact]
    public async Task TheThreadIdColumn_RefusesNull()
    {
        await using var context = new AppDbContext(_options);
        var (orgId, orderId) = await SeedAsync(context);

        var entry = Entry(orderId, "thread-not-null");
        entry.OrganizationId = orgId;
        context.ApprovalQueue.Add(entry);
        await context.SaveChangesAsync();

        // Raw SQL on purpose: the C# property is non-nullable, so only the database can be asked
        // whether the constraint actually exists.
        var failure = await Assert.ThrowsAsync<PostgresException>(() =>
            context.Database.ExecuteSqlRawAsync(
                """UPDATE "ApprovalQueue" SET "ThreadId" = NULL WHERE "Id" = {0}""",
                entry.Id));

        Assert.Equal(PostgresErrorCodes.NotNullViolation, failure.SqlState);
    }

    [Fact]
    public async Task ASecondPendingApprovalOnTheSameThread_IsRejectedByTheDatabase()
    {
        // Invariant A4: creating an order when a run pauses is a side effect of a run that may be
        // retried, and a second order for one pause would double every figure derived from it. The
        // service checks first; this is the guarantee that holds when two writes race.
        await using var context = new AppDbContext(_options);
        var (orgId, orderId) = await SeedAsync(context);

        var first = Entry(orderId, "thread-race");
        first.OrganizationId = orgId;
        context.ApprovalQueue.Add(first);
        await context.SaveChangesAsync();

        var secondOrderId = await SeedOrderAsync(context);
        var second = Entry(secondOrderId, "thread-race");
        second.OrganizationId = orgId;
        context.ApprovalQueue.Add(second);

        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.Equal(
            PostgresErrorCodes.UniqueViolation,
            Assert.IsType<PostgresException>(failure.InnerException).SqlState);
    }

    [Fact]
    public async Task ASecondApprovalOnTheSameThread_IsAllowedOnceTheFirstIsDecided()
    {
        // A thread may legitimately place another order after the first has been ruled on, so the
        // guard is scoped to `pending` rows rather than to the thread.
        await using var context = new AppDbContext(_options);
        var (orgId, orderId) = await SeedAsync(context);

        var first = Entry(orderId, "thread-reused");
        first.OrganizationId = orgId;
        context.ApprovalQueue.Add(first);
        await context.SaveChangesAsync();

        first.Status = "approved";
        await context.SaveChangesAsync();

        var secondOrderId = await SeedOrderAsync(context);
        var second = Entry(secondOrderId, "thread-reused");
        second.OrganizationId = orgId;
        context.ApprovalQueue.Add(second);

        await context.SaveChangesAsync();

        Assert.Equal(2, await context.ApprovalQueue.CountAsync(a => a.ThreadId == "thread-reused"));
    }

    [Fact]
    public async Task TheMigration_BackfillsLegacyRowsWithAValueThatNamesNoCheckpoint()
    {
        await using var context = new AppDbContext(_options);
        var migrator = context.Database.GetService<IMigrator>();

        // Rebuild the world as it was before this migration: nullable thread, one pending row with
        // none set. This is the only way to test a backfill honestly.
        await migrator.MigrateAsync(PreviousMigration);

        var orderId = await SeedOrderAsync(context);
        var orgId = await context.Orders.Where(o => o.Id == orderId).Select(o => o.OrganizationId).SingleAsync();
        var legacyId = Guid.CreateVersion7();

        await context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "ApprovalQueue"
                ("Id", "OrganizationId", "OrderId", "ApprovalType", "Status",
                 "ThresholdExceeded", "Reason", "ThreadId", "CreatedAt")
            VALUES ({0}, {1}, {2}, 'order_approval', 'pending', true, 'legacy row', NULL, now())
            """,
            legacyId, orgId, orderId);

        await migrator.MigrateAsync();

        var backfilled = await context.ApprovalQueue.AsNoTracking().SingleAsync(a => a.Id == legacyId);

        // Distinct per row (so the new unique index cannot collide) and obviously synthetic (so
        // nothing tries to resume a checkpoint that was never written).
        Assert.Equal($"legacy-{legacyId}", backfilled.ThreadId);
    }

    [Fact]
    public void TheMigrationFile_BackfillsBeforeForcingNotNull()
    {
        // The scaffolder's proposal was the empty string, which is not a thread id: every legacy row
        // would have collided on the new unique index. This pins the order of the two statements,
        // because the wrong order only shows up against data that no fresh database has.
        var migrationPath = Path.Combine(
            RepositoryRoot(),
            "Aveline.Api", "Migrations", "20260922234823_AddApprovalThreadLinkage.cs");
        var source = File.ReadAllText(migrationPath);

        var updateIndex = source.IndexOf("UPDATE \"ApprovalQueue\"", StringComparison.Ordinal);
        var alterIndex = source.IndexOf("AlterColumn<string>", StringComparison.Ordinal);

        Assert.True(updateIndex > 0, "the migration must backfill legacy rows");
        Assert.True(alterIndex > 0, "the migration must make the column NOT NULL");
        Assert.True(updateIndex < alterIndex, "the backfill must run before the column becomes NOT NULL");
        Assert.Contains("legacy-", source);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Aveline.Api", "Aveline.Api.csproj")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
