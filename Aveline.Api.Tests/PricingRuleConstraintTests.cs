using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Billing.Domain;
using Aveline.Api.Modules.Billing.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Aveline.Api.Tests;

/// <summary>
/// Issue #184 — the database-enforced non-overlap invariant (BR-1.3). The in-memory
/// provider cannot exercise an exclusion constraint, so this runs against PostgreSQL.
/// </summary>
public class PricingRuleConstraintTests : IAsyncLifetime
{
    private static readonly DateTime WindowStart = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

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

    private static BlossomConversionRule Rule(
        DateTime effectiveFrom,
        DateTime? effectiveTo,
        BlossomRuleStatus status = BlossomRuleStatus.Active) => new()
    {
        ScopeKind = BlossomRuleScopeKind.Global,
        UnitsPerBlossom = 1000,
        MinimumChargeBlossoms = 0.1m,
        RoundingMode = BlossomRoundingMode.Ceiling,
        RoundingDecimals = 1,
        EffectiveFrom = effectiveFrom,
        EffectiveTo = effectiveTo,
        Status = status,
        ChangeReason = "Constraint test rule.",
        CreatedByUserId = Guid.CreateVersion7(),
    };

    [Fact]
    public async Task OverlappingActiveWindows_AreRejectedByTheDatabase()
    {
        _context.BlossomConversionRules.Add(Rule(WindowStart, WindowStart.AddDays(30)));
        await _context.SaveChangesAsync();

        _context.BlossomConversionRules.Add(Rule(WindowStart.AddDays(10), null));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => _context.SaveChangesAsync());

        var postgres = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal("23P01", postgres.SqlState);
    }

    [Fact]
    public async Task NonOverlappingWindows_AreAccepted()
    {
        _context.BlossomConversionRules.Add(Rule(WindowStart, WindowStart.AddDays(30)));
        await _context.SaveChangesAsync();

        _context.BlossomConversionRules.Add(Rule(WindowStart.AddDays(30), WindowStart.AddDays(60)));
        await _context.SaveChangesAsync();

        Assert.Equal(2, await _context.BlossomConversionRules.CountAsync());
    }

    [Fact]
    public async Task DraftOverlappingAnActiveRule_IsAccepted()
    {
        // A successor must be preparable while the predecessor is still Active; activation
        // supersedes the predecessor in the same flow (BR-1.8). Only Active rows are
        // constrained, so the Draft insert is legal.
        _context.BlossomConversionRules.Add(Rule(WindowStart, null));
        await _context.SaveChangesAsync();

        _context.BlossomConversionRules.Add(Rule(WindowStart.AddDays(10), null, BlossomRuleStatus.Draft));
        await _context.SaveChangesAsync();

        Assert.Equal(2, await _context.BlossomConversionRules.CountAsync());
    }

    [Fact]
    public async Task CancelledWindow_DoesNotBlockANewRule()
    {
        _context.BlossomConversionRules.Add(
            Rule(WindowStart, WindowStart.AddDays(30), BlossomRuleStatus.Cancelled));
        await _context.SaveChangesAsync();

        // Overlaps the cancelled window with a distinct start: only the exclusion
        // constraint's Draft/Active predicate can reject this.
        _context.BlossomConversionRules.Add(Rule(WindowStart.AddDays(10), null));
        await _context.SaveChangesAsync();

        Assert.Equal(2, await _context.BlossomConversionRules.CountAsync());
    }
}
