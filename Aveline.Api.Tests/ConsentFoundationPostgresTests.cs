using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.CustomerConcierge.Models;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Aveline.Api.Tests;

/// <summary>
/// Phase 0 consent foundation, verified the only way the schema can be: by applying the real
/// migrations to a real PostgreSQL container and reading the resulting catalog. The in-memory
/// provider accepts almost any model, so it cannot prove the migration (0.4, 0.5, 0.6) applied.
///
/// Follows <c>IncomeLedgerPostgresTests</c>: a pgvector image (the app's migrations create a
/// vector column), <c>CREATE EXTENSION vector</c>, then <c>MigrateAsync</c>.
/// </summary>
public class ConsentFoundationPostgresTests : IAsyncLifetime
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
        await context.Database.MigrateAsync();
        await SeedAsync(context);
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private async Task SeedAsync(AppDbContext context)
    {
        var ownerId = Guid.CreateVersion7();
        context.Users.Add(new User
        {
            Id = ownerId,
            ClerkId = $"consent_pg_{ownerId:N}",
            Email = "consent-pg@aveline.lk",
            FirstName = "Consent",
            LastName = "Owner",
            Username = $"consent_pg_{ownerId:N}",
            UserRole = "owner",
            OrganizationRole = "org:boutique_owner",
        });
        var org = new Organization
        {
            Name = "Consent Pg Boutique",
            Slug = $"consent-pg-{ownerId:N}",
            OwnerUserId = ownerId,
        };
        context.Organizations.Add(org);
        await context.SaveChangesAsync();
        _orgId = org.Id;

        var customer = new Customer
        {
            OrganizationId = org.Id,
            PhoneNumber = "+94779000123",
            FullName = "Consent Pg Client",
            Status = "new",
        };
        context.Customers.Add(customer);
        await context.SaveChangesAsync();
        _customerId = customer.Id;
    }

    /// <summary>Strips the quoting so an index definition can be matched by column list.</summary>
    private static string Normalize(string indexDefinition) => indexDefinition.Replace("\"", string.Empty);

    [Fact]
    public async Task TheMigrationAddsThePrivacyColumnsAndDropsThePlaintextToken()
    {
        await using var context = new AppDbContext(_options);

        var columns = await context.Database
            .SqlQuery<string>(
                $"SELECT column_name AS \"Value\" FROM information_schema.columns WHERE table_name = 'CustomerConsent'")
            .ToListAsync();

        Assert.Contains("ConsentStatus", columns);
        Assert.Contains("ConsentGrantedAt", columns);
        Assert.Contains("ConsentRevokedAt", columns);
        // 0.4: the additive privacy columns.
        Assert.Contains("ConsentSource", columns);
        Assert.Contains("GlobalSubjectId", columns);
        Assert.Contains("DisclosureShownAt", columns);
        Assert.Contains("DisclosureVersion", columns);
        // D-1: the dead plaintext token column is gone.
        Assert.DoesNotContain("RevokeToken", columns);
        // Pr3 uses a stateless HMAC link, so no stored token hash was introduced.
        Assert.DoesNotContain("RevokeTokenHash", columns);
    }

    [Fact]
    public async Task TheMigrationCreatesTheAppendOnlyConsentAuditTable()
    {
        await using var context = new AppDbContext(_options);

        var tableCount = await context.Database
            .SqlQuery<int>(
                $"SELECT COUNT(*) AS \"Value\" FROM information_schema.tables WHERE table_name = 'ConsentAuditEntries'")
            .SingleAsync();
        Assert.Equal(1, tableCount);

        var columns = await context.Database
            .SqlQuery<string>(
                $"SELECT column_name AS \"Value\" FROM information_schema.columns WHERE table_name = 'ConsentAuditEntries'")
            .ToListAsync();

        Assert.Contains("OrganizationId", columns);
        Assert.Contains("CustomerId", columns);
        Assert.Contains("Action", columns);
        Assert.Contains("PreviousStatus", columns);
        Assert.Contains("NewStatus", columns);
        Assert.Contains("Source", columns);
        Assert.Contains("ActorKind", columns);
        Assert.Contains("ActorUserId", columns);
        Assert.Contains("ActorRef", columns);
        Assert.Contains("EvidenceJson", columns);
        Assert.Contains("IpHash", columns);
        Assert.Contains("UserAgent", columns);
        Assert.Contains("CreatedAt", columns);

        var indexes = await context.Database
            .SqlQuery<string>(
                $"SELECT indexdef AS \"Value\" FROM pg_indexes WHERE tablename = 'ConsentAuditEntries'")
            .ToListAsync();

        // 0.6: the staff/agent timeline, and the cross-org view an erasure request needs.
        Assert.Contains(indexes, definition =>
            Normalize(definition).Contains("(OrganizationId, CustomerId, CreatedAt DESC)"));
        Assert.Contains(indexes, definition =>
            Normalize(definition).Contains("(CustomerId, CreatedAt DESC)"));
    }

    [Fact]
    public async Task EvidenceJson_IsJsonbAndDefaultsToAnEmptyObject()
    {
        await using var context = new AppDbContext(_options);

        var dataType = await context.Database
            .SqlQuery<string>(
                $"SELECT data_type AS \"Value\" FROM information_schema.columns WHERE table_name = 'ConsentAuditEntries' AND column_name = 'EvidenceJson'")
            .SingleAsync();
        Assert.Equal("jsonb", dataType);

        var columnDefault = await context.Database
            .SqlQuery<string>(
                $"SELECT column_default AS \"Value\" FROM information_schema.columns WHERE table_name = 'ConsentAuditEntries' AND column_name = 'EvidenceJson'")
            .SingleAsync();
        Assert.Contains("{}", columnDefault);

        // A row written without evidence gets the empty object, never NULL.
        await context.Database.ExecuteSqlRawAsync(
            "INSERT INTO \"ConsentAuditEntries\" (\"Id\",\"OrganizationId\",\"CustomerId\",\"Action\",\"NewStatus\",\"Source\",\"ActorKind\",\"CreatedAt\") VALUES ({0},{1},{2},{3},{4},{5},{6},{7})",
            Guid.CreateVersion7(), _orgId, _customerId, "consent.row.created", "pending", "system", "system", DateTime.UtcNow);

        var evidence = await context.Database
            .SqlQuery<string>($"SELECT \"EvidenceJson\"::text AS \"Value\" FROM \"ConsentAuditEntries\"")
            .SingleAsync();
        Assert.Equal("{}", evidence);
    }

    [Fact]
    public async Task TheCustomerForeignKeyCascadesAndTheOrganizationForeignKeyRestricts()
    {
        await using var context = new AppDbContext(_options);

        var behaviours = await context.Database
            .SqlQuery<string>(
                $"SELECT confdeltype::text AS \"Value\" FROM pg_constraint WHERE conrelid = '\"ConsentAuditEntries\"'::regclass AND contype = 'f'")
            .ToListAsync();

        Assert.Contains("c", behaviours); // ON DELETE CASCADE from Customers
        Assert.Contains("r", behaviours); // ON DELETE RESTRICT from Organizations
    }

    [Fact]
    public async Task TheConsentStatusIndexExists()
    {
        await using var context = new AppDbContext(_options);

        var indexes = await context.Database
            .SqlQuery<string>(
                $"SELECT indexdef AS \"Value\" FROM pg_indexes WHERE tablename = 'CustomerConsent'")
            .ToListAsync();

        // 0.5: "count by status within an org".
        Assert.Contains(indexes, definition =>
            Normalize(definition).Contains("(OrganizationId, ConsentStatus)"));
    }
}
