using Aveline.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Testcontainers.PostgreSql;

namespace Aveline.Api.Tests;

/// <summary>
/// Item 3.2 (plan §4.2/§4.4): the data-only migration backfills <c>DisclosureShownAt = CreatedAt</c>
/// for every consent row that existed before the feature shipped, so a deploy does not mass-message
/// every historical customer on their next inbound message.
/// <para>
/// The only way to prove a data step ran is to run it against real PostgreSQL at the schema that
/// existed before it. The container is migrated up to the migration immediately preceding the
/// backfill, genuine pre-existing rows are inserted with raw SQL (the model reflects the *latest*
/// schema), and then the remaining migrations - including the backfill - are applied.
/// </para>
/// </summary>
public class DisclosureBackfillPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("pgvector/pgvector:pg16")
        .WithDatabase("aveline_test")
        .WithUsername("aveline")
        .WithPassword("aveline_test_pw")
        .Build();

    private DbContextOptions<AppDbContext> _options = null!;
    private Guid _orgId;
    private Guid _customerId;

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

        // Stop at the migration immediately before the backfill, so the data step sees genuine
        // pre-existing rows. Resolved from the assembly rather than hard-coded, so a concurrent
        // migration landing after ours cannot silently change which schema this test exercises.
        var target = MigrationBeforeTheBackfill(context);
        await context.Database.GetService<IMigrator>().MigrateAsync(target);

        await SeedPreExistingRowsAsync(context);
    }

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
    }

    /// <summary>The last migration before the disclosure backfill, found from the applied order.</summary>
    private static string MigrationBeforeTheBackfill(AppDbContext context)
    {
        var migrations = context.Database.GetMigrations().ToList();
        var index = migrations.FindIndex(
            id => id.EndsWith("_BackfillDisclosureShownAt", StringComparison.Ordinal));
        if (index <= 0)
        {
            throw new InvalidOperationException(
                "The BackfillDisclosureShownAt migration was not found in the assembly.");
        }

        return migrations[index - 1];
    }

    private async Task SeedPreExistingRowsAsync(AppDbContext context)
    {
        var userId = Guid.CreateVersion7();
        _orgId = Guid.CreateVersion7();
        _customerId = Guid.CreateVersion7();
        var now = DateTime.UtcNow;

        await context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "Users" ("Id","ClerkId","FirstName","LastName","OrganizationId","Email","Username",
              "PhoneNumber","UserRole","OrganizationRole","ContactPreference","CreatedAt","UpdatedAt")
            VALUES ({0}, {1}, 'Backfill', 'Owner', 'org_legacy', 'disclosure-backfill@aveline.lk', {2},
              '+94770000111', 'owner', 'org:boutique_owner', 'Email', {3}, {3})
            """,
            userId, $"clerk_{userId:N}", $"disclosure_backfill_{userId:N}", now);

        await context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "Organizations" ("Id","Name","Slug","OwnerUserId","CreatedAt","UpdatedAt","PlanTier")
            VALUES ({0}, 'Backfill Boutique', {1}, {2}, {3}, {3}, 'Bloom')
            """,
            _orgId, $"disclosure-backfill-{_orgId:N}", userId, now);

        await context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "Customers" ("Id","OrganizationId","PhoneNumber","Status","TotalSpent","VisitCount",
              "CreatedAt","UpdatedAt")
            VALUES ({0}, {1}, '+94779000456', 'new', 0, 0, {2}, {2})
            """,
            _customerId, _orgId, now);
    }

    /// <summary>Inserts a consent row at the pre-backfill schema, returning its id.</summary>
    private async Task<Guid> InsertConsentAsync(Guid customerId, DateTime? disclosureShownAt)
    {
        await using var context = new AppDbContext(_options);
        var consentId = Guid.CreateVersion7();
        var createdAt = new DateTime(2026, 1, 15, 9, 30, 0, DateTimeKind.Utc);

        await context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "CustomerConsent" ("Id","OrganizationId","CustomerId","ConsentStatus","CreatedAt",
              "UpdatedAt","DisclosureShownAt")
            VALUES ({0}, {1}, {2}, 'pending', {3}, {3}, {4})
            """,
            consentId, _orgId, customerId, createdAt, disclosureShownAt);

        return consentId;
    }

    [Fact]
    public async Task TheBackfillStampsEveryPreExistingConsentRowThatWasNotAlreadyDisclosed()
    {
        // Two pre-existing customers who have never been disclosed to.
        var neverDisclosed = Guid.CreateVersion7();
        var neverDisclosedConsent = Guid.CreateVersion7();
        var createdAt = new DateTime(2026, 1, 15, 9, 30, 0, DateTimeKind.Utc);

        await using (var context = new AppDbContext(_options))
        {
            await context.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "Customers" ("Id","OrganizationId","PhoneNumber","Status","TotalSpent","VisitCount",
                  "CreatedAt","UpdatedAt")
                VALUES ({0}, {1}, '+94779000777', 'new', 0, 0, {2}, {2})
                """,
                neverDisclosed, _orgId, createdAt);

            await context.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "CustomerConsent" ("Id","OrganizationId","CustomerId","ConsentStatus","CreatedAt",
                  "UpdatedAt","DisclosureShownAt")
                VALUES ({0}, {1}, {2}, 'pending', {3}, {3}, NULL)
                """,
                neverDisclosedConsent, _orgId, neverDisclosed, createdAt);

            // Apply the rest of the migrations, including the backfill.
            await context.Database.MigrateAsync();
        }

        await using var verify = new AppDbContext(_options);

        var rows = await verify.CustomerConsents
            .AsNoTracking()
            .Where(c => c.OrganizationId == _orgId)
            .ToListAsync();

        var backfilled = rows.Single(r => r.Id == neverDisclosedConsent);
        Assert.NotNull(backfilled.DisclosureShownAt);
        Assert.Equal(createdAt, backfilled.DisclosureShownAt!.Value);
        // Nothing was actually shown, so no version is claimed for the historical row.
        Assert.Null(backfilled.DisclosureVersion);

        // The acceptance criterion: not one pre-existing row is left with a null stamp.
        Assert.DoesNotContain(rows, r => r.DisclosureShownAt is null);
    }

    [Fact]
    public async Task TheBackfillLeavesAnAlreadyDisclosedRowAlone()
    {
        var shownAt = new DateTime(2025, 12, 1, 8, 0, 0, DateTimeKind.Utc);
        // The seeded customer from InitializeAsync, so the consent row's FK is satisfied.
        var consentId = await InsertConsentAsync(_customerId, shownAt);

        await using (var context = new AppDbContext(_options))
        {
            await context.Database.MigrateAsync();
        }

        await using var verify = new AppDbContext(_options);
        var row = await verify.CustomerConsents.AsNoTracking().SingleAsync(c => c.Id == consentId);

        // An idempotent backfill must not rewrite a real disclosure timestamp.
        Assert.Equal(shownAt, row.DisclosureShownAt!.Value);
    }

    [Fact]
    public async Task TheBackfillDoesNotStampRowsCreatedAfterItRan()
    {
        await using (var context = new AppDbContext(_options))
        {
            await context.Database.MigrateAsync();
        }

        // A genuinely new customer after the deploy has no stamp - that is what makes the next
        // inbound message trigger their disclosure.
        var newCustomer = Guid.CreateVersion7();
        await using (var context = new AppDbContext(_options))
        {
            await context.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "Customers" ("Id","OrganizationId","PhoneNumber","Status","TotalSpent","VisitCount",
                  "CreatedAt","UpdatedAt")
                VALUES ({0}, {1}, '+94779000888', 'new', 0, 0, {2}, {2})
                """,
                newCustomer, _orgId, DateTime.UtcNow);
            await context.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "CustomerConsent" ("Id","OrganizationId","CustomerId","ConsentStatus","CreatedAt",
                  "UpdatedAt","DisclosureShownAt")
                VALUES ({0}, {1}, {2}, 'pending', {3}, {3}, NULL)
                """,
                Guid.CreateVersion7(), _orgId, newCustomer, DateTime.UtcNow);
        }

        await using var verify = new AppDbContext(_options);
        var row = await verify.CustomerConsents.AsNoTracking().SingleAsync(c => c.CustomerId == newCustomer);
        Assert.Null(row.DisclosureShownAt);
    }
}
