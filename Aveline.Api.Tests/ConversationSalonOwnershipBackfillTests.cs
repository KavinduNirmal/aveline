using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Testcontainers.PostgreSql;

namespace Aveline.Api.Tests;

/// <summary>
/// ADR-021 — the ownership migration must bind the pre-existing organization-wide general Salon
/// to the organization owner, while leaving customer-bound and inbound channel Salons
/// organization-shared.
///
/// This backfill is load-bearing: <c>OwnerUserId IS NULL</c> is what marks a Salon as shared, so
/// a general Salon left un-backfilled would stay visible to every member of the organization -
/// the exact leak the ADR closes.
/// </summary>
public class ConversationSalonOwnershipBackfillTests : IAsyncLifetime
{
    private const string PreOwnershipMigration =
        "20260913134710_AddOverrideNoOverlapConstraintAndPartialIdempotencyIndex";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("pgvector/pgvector:pg16")
        .WithDatabase("aveline_test")
        .WithUsername("aveline")
        .WithPassword("aveline_test_pw")
        .Build();

    private AppDbContext _context = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(
                _postgres.GetConnectionString(),
                npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .Options;

        _context = new AppDbContext(options);
        await _context.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS vector");

        // Stop one migration short so the backfill sees genuine pre-ADR-021 rows.
        await _context.Database.GetService<IMigrator>().MigrateAsync(PreOwnershipMigration);
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task Backfill_BindsGeneralSalonToOwner_AndLeavesSharedSalonsUnowned()
    {
        var ownerId = Guid.CreateVersion7();
        var orgId = Guid.CreateVersion7();
        var customerId = Guid.CreateVersion7();
        var generalSalonId = Guid.CreateVersion7();
        var customerSalonId = Guid.CreateVersion7();
        var channelSalonId = Guid.CreateVersion7();

        _context.Users.Add(new User
        {
            Id = ownerId,
            ClerkId = "clerk_owner_backfill",
            FirstName = "Owner",
            LastName = "Backfill",
            OrganizationId = orgId.ToString(),
            Email = "owner_backfill@aveline.lk",
            Username = "owner_backfill",
            UserRole = "Staff",
            OrganizationRole = "org:owner",
        });
        _context.Organizations.Add(new Organization
        {
            Id = orgId,
            Name = "Backfill Boutique",
            Slug = "backfill-boutique",
            OwnerUserId = ownerId,
        });
        await _context.SaveChangesAsync();

        // Conversations are written with raw SQL because the OwnerUserId column does not exist yet
        // at this migration point.
        await _context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "Conversations"
                ("Id", "OrganizationId", "Kind", "CustomerId", "ThreadId", "ExternalRef", "Status", "CreatedAt", "UpdatedAt")
            VALUES
                ({0}, {3}, 'Salon', NULL, 'thread-general', NULL, 'Active', now(), now()),
                ({1}, {3}, 'Salon', {4}, 'thread-customer', NULL, 'Active', now(), now()),
                ({2}, {3}, 'Salon', NULL, 'thread-channel', '+94771234567', 'Active', now(), now())
            """,
            generalSalonId, customerSalonId, channelSalonId, orgId, customerId);

        await _context.Database.MigrateAsync();

        var general = await _context.Conversations.AsNoTracking().SingleAsync(c => c.Id == generalSalonId);
        Assert.Equal(ownerId, general.OwnerUserId);

        var customerSalon = await _context.Conversations.AsNoTracking().SingleAsync(c => c.Id == customerSalonId);
        Assert.Null(customerSalon.OwnerUserId);

        // A channel Salon also has CustomerId == NULL, so the backfill must not sweep it up.
        var channelSalon = await _context.Conversations.AsNoTracking().SingleAsync(c => c.Id == channelSalonId);
        Assert.Null(channelSalon.OwnerUserId);
    }
}
