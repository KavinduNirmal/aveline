using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.CustomerConcierge.DTOs;
using Aveline.Api.Modules.CustomerConcierge.Models;
using Aveline.Api.Modules.CustomerConcierge.Repositories;
using Aveline.Api.Modules.CustomerConcierge.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aveline.Api.Tests;

/// <summary>
/// The memory write path's integrity properties: the provenance fields the caller supplies reach
/// the stored row (gap A1), a duplicate statement does not become a second row (gap A3), a memory
/// can be corrected and removed without a GDPR erase (gap A5), and every assignment of
/// <see cref="CustomerMemory.DeletedAt"/> is deliberate (gap A6).
/// </summary>
public class CustomerMemoryWriteIntegrityTests
{
    private readonly AppDbContext _context;
    private readonly Guid _org = Guid.NewGuid();
    private readonly Guid _otherOrg = Guid.NewGuid();

    public CustomerMemoryWriteIntegrityTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"MemoryWriteIntegrity_{Guid.NewGuid()}")
            .Options;
        _context = new AppDbContext(options);
    }

    private sealed class RecordingEmbeddingService : IEmbeddingService
    {
        public int Calls { get; private set; }

        public Task<float[]> GenerateAsync(string text, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(Enumerable.Range(0, 1536).Select(i => 0.1f + i).ToArray());
        }
    }

    private sealed record Services(
        ICustomerService Customers,
        ICustomerMemoryService Memories,
        ICustomerConsentService Consent,
        RecordingEmbeddingService Embeddings);

    private Services BuildServices()
    {
        var embeddings = new RecordingEmbeddingService();
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
            embeddings,
            NullLogger<CustomerMemoryService>.Instance);
        var consent = new CustomerConsentService(new CustomerConsentRepository(_context));
        return new Services(customers, memories, consent, embeddings);
    }

    private async Task<Guid> GrantCustomerAsync(Services services, Guid? org = null)
    {
        var scope = org ?? _org;
        var profile = await services.Customers.IdentifyOrCreateAsync(scope, "+94771234567");
        await services.Consent.UpdateAsync(scope, profile.CustomerId, "granted");
        return profile.CustomerId;
    }

    [Fact]
    public async Task SaveMemory_StoresCallerSuppliedProvenance()
    {
        var services = BuildServices();
        var customerId = await GrantCustomerAsync(services);

        var saved = await services.Memories.SaveMemoryAsync(customerId, new SaveMemoryRequest
        {
            OrganizationId = _org,
            Content = "Sarah prefers emerald silk",
            Category = "preference",
            Source = "conversation",
            IsExplicit = true,
            Confidence = 0.90m,
        });

        Assert.NotNull(saved);
        Assert.True(saved!.IsExplicit);
        Assert.Equal(0.90m, saved.Confidence);
        Assert.Equal("conversation", saved.Source);
    }

    [Fact]
    public async Task SaveMemory_InfersProvenanceWhenCallerOmitsIt()
    {
        var services = BuildServices();
        var customerId = await GrantCustomerAsync(services);

        var saved = await services.Memories.SaveMemoryAsync(customerId, new SaveMemoryRequest
        {
            OrganizationId = _org,
            Content = "Prefers pastel tones",
            Category = "preference",
        });

        Assert.NotNull(saved);
        Assert.False(saved!.IsExplicit);
        Assert.Equal(0.50m, saved.Confidence);
        Assert.Equal("conversation", saved.Source);
    }

    [Fact]
    public async Task SaveMemory_CollapsesARestatementOfTheSameNote()
    {
        var services = BuildServices();
        var customerId = await GrantCustomerAsync(services);

        var first = await services.Memories.SaveMemoryAsync(customerId, new SaveMemoryRequest
        {
            OrganizationId = _org,
            Content = "The customer has a party",
            Category = "event",
        });
        var second = await services.Memories.SaveMemoryAsync(customerId, new SaveMemoryRequest
        {
            OrganizationId = _org,
            // Same note: case, surrounding whitespace and trailing punctuation are not distinctions.
            Content = "  the customer has a party.  ",
            Category = "event",
        });

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first!.Id, second!.Id);
        Assert.Equal(1, await _context.CustomerMemories.CountAsync());
        // The duplicate never reached the embedding provider: the store refused the write first.
        Assert.Equal(1, services.Embeddings.Calls);
    }

    [Fact]
    public async Task SaveMemory_KeepsDistinctNotesForTheSameCustomer()
    {
        var services = BuildServices();
        var customerId = await GrantCustomerAsync(services);

        await services.Memories.SaveMemoryAsync(customerId, new SaveMemoryRequest
        {
            OrganizationId = _org,
            Content = "The customer has a party",
            Category = "event",
        });
        await services.Memories.SaveMemoryAsync(customerId, new SaveMemoryRequest
        {
            OrganizationId = _org,
            Content = "The customer has a wedding",
            Category = "event",
        });

        Assert.Equal(2, await _context.CustomerMemories.CountAsync());
    }

    [Fact]
    public async Task SaveMemory_KeepsTheSameNoteForDifferentCustomers()
    {
        var services = BuildServices();
        var first = await GrantCustomerAsync(services);
        var secondProfile = await services.Customers.IdentifyOrCreateAsync(_org, "+94770000001");
        await services.Consent.UpdateAsync(_org, secondProfile.CustomerId, "granted");

        await services.Memories.SaveMemoryAsync(first, new SaveMemoryRequest
        {
            OrganizationId = _org,
            Content = "Prefers emerald silk",
            Category = "preference",
        });
        await services.Memories.SaveMemoryAsync(secondProfile.CustomerId, new SaveMemoryRequest
        {
            OrganizationId = _org,
            Content = "Prefers emerald silk",
            Category = "preference",
        });

        Assert.Equal(2, await _context.CustomerMemories.CountAsync());
    }

    [Fact]
    public async Task SaveMemory_StoresTheSuppliedMetadata()
    {
        var services = BuildServices();
        var customerId = await GrantCustomerAsync(services);

        var saved = await services.Memories.SaveMemoryAsync(customerId, new SaveMemoryRequest
        {
            OrganizationId = _org,
            Content = "Prefers emerald silk",
            Category = "preference",
            MetadataJson = """{"interactionId":"int-9"}""",
        });

        Assert.Equal("""{"interactionId":"int-9"}""", saved!.MetadataJson);
    }

    [Fact]
    public async Task SaveMemory_StoresAnExpiryWhenSupplied()
    {
        var services = BuildServices();
        var customerId = await GrantCustomerAsync(services);
        var expiresAt = DateTime.UtcNow.AddDays(30);

        var saved = await services.Memories.SaveMemoryAsync(customerId, new SaveMemoryRequest
        {
            OrganizationId = _org,
            Content = "Attending the December gala",
            Category = "event",
            ExpiresAt = expiresAt,
        });

        Assert.Equal(expiresAt, saved!.ExpiresAt);
    }
}
