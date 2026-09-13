using System.Text.Json;
using Aveline.Api.Common.Jobs;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Audit.Repositories;
using Aveline.Api.Modules.Audit.Services;
using Aveline.Api.Modules.Billing.Domain;
using Aveline.Api.Modules.Billing.DTOs;
using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Billing.Repositories;
using Aveline.Api.Modules.Billing.Services;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Shared.Models;
using Aveline.Api.Modules.Statistics.Jobs;
using Aveline.Api.Modules.Statistics.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Aveline.Api.Tests;

/// <summary>
/// Reconciliation §1.4 — the paths added by the fix session whose PostgreSQL behaviour was
/// only ever asserted through the EF in-memory provider: the Blossom capture query (never
/// translated by Npgsql before) and the entitlement-override batch transaction.
/// </summary>
public class AdminReconciliationPostgresTests : IAsyncLifetime
{
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
        await _context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    private async Task<Guid> SeedOrganizationAsync(string suffix)
    {
        var owner = new User
        {
            Id = Guid.CreateVersion7(),
            ClerkId = $"pg_owner_{suffix}",
            Email = $"pg_owner_{suffix}@aveline.lk",
            FirstName = "Postgres",
            LastName = "Owner",
            Username = $"pg_owner_{suffix}",
            UserRole = Aveline.Api.Authorization.Roles.Admin,
            OrganizationRole = string.Empty,
            HasCompletedOnboarding = true,
            AccountState = AccountState.Active,
        };
        _context.Users.Add(owner);

        var organization = new Organization
        {
            Name = $"Postgres Org {suffix}",
            Slug = $"postgres-org-{suffix}",
            OwnerUserId = owner.Id,
        };
        _context.Organizations.Add(organization);
        await _context.SaveChangesAsync();
        return organization.Id;
    }

    private sealed class ExposedCollector(IServiceScopeFactory scopeFactory, IDistributedJobLock jobLock)
        : SystemMetricCollector(
            scopeFactory,
            jobLock,
            NullLogger<SystemMetricCollector>.Instance,
            new ConfigurationBuilder().Build())
    {
        public Task<MetricSnapshot> CaptureForTestAsync(
            IServiceProvider services, CancellationToken cancellationToken)
            => base.CaptureAsync(services, cancellationToken);
    }

    [Fact]
    public async Task CaptureBlossom_IsTranslatableByNpgsql_AndIgnoresClosedPeriods()
    {
        var now = DateTime.UtcNow;
        var organizationId = await SeedOrganizationAsync(Guid.NewGuid().ToString("N"));

        // A closed, overdrawn historical period. Closed rows are never deleted, so an
        // unfiltered Min(...) would latch blossom.balance.negative for ever (§3.3(b)).
        _context.UsageAccounts.Add(new UsageAccount
        {
            OrganizationId = organizationId,
            PeriodStart = now.AddDays(-60),
            PeriodEnd = now.AddDays(-30),
            IsClosed = true,
            ClosedAt = now.AddDays(-30),
            MonthlyBlossomLimit = 100m,
            BlossomUsed = 150m,
            BlossomRemaining = -50m,
        });

        var open = new UsageAccount
        {
            OrganizationId = organizationId,
            PeriodStart = now.AddDays(-1),
            PeriodEnd = now.AddDays(29),
            MonthlyBlossomLimit = 100m,
            BlossomUsed = 40m,
            BlossomRemaining = 60m,
        };
        _context.UsageAccounts.Add(open);
        await _context.SaveChangesAsync();

        _context.BlossomLedgerEntries.Add(new BlossomLedgerEntry
        {
            OrganizationId = organizationId,
            UsageAccountId = open.Id,
            EntryType = BlossomLedgerEntryType.AdminCredit,
            BlossomDelta = 20m,
            BlossomBalanceAfter = 60m,
            Reason = "Postgres drift translation probe.",
            SourceKind = BlossomSourceKind.System,
            CreatedAt = now,
        });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var services = new ServiceCollection();
        services.AddSingleton(_context);
        using var provider = services.BuildServiceProvider();

        var collector = new ExposedCollector(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new InMemoryDistributedJobLock());

        var snapshot = await collector.CaptureForTestAsync(provider, CancellationToken.None);

        Assert.Equal(0, collector.DatabaseReadFailures);
        Assert.Equal(60m, snapshot.BlossomBalance);
        Assert.NotNull(snapshot.BlossomReconciliationDrift);
    }

    [Fact]
    public async Task SetOverrides_RollsBackAnEarlierEntryWhenALaterOneIsRejected()
    {
        var organizationId = await SeedOrganizationAsync(Guid.NewGuid().ToString("N"));
        var service = new EntitlementOverrideService(
            _context,
            new EntitlementRepository(_context),
            new EntitlementResolver(new EntitlementRepository(_context)),
            new AuditService(
                new AuditRepository(_context), new AuditRedactor(),
                new HttpContextAccessor(), NullLogger<AuditService>.Instance));

        EntitlementOverrideInput Override(DateTime from, decimal value) => new(
            "staff.max",
            "Integer",
            JsonSerializer.SerializeToElement(value),
            "Postgres override batch probe.",
            from,
            EffectiveTo: null);

        var overrides = new[]
        {
            Override(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), 50m),
            // The second entry overlaps the first, so the batch must be refused after the
            // first write has already been flushed inside the transaction.
            Override(new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc), 75m),
        };

        await Assert.ThrowsAsync<EntitlementOverrideValidationException>(
            () => service.SetOverridesAsync(organizationId, Guid.CreateVersion7(), overrides));

        _context.ChangeTracker.Clear();

        Assert.Empty(await _context.PlanEntitlementOverrides
            .Where(overrideRow => overrideRow.OrganizationId == organizationId)
            .ToListAsync());
        Assert.Empty(await _context.AuditLogEntries
            .Where(entry => entry.OrganizationId == organizationId)
            .ToListAsync());
    }

    [Fact]
    public async Task OverrideNoOverlapConstraint_RejectsOverlappingRowsInTheDatabase()
    {
        var organizationId = await SeedOrganizationAsync(Guid.NewGuid().ToString("N"));

        _context.PlanEntitlementOverrides.Add(Override(organizationId, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        await _context.SaveChangesAsync();

        // A second window with a different start that overlaps the first. The application
        // check refuses this through the API; the exclusion constraint must refuse it even
        // when two concurrent writers race past that check (§2.4).
        _context.PlanEntitlementOverrides.Add(Override(organizationId, new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc)));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => _context.SaveChangesAsync());
        var postgres = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal("23P01", postgres.SqlState);
    }

    private static PlanEntitlementOverride Override(Guid organizationId, DateTime effectiveFrom) => new()
    {
        OrganizationId = organizationId,
        Key = "staff.max",
        ValueType = EntitlementValueType.Integer,
        ValueDecimal = 50m,
        EffectiveFrom = effectiveFrom,
        Reason = "Postgres override constraint probe.",
        CreatedByUserId = Guid.CreateVersion7(),
    };
}
