using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.CustomerConcierge.DTOs;
using Aveline.Api.Modules.CustomerConcierge.Repositories;
using Aveline.Api.Modules.CustomerConcierge.Services;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Tests;

/// <summary>
/// Unit tests for the Customer Concierge services (identification, memory persistence,
/// consent gating, brief generation) backed by the in-memory EF provider with a stubbed
/// embedding service (no pgvector / no external calls).
/// </summary>
public class CustomerConciergeServiceTests
{
    private readonly AppDbContext _context;
    private readonly Guid _orgA = Guid.NewGuid();
    private readonly Guid _orgB = Guid.NewGuid();

    public CustomerConciergeServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"CustomerConciergeSvc_{Guid.NewGuid()}")
            .Options;
        _context = new AppDbContext(options);
    }

    private static IEmbeddingService StubEmbeddings(float[] vector) => new StubEmbeddingService(vector);

    private static float[] DummyVector() => Enumerable.Range(0, 1536).Select(i => (float)i).ToArray();

    private (ICustomerService customers, ICustomerMemoryService memories,
             ICustomerConsentService consent, ICustomerInteractionService interactions,
             ICustomerEventService events) BuildServices(float[]? embeddingVector = null)
    {
        var customers = new CustomerService(
            new CustomerRepository(_context),
            new CustomerConsentRepository(_context),
            new CustomerTagRepository(_context));
        var memories = new CustomerMemoryService(
            new CustomerMemoryRepository(_context),
            new CustomerRepository(_context),
            new CustomerEventRepository(_context),
            new CustomerConsentRepository(_context),
            new CustomerTagRepository(_context),
            StubEmbeddings(embeddingVector ?? DummyVector()));
        var consent = new CustomerConsentService(new CustomerConsentRepository(_context));
        var interactions = new CustomerInteractionService(new CustomerInteractionRepository(_context));
        var events = new CustomerEventService(new CustomerEventRepository(_context));
        return (customers, memories, consent, interactions, events);
    }

    [Fact]
    public async Task IdentifyOrCreate_CreatesNewCustomerWithPendingConsent()
    {
        var (customers, _, _, _, _) = BuildServices();
        var profile = await customers.IdentifyOrCreateAsync(_orgA, "+94771234567", "Sarah Perera");

        Assert.Equal("new", profile.Status);
        Assert.Equal("Sarah Perera", profile.FullName);
        Assert.Equal("pending", profile.ConsentStatus);
        Assert.NotEqual(Guid.Empty, profile.CustomerId);
    }

    [Fact]
    public async Task IdentifyOrCreate_ReturnsExisting_ForSamePhoneAndOrg()
    {
        var (customers, _, _, _, _) = BuildServices();
        var first = await customers.IdentifyOrCreateAsync(_orgA, "+94771234567");
        var second = await customers.IdentifyOrCreateAsync(_orgA, "+94771234567");

        Assert.Equal(first.CustomerId, second.CustomerId);
        Assert.Equal(1, await _context.Customers.CountAsync());
    }

    [Fact]
    public async Task IdentifyOrCreate_IsScopedPerOrganization()
    {
        var (customers, _, _, _, _) = BuildServices();
        await customers.IdentifyOrCreateAsync(_orgA, "+94771234567");
        var orgBProfile = await customers.IdentifyOrCreateAsync(_orgB, "+94771234567");

        Assert.NotEqual(Guid.Empty, orgBProfile.CustomerId);
        Assert.Equal(2, await _context.Customers.CountAsync());
    }

    [Fact]
    public async Task SaveMemory_WhenConsentGranted_PersistsMemory()
    {
        var (customers, memories, consent, _, _) = BuildServices();
        var profile = await customers.IdentifyOrCreateAsync(_orgA, "+94771234567");
        await consent.UpdateAsync(_orgA, profile.CustomerId, "granted");

        var saved = await memories.SaveMemoryAsync(profile.CustomerId, new SaveMemoryRequest
        {
            OrganizationId = _orgA,
            Content = "Prefers emerald silk",
            Category = "preference",
            IsExplicit = true,
            Confidence = 0.95m,
        });

        Assert.NotNull(saved);
        Assert.Equal("Prefers emerald silk", saved!.Content);
        Assert.Equal("preference", saved.Category);
    }

    [Fact]
    public async Task SaveMemory_WhenConsentRevoked_ReturnsNull()
    {
        var (customers, memories, consent, _, _) = BuildServices();
        var profile = await customers.IdentifyOrCreateAsync(_orgA, "+94771234567");
        await consent.UpdateAsync(_orgA, profile.CustomerId, "revoked");

        var saved = await memories.SaveMemoryAsync(profile.CustomerId, new SaveMemoryRequest
        {
            OrganizationId = _orgA,
            Content = "Should not be stored",
            Category = "fact",
        });

        Assert.Null(saved);
        Assert.Equal(0, await _context.CustomerMemories.CountAsync());
    }

    [Fact]
    public async Task ConsentUpdate_RejectsUnknownStatus()
    {
        var (customers, _, consent, _, _) = BuildServices();
        var profile = await customers.IdentifyOrCreateAsync(_orgA, "+94771234567");

        await Assert.ThrowsAsync<ArgumentException>(
            () => consent.UpdateAsync(_orgA, profile.CustomerId, "maybe"));
    }

    [Fact]
    public async Task GenerateBrief_ReturnsSummary_WithEvents()
    {
        var (customers, memories, consent, _, events) = BuildServices();
        var profile = await customers.IdentifyOrCreateAsync(_orgA, "+94771234567", "Sarah Perera");
        await consent.UpdateAsync(_orgA, profile.CustomerId, "granted");
        await events.AddAsync(profile.CustomerId, new AddEventRequest
        {
            OrganizationId = _orgA,
            EventType = "wedding",
            EventDate = new DateTime(2026, 12, 1),
            Description = "Daughter's wedding",
        });

        var brief = await memories.GenerateBriefAsync(_orgA, profile.CustomerId);

        Assert.NotNull(brief);
        Assert.Equal("Sarah Perera", brief!.CustomerName);
        Assert.Contains("wedding", brief.UpcomingEvents);
    }

    [Fact]
    public async Task RecordInteraction_PersistsParsedIntent()
    {
        var (customers, _, _, interactions, _) = BuildServices();
        var profile = await customers.IdentifyOrCreateAsync(_orgA, "+94771234567");

        var recorded = await interactions.RecordAsync(
            _orgA, profile.CustomerId, "whatsapp", "inbound",
            "I need a blue saree", "{\"occasion\":\"wedding\"}");

        Assert.Equal("whatsapp", recorded.Channel);
        Assert.Equal("I need a blue saree", recorded.MessageContent);
    }

    private sealed class StubEmbeddingService(float[] vector) : IEmbeddingService
    {
        public Task<float[]> GenerateAsync(string text, CancellationToken cancellationToken = default)
            => Task.FromResult(vector);
    }
}
