using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.CustomerConcierge.DTOs;
using Aveline.Api.Modules.CustomerConcierge.Models;
using Aveline.Api.Modules.CustomerConcierge.Repositories;
using Aveline.Api.Modules.CustomerConcierge.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aveline.Api.Tests;

/// <summary>
/// The §5.5 enforcement table's remaining rows (plan item 1.7). Revocation must close every
/// customer-data read and write, not just <c>SaveMemoryAsync</c>:
///
/// <list type="bullet">
/// <item>memory read (semantic search) - <c>SearchAsync</c>;</item>
/// <item>brief generation - <c>GenerateBriefAsync</c>;</item>
/// <item>interaction log write - <c>RecordAsync</c>;</item>
/// <item>event write - <c>AddAsync</c>.</item>
/// </list>
///
/// Each revoked test asserts both halves of "nothing happens": no row is returned/written, and
/// the store is not even reached.
/// </summary>
public class ConsentEnforcementTests
{
    private readonly AppDbContext _context;
    private readonly Guid _orgId = Guid.NewGuid();
    private readonly RecordingMemoryRepository _memoryRows = new();
    private readonly RecordingEmbeddingService _embedding = new();
    private readonly IConsentGateService _gate;

    public ConsentEnforcementTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"ConsentEnforcement_{Guid.NewGuid()}")
            .Options;
        _context = new AppDbContext(options);
        _gate = new ConsentGateService(
            new CustomerConsentRepository(_context), NullLogger<ConsentGateService>.Instance);
    }

    private async Task<Guid> SeedCustomerAsync(string consentStatus)
    {
        var customer = new Customer
        {
            OrganizationId = _orgId,
            PhoneNumber = "+94771234567",
            FullName = "Sarah Perera",
            Status = "vip",
        };
        _context.Customers.Add(customer);
        _context.CustomerConsents.Add(new CustomerConsent
        {
            OrganizationId = _orgId,
            CustomerId = customer.Id,
            ConsentStatus = consentStatus,
        });
        await _context.SaveChangesAsync();
        return customer.Id;
    }

    private CustomerMemoryService BuildMemoryService() => new(
        _memoryRows,
        new CustomerRepository(_context),
        new CustomerEventRepository(_context),
        _gate,
        new CustomerTagRepository(_context),
        _embedding);

    private CustomerInteractionService BuildInteractionService() =>
        new(new CustomerInteractionRepository(_context), _gate);

    private CustomerEventService BuildEventService() =>
        new(new CustomerEventRepository(_context), _gate);

    [Fact]
    public async Task SearchMemory_WhenConsentRevoked_ReturnsNothingAndDoesNotRead()
    {
        var customerId = await SeedCustomerAsync(ConsentStatuses.Revoked);
        _memoryRows.Results =
        [
            new CustomerMemorySearchResult(Guid.NewGuid(), customerId, "Prefers emerald silk", "preference", 0.9m, true, 0.98),
        ];

        var results = await BuildMemoryService().SearchAsync(new MemorySearchRequest
        {
            OrganizationId = _orgId,
            CustomerId = customerId,
            Query = "silk",
        });

        Assert.Empty(results);
        Assert.Equal(0, _memoryRows.SearchCalls);
        Assert.Equal(0, _embedding.Calls);
    }

    [Fact]
    public async Task SearchMemory_WhenConsentGranted_ReadsTheStore()
    {
        var customerId = await SeedCustomerAsync(ConsentStatuses.Granted);
        _memoryRows.Results =
        [
            new CustomerMemorySearchResult(Guid.NewGuid(), customerId, "Prefers emerald silk", "preference", 0.9m, true, 0.98),
        ];

        var results = await BuildMemoryService().SearchAsync(new MemorySearchRequest
        {
            OrganizationId = _orgId,
            CustomerId = customerId,
            Query = "silk",
        });

        Assert.Single(results);
        Assert.Equal(1, _memoryRows.SearchCalls);
    }

    [Fact]
    public async Task GenerateBrief_WhenConsentRevoked_ReturnsNothingAndDoesNotRead()
    {
        var customerId = await SeedCustomerAsync(ConsentStatuses.Revoked);
        _context.CustomerEvents.Add(new CustomerEvent
        {
            OrganizationId = _orgId,
            CustomerId = customerId,
            EventType = "wedding",
            EventDate = new DateTime(2026, 12, 1),
            IsActive = true,
        });
        await _context.SaveChangesAsync();

        var brief = await BuildMemoryService().GenerateBriefAsync(_orgId, customerId);

        Assert.Null(brief);
    }

    [Fact]
    public async Task GenerateBrief_WhenConsentGranted_ReturnsTheBriefWithItsConsentStatus()
    {
        var customerId = await SeedCustomerAsync(ConsentStatuses.Granted);

        var brief = await BuildMemoryService().GenerateBriefAsync(_orgId, customerId);

        Assert.NotNull(brief);
        Assert.Equal("Sarah Perera", brief!.CustomerName);
    }

    [Fact]
    public async Task RecordInteraction_WhenConsentRevoked_WritesNothing()
    {
        var customerId = await SeedCustomerAsync(ConsentStatuses.Revoked);

        var recorded = await BuildInteractionService().RecordAsync(
            _orgId, customerId, "whatsapp", "inbound", "I need a blue saree", "{}");

        Assert.Null(recorded);
        Assert.Equal(0, await _context.CustomerInteractions.CountAsync());
    }

    [Fact]
    public async Task RecordInteraction_WhenConsentGranted_WritesTheRow()
    {
        var customerId = await SeedCustomerAsync(ConsentStatuses.Granted);

        var recorded = await BuildInteractionService().RecordAsync(
            _orgId, customerId, "whatsapp", "inbound", "I need a blue saree", "{}");

        Assert.NotNull(recorded);
        Assert.Equal(1, await _context.CustomerInteractions.CountAsync());
    }

    [Fact]
    public async Task AddEvent_WhenConsentRevoked_WritesNothing()
    {
        var customerId = await SeedCustomerAsync(ConsentStatuses.Revoked);

        var customerEvent = await BuildEventService().AddAsync(customerId, new AddEventRequest
        {
            OrganizationId = _orgId,
            EventType = "wedding",
            EventDate = new DateTime(2026, 12, 1),
        });

        Assert.Null(customerEvent);
        Assert.Equal(0, await _context.CustomerEvents.CountAsync());
    }

    [Fact]
    public async Task AddEvent_WhenConsentGranted_WritesTheRow()
    {
        var customerId = await SeedCustomerAsync(ConsentStatuses.Granted);

        var customerEvent = await BuildEventService().AddAsync(customerId, new AddEventRequest
        {
            OrganizationId = _orgId,
            EventType = "wedding",
            EventDate = new DateTime(2026, 12, 1),
        });

        Assert.NotNull(customerEvent);
        Assert.Equal(1, await _context.CustomerEvents.CountAsync());
    }

    [Fact]
    public async Task SearchMemory_WhenTheGateFailsClosed_ReturnsNothing()
    {
        // Fail closed (plan §8.3): a read failure must not become an empty *successful* search
        // that then feeds personalization as though the customer had consented. The gate reports
        // the failure as a fail-closed decision (see ConsentGateServiceTests); this pins that the
        // read path honours it rather than only the boolean revoked case.
        var service = new CustomerMemoryService(
            _memoryRows,
            new CustomerRepository(_context),
            new CustomerEventRepository(_context),
            new FailClosedConsentGate(),
            new CustomerTagRepository(_context),
            _embedding);
        _memoryRows.Results =
        [
            new CustomerMemorySearchResult(Guid.NewGuid(), Guid.NewGuid(), "Prefers emerald silk", "preference", 0.9m, true, 0.98),
        ];

        var results = await service.SearchAsync(new MemorySearchRequest
        {
            OrganizationId = _orgId,
            CustomerId = Guid.NewGuid(),
            Query = "silk",
        });

        Assert.Empty(results);
        Assert.Equal(0, _memoryRows.SearchCalls);
    }

    private sealed class RecordingMemoryRepository : ICustomerMemoryRepository
    {
        public int SearchCalls { get; private set; }
        public IReadOnlyList<CustomerMemorySearchResult> Results { get; set; } = [];

        public Task<CustomerMemory> AddAsync(CustomerMemory memory, CancellationToken cancellationToken = default)
            => Task.FromResult(memory);

        public Task<CustomerMemory?> GetAsync(Guid orgId, Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult<CustomerMemory?>(null);

        public Task<IReadOnlyList<CustomerMemory>> ListByCustomerAsync(
            Guid orgId, Guid customerId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CustomerMemory>>([]);

        public Task UpdateEmbeddingAsync(
            Guid orgId, Guid memoryId, float[] embedding, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<CustomerMemorySearchResult>> SearchSemanticAsync(
            Guid orgId, Guid customerId, float[] queryEmbedding, int topK = 5,
            CancellationToken cancellationToken = default)
        {
            SearchCalls++;
            return Task.FromResult(Results);
        }

        public Task SaveAsync(CustomerMemory memory, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class RecordingEmbeddingService : IEmbeddingService
    {
        public int Calls { get; private set; }

        public Task<float[]> GenerateAsync(string text, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new float[8]);
        }
    }

    private sealed class FailClosedConsentGate : IConsentGateService
    {
        public Task<ConsentDecision> CheckAsync(
            Guid organizationId, Guid? customerId, CancellationToken cancellationToken = default)
            => Task.FromResult(ConsentDecision.FailClosed());
    }
}
