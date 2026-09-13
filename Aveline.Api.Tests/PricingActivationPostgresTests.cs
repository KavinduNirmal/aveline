using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Audit.Repositories;
using Aveline.Api.Modules.Audit.Services;
using Aveline.Api.Modules.Billing.Domain;
using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Billing.Repositories;
using Aveline.Api.Modules.Billing.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Aveline.Api.Tests;

/// <summary>
/// Issue #188 — Postgres-backed verification of the invariants the in-memory provider
/// cannot exercise: the unique scope index, the GiST exclusion constraint, and the
/// transactional predecessor trim performed on activation.
/// </summary>
public class PricingActivationPostgresTests : IAsyncLifetime
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

    private PricingService CreateService(IPricingRepository? repository = null) => new(
        repository ?? new PricingRepository(_context),
        new PricingRuleCache(new MemoryCache(new MemoryCacheOptions())),
        new AuditService(
            new AuditRepository(_context), new AuditRedactor(),
            new HttpContextAccessor(), NullLogger<AuditService>.Instance),
        NullLogger<PricingService>.Instance);

    private static readonly DateTime Start = new(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Activate_TrimsAndSupersedesThePredecessorInTheDatabase()
    {
        var predecessor = new BlossomConversionRule
        {
            ScopeKind = BlossomRuleScopeKind.Global,
            UnitsPerBlossom = 1000,
            MinimumChargeBlossoms = 0.1m,
            RoundingMode = BlossomRoundingMode.Ceiling,
            RoundingDecimals = 1,
            EffectiveFrom = Start,
            Status = BlossomRuleStatus.Active,
            ChangeReason = "Predecessor rule for activation test.",
            CreatedByUserId = Guid.CreateVersion7(),
        };
        _context.BlossomConversionRules.Add(predecessor);
        await _context.SaveChangesAsync();

        var service = CreateService();
        var draft = await service.CreateRuleAsync(new CreatePricingRuleCommand(
            BlossomRuleScopeKind.Global, null, null, 2000, 0.1m,
            BlossomRoundingMode.Ceiling, 1,
            Start.AddDays(30), null, "Successor rule for activation test.",
            Guid.CreateVersion7(), AllowBackdate: true));

        var activated = await service.ActivateRuleAsync(draft.Id);

        _context.ChangeTracker.Clear();

        var storedPredecessor = await _context.BlossomConversionRules.SingleAsync(r => r.Id == predecessor.Id);
        Assert.Equal(BlossomRuleStatus.Superseded, storedPredecessor.Status);
        Assert.Equal(activated.EffectiveFrom, storedPredecessor.EffectiveTo);

        var storedDraft = await _context.BlossomConversionRules.SingleAsync(r => r.Id == draft.Id);
        Assert.Equal(BlossomRuleStatus.Active, storedDraft.Status);

        // The exclusion constraint must be satisfied: both rows exist, none overlap.
        Assert.Equal(2, await _context.BlossomConversionRules.CountAsync());
    }

    [Fact]
    public async Task UniqueScopeIndex_RejectsDuplicateScopeAndStart()
    {
        _context.BlossomConversionRules.Add(Cancelled(Start));
        await _context.SaveChangesAsync();

        _context.BlossomConversionRules.Add(Cancelled(Start));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
            _context.SaveChangesAsync());

        var postgres = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal("23505", postgres.SqlState);
    }

    [Fact]
    public async Task BtreeGistExtension_IsInstalledByMigrationM2()
    {
        var count = await _context.Database
            .SqlQuery<int>($"SELECT COUNT(*)::int AS \"Value\" FROM pg_extension WHERE extname = 'btree_gist'")
            .SingleAsync();

        Assert.Equal(1, count);
    }

    private static BlossomConversionRule Cancelled(DateTime from) => new()
    {
        ScopeKind = BlossomRuleScopeKind.Global,
        UnitsPerBlossom = 1000,
        MinimumChargeBlossoms = 0.1m,
        RoundingMode = BlossomRoundingMode.Ceiling,
        RoundingDecimals = 1,
        EffectiveFrom = from,
        Status = BlossomRuleStatus.Cancelled,
        ChangeReason = "Cancelled rule for uniqueness test.",
        CreatedByUserId = Guid.CreateVersion7(),
    };

    [Fact]
    public async Task Activate_RollsBackThePredecessorTrimWhenTheSuccessorWriteFails()
    {
        var predecessor = new BlossomConversionRule
        {
            ScopeKind = BlossomRuleScopeKind.Global,
            UnitsPerBlossom = 1000,
            MinimumChargeBlossoms = 0.1m,
            RoundingMode = BlossomRoundingMode.Ceiling,
            RoundingDecimals = 1,
            EffectiveFrom = Start,
            Status = BlossomRuleStatus.Active,
            ChangeReason = "Predecessor rule for the atomic activation test.",
            CreatedByUserId = Guid.CreateVersion7(),
        };
        _context.BlossomConversionRules.Add(predecessor);
        await _context.SaveChangesAsync();

        var repository = new FailSecondRuleUpdateRepository(new PricingRepository(_context));
        var service = CreateService(repository);

        var draft = await service.CreateRuleAsync(new CreatePricingRuleCommand(
            BlossomRuleScopeKind.Global, null, null, 2000, 0.1m,
            BlossomRoundingMode.Ceiling, 1,
            Start.AddDays(30), null, "Successor rule for the atomic activation test.",
            Guid.CreateVersion7(), AllowBackdate: true));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ActivateRuleAsync(draft.Id));

        _context.ChangeTracker.Clear();

        var storedPredecessor = await _context.BlossomConversionRules.SingleAsync(r => r.Id == predecessor.Id);
        Assert.Equal(BlossomRuleStatus.Active, storedPredecessor.Status);
        Assert.Null(storedPredecessor.EffectiveTo);

        var storedDraft = await _context.BlossomConversionRules.SingleAsync(r => r.Id == draft.Id);
        Assert.Equal(BlossomRuleStatus.Draft, storedDraft.Status);
    }

    /// <summary>
    /// Delegates to the real repository but throws on the second rule update, simulating a
    /// failure of the successor write after the predecessor trim has been flushed. Without a
    /// shared transaction the trim would already be committed and this test would fail.
    /// </summary>
    private sealed class FailSecondRuleUpdateRepository(IPricingRepository inner) : IPricingRepository
    {
        private int _ruleUpdateCount;

        public Task<BlossomConversionRule?> ResolveActiveRuleAsync(
            string? provider, string? model, DateTime at, CancellationToken cancellationToken = default) =>
            inner.ResolveActiveRuleAsync(provider, model, at, cancellationToken);

        public Task<BlossomConversionRule?> GetRuleAsync(
            Guid ruleId, CancellationToken cancellationToken = default) =>
            inner.GetRuleAsync(ruleId, cancellationToken);

        public Task<IReadOnlyList<BlossomConversionRule>> ListRulesAsync(
            PricingRuleFilter filter, int page, int pageSize, CancellationToken cancellationToken = default) =>
            inner.ListRulesAsync(filter, page, pageSize, cancellationToken);

        public Task<int> CountRulesAsync(
            PricingRuleFilter filter, CancellationToken cancellationToken = default) =>
            inner.CountRulesAsync(filter, cancellationToken);

        public Task<int> GetNextVersionAsync(
            BlossomRuleScopeKind scopeKind, string? provider, string? model,
            CancellationToken cancellationToken = default) =>
            inner.GetNextVersionAsync(scopeKind, provider, model, cancellationToken);

        public Task<BlossomConversionRule?> FindPredecessorAsync(
            BlossomConversionRule candidate, CancellationToken cancellationToken = default) =>
            inner.FindPredecessorAsync(candidate, cancellationToken);

        public Task AddRuleAsync(
            BlossomConversionRule rule, CancellationToken cancellationToken = default) =>
            inner.AddRuleAsync(rule, cancellationToken);

        public Task UpdateRuleAsync(
            BlossomConversionRule rule, CancellationToken cancellationToken = default)
        {
            _ruleUpdateCount++;
            if (_ruleUpdateCount == 2)
            {
                throw new InvalidOperationException("Simulated failure of the successor write.");
            }

            return inner.UpdateRuleAsync(rule, cancellationToken);
        }

        public Task<BlossomPriceEntry> AddPriceEntryAsync(
            BlossomPriceEntry entry, CancellationToken cancellationToken = default) =>
            inner.AddPriceEntryAsync(entry, cancellationToken);

        public Task<BlossomPriceEntry?> GetPriceEntryAsync(
            Guid entryId, Guid? organizationId, CancellationToken cancellationToken = default) =>
            inner.GetPriceEntryAsync(entryId, organizationId, cancellationToken);

        public Task<BlossomPriceEntry?> FindPriceEntryAsync(
            Guid entryId, CancellationToken cancellationToken = default) =>
            inner.FindPriceEntryAsync(entryId, cancellationToken);

        public Task UpdatePriceEntryAsync(
            BlossomPriceEntry entry, CancellationToken cancellationToken = default) =>
            inner.UpdatePriceEntryAsync(entry, cancellationToken);

        public Task<IReadOnlyList<BlossomPriceEntry>> ListPriceEntriesAsync(
            BlossomSkuKind? skuKind, PlanTier? planTier, Guid? organizationId,
            CancellationToken cancellationToken = default) =>
            inner.ListPriceEntriesAsync(skuKind, planTier, organizationId, cancellationToken);

        public Task<IPricingTransaction> BeginTransactionAsync(
            CancellationToken cancellationToken = default) =>
            inner.BeginTransactionAsync(cancellationToken);
    }
}
