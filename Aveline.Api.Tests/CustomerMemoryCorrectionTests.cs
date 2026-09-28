using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.CustomerConcierge.DTOs;
using Aveline.Api.Modules.CustomerConcierge.Models;
using Aveline.Api.Modules.CustomerConcierge.Repositories;
using Aveline.Api.Modules.CustomerConcierge.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aveline.Api.Tests;

/// <summary>
/// Correcting and removing a single memory, and the read paths that must respect the soft-delete
/// column (gaps A4, A5, A6). The GDPR erase is the blunt instrument; this is the per-note one a
/// staff member needs when a memory is simply wrong.
/// </summary>
public class CustomerMemoryCorrectionTests
{
    private readonly AppDbContext _context;
    private readonly Guid _org = Guid.NewGuid();
    private readonly Guid _otherOrg = Guid.NewGuid();

    public CustomerMemoryCorrectionTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"MemoryCorrection_{Guid.NewGuid()}")
            .Options;
        _context = new AppDbContext(options);
    }

    private sealed class StubEmbeddingService : IEmbeddingService
    {
        public Task<float[]> GenerateAsync(string text, CancellationToken cancellationToken = default)
            => Task.FromResult(Enumerable.Range(0, 1536).Select(i => 0.2f + i).ToArray());
    }

    private sealed record Services(ICustomerService Customers, ICustomerMemoryService Memories, ICustomerConsentService Consent);

    private Services BuildServices()
    {
        var customers = new CustomerService(
            new CustomerRepository(_context),
            new CustomerConsentRepository(_context),
            new CustomerTagRepository(_context),
            new TestDistributedCache(),
            NullLogger<CustomerService>.Instance);
        var gate = new ConsentGateService(
            new CustomerConsentRepository(_context), NullLogger<ConsentGateService>.Instance);
        var memories = new CustomerMemoryService(
            _context,
            new CustomerMemoryRepository(_context),
            new CustomerRepository(_context),
            new CustomerEventRepository(_context),
            gate,
            new CustomerTagRepository(_context),
            new StubEmbeddingService(),
            NullLogger<CustomerMemoryService>.Instance);
        var consent = new CustomerConsentService(new CustomerConsentRepository(_context));
        return new Services(customers, memories, consent);
    }

    private async Task<Guid> GrantCustomerAsync(Services services, string phone = "+94771234567", Guid? org = null)
    {
        var scope = org ?? _org;
        var profile = await services.Customers.IdentifyOrCreateAsync(scope, phone);
        await services.Consent.UpdateAsync(scope, profile.CustomerId, "granted");
        return profile.CustomerId;
    }

    private async Task<CustomerMemoryDto> SaveAsync(Services services, Guid customerId, string content, Guid? org = null)
    {
        var saved = await services.Memories.SaveMemoryAsync(customerId, new SaveMemoryRequest
        {
            OrganizationId = org ?? _org,
            Content = content,
            Category = "preference",
        });
        return saved!;
    }

    [Fact]
    public async Task CorrectMemory_RewritesContentAndReembeds()
    {
        var services = BuildServices();
        var customerId = await GrantCustomerAsync(services);
        var saved = await SaveAsync(services, customerId, "Sarah prefers emerald silk");

        var corrected = await services.Memories.CorrectMemoryAsync(
            _org, customerId, saved.Id, new CorrectMemoryRequest { Content = "Sarah prefers sapphire silk" });

        Assert.NotNull(corrected);
        Assert.Equal("Sarah prefers sapphire silk", corrected!.Content);
        Assert.NotEqual(
            MemoryContentKey.From(saved.Content),
            MemoryContentKey.From(corrected.Content));
        var stored = await _context.CustomerMemories.SingleAsync();
        Assert.Equal("Sarah prefers sapphire silk", stored.Content);
        Assert.True(stored.UpdatedAt >= stored.CreatedAt);
    }

    [Fact]
    public async Task CorrectMemory_ScopedToTheOwningCustomerAndOrg()
    {
        var services = BuildServices();
        var customerId = await GrantCustomerAsync(services);
        var saved = await SaveAsync(services, customerId, "Prefers emerald silk");

        var wrongOrg = await services.Memories.CorrectMemoryAsync(
            _otherOrg, customerId, saved.Id, new CorrectMemoryRequest { Content = "Rewritten" });
        var wrongCustomer = await services.Memories.CorrectMemoryAsync(
            _org, Guid.NewGuid(), saved.Id, new CorrectMemoryRequest { Content = "Rewritten" });

        Assert.Null(wrongOrg);
        Assert.Null(wrongCustomer);
        Assert.Equal("Prefers emerald silk", (await _context.CustomerMemories.SingleAsync()).Content);
    }

    [Fact]
    public async Task CorrectMemory_CannotRestateAnotherNote()
    {
        var services = BuildServices();
        var customerId = await GrantCustomerAsync(services);
        await SaveAsync(services, customerId, "Prefers emerald silk");
        var second = await SaveAsync(services, customerId, "Prefers pastel tones");

        var corrected = await services.Memories.CorrectMemoryAsync(
            _org, customerId, second.Id, new CorrectMemoryRequest { Content = "prefers emerald silk." });

        // Collapsing onto an existing note would silently drop a row, so the correction is refused.
        Assert.Null(corrected);
        Assert.Equal("Prefers pastel tones", (await _context.CustomerMemories.SingleAsync(m => m.Id == second.Id)).Content);
    }

    [Fact]
    public async Task RemoveMemory_SoftDeletesAndHidesTheRow()
    {
        var services = BuildServices();
        var customerId = await GrantCustomerAsync(services);
        var saved = await SaveAsync(services, customerId, "Prefers emerald silk");

        var removed = await services.Memories.RemoveMemoryAsync(_org, customerId, saved.Id);

        Assert.True(removed);
        // The row is still physically present (the soft-delete column is the tombstone)...
        var raw = await _context.CustomerMemories.IgnoreQueryFilters().SingleAsync();
        Assert.NotNull(raw.DeletedAt);
        // ...and invisible to every query-filtered read.
        Assert.Empty(await _context.CustomerMemories.ToListAsync());
        Assert.Empty(await services.Memories.ListMemoriesAsync(_org, customerId));
    }

    [Fact]
    public async Task RemoveMemory_IsIdempotentAndScoped()
    {
        var services = BuildServices();
        var customerId = await GrantCustomerAsync(services);
        var saved = await SaveAsync(services, customerId, "Prefers emerald silk");

        Assert.False(await services.Memories.RemoveMemoryAsync(_otherOrg, customerId, saved.Id));
        Assert.False(await services.Memories.RemoveMemoryAsync(_org, Guid.NewGuid(), saved.Id));
        Assert.True(await services.Memories.RemoveMemoryAsync(_org, customerId, saved.Id));
        // A second removal of an already-removed row reports "nothing to do", not success.
        Assert.False(await services.Memories.RemoveMemoryAsync(_org, customerId, saved.Id));
    }

    [Fact]
    public async Task ListMemories_ReturnsActiveNotesNewestFirst()
    {
        var services = BuildServices();
        var customerId = await GrantCustomerAsync(services);
        var older = await SaveAsync(services, customerId, "Prefers emerald silk");
        var newer = await SaveAsync(services, customerId, "Prefers pastel tones");

        var listed = await services.Memories.ListMemoriesAsync(_org, customerId);

        Assert.Equal([newer.Id, older.Id], listed.Select(m => m.Id).ToArray());
    }

    [Fact]
    public async Task SaveMemory_DoesNotResurrectARemovedNote()
    {
        var services = BuildServices();
        var customerId = await GrantCustomerAsync(services);
        var saved = await SaveAsync(services, customerId, "Prefers emerald silk");
        await services.Memories.RemoveMemoryAsync(_org, customerId, saved.Id);

        var reSaved = await services.Memories.SaveMemoryAsync(customerId, new SaveMemoryRequest
        {
            OrganizationId = _org,
            Content = "Prefers emerald silk",
            Category = "preference",
        });

        // A removed note is a new fact if it is stated again, not an update of a tombstone.
        Assert.NotNull(reSaved);
        Assert.NotEqual(saved.Id, reSaved!.Id);
        Assert.Single(await _context.CustomerMemories.ToListAsync());
    }

    [Fact]
    public async Task GenerateBrief_ReportsOnlyTheCustomerDescriptionNotTheAlias()
    {
        var services = BuildServices();
        var customerId = await GrantCustomerAsync(services);
        var customer = await _context.Customers.SingleAsync();
        customer.Nickname = "Sara";
        customer.Description = "Collector of hand-woven silks; prefers private appointments.";
        await _context.SaveChangesAsync();

        var brief = await services.Memories.GenerateBriefAsync(_org, customerId);

        Assert.NotNull(brief);
        Assert.Equal("Collector of hand-woven silks; prefers private appointments.", brief!.Description);
    }

    [Fact]
    public async Task SavePreference_ReachesTheTableTheBriefReads()
    {
        // Gap C4: the brief's preference summary is assembled from the preferences table, so a
        // stated preference that only became a memory left the brief saying nothing.
        var services = BuildServices();
        var customerId = await GrantCustomerAsync(services);

        var saved = await services.Memories.SavePreferenceAsync(customerId, new SavePreferenceRequest
        {
            OrganizationId = _org,
            PreferenceKey = "general",
            PreferenceValue = "silk sarees",
            IsExplicit = true,
            Confidence = 0.90m,
        });

        Assert.NotNull(saved);
        Assert.Equal("silk sarees", saved!.PreferenceValue);
        var brief = await services.Memories.GenerateBriefAsync(_org, customerId);
        Assert.NotNull(brief);
        Assert.Contains("silk sarees", brief!.PreferenceSummary);
    }

    [Fact]
    public async Task SavePreference_RestatingTheSameValueDoesNotDuplicateTheRow()
    {
        var services = BuildServices();
        var customerId = await GrantCustomerAsync(services);

        await services.Memories.SavePreferenceAsync(customerId, new SavePreferenceRequest
        {
            OrganizationId = _org, PreferenceKey = "general", PreferenceValue = "silk",
            IsExplicit = true, Confidence = 0.90m,
        });
        await services.Memories.SavePreferenceAsync(customerId, new SavePreferenceRequest
        {
            OrganizationId = _org, PreferenceKey = "general", PreferenceValue = "silk",
            IsExplicit = true, Confidence = 0.95m,
        });

        var stored = await _context.CustomerPreferences.SingleAsync();
        Assert.Equal(0.95m, stored.Confidence);
    }

    [Fact]
    public async Task SavePreference_KeepsADifferentValueAlongsideTheOldOne()
    {
        var services = BuildServices();
        var customerId = await GrantCustomerAsync(services);

        await services.Memories.SavePreferenceAsync(customerId, new SavePreferenceRequest
        {
            OrganizationId = _org, PreferenceKey = "general", PreferenceValue = "silk",
        });
        await services.Memories.SavePreferenceAsync(customerId, new SavePreferenceRequest
        {
            OrganizationId = _org, PreferenceKey = "general", PreferenceValue = "linen",
        });

        Assert.Equal(2, await _context.CustomerPreferences.CountAsync());
    }

    [Fact]
    public async Task SavePreference_IsRefusedForARevokedCustomer()
    {
        var services = BuildServices();
        var customerId = await GrantCustomerAsync(services);
        await services.Consent.UpdateAsync(_org, customerId, "revoked");

        var saved = await services.Memories.SavePreferenceAsync(customerId, new SavePreferenceRequest
        {
            OrganizationId = _org, PreferenceKey = "general", PreferenceValue = "silk",
        });

        Assert.Null(saved);
        Assert.Equal(0, await _context.CustomerPreferences.CountAsync());
    }
}
