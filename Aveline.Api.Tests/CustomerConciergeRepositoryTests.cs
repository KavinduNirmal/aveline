using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.CustomerConcierge.Models;
using Aveline.Api.Modules.CustomerConcierge.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Tests;

/// <summary>
/// In-memory repository tests for the Customer Concierge data-access layer (CRUD, tenant
/// scoping, relationships). pgvector-specific operations are covered separately against a real
/// PostgreSQL container in <see cref="CustomerMemoryRepositoryPostgresTests"/>.
/// </summary>
public class CustomerConciergeRepositoryTests
{
    private readonly AppDbContext _context;
    private readonly Guid _orgA = Guid.NewGuid();
    private readonly Guid _orgB = Guid.NewGuid();

    public CustomerConciergeRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"CustomerConciergeRepo_{Guid.NewGuid()}")
            .Options;
        _context = new AppDbContext(options);
    }

    private static Customer NewCustomer(Guid org, string phone) => new()
    {
        OrganizationId = org,
        PhoneNumber = phone,
        FullName = "Sarah Perera",
        Status = "new",
    };

    [Fact]
    public async Task CustomerRepository_GetByPhone_ReturnsNull_WhenUnknown()
    {
        var sut = new CustomerRepository(_context);
        var found = await sut.GetByPhoneAsync(_orgA, "+94770000000");
        Assert.Null(found);
    }

    [Fact]
    public async Task CustomerRepository_Add_SetsStatusNew_AndLookupByPhone()
    {
        var sut = new CustomerRepository(_context);
        var created = await sut.AddAsync(NewCustomer(_orgA, "+94771234567"));
        Assert.Equal("new", created.Status);

        var found = await sut.GetByPhoneAsync(_orgA, "+94771234567");
        Assert.NotNull(found);
        Assert.Equal(created.Id, found.Id);
    }

    [Fact]
    public async Task CustomerRepository_IsScopedPerOrganization()
    {
        var sut = new CustomerRepository(_context);
        await sut.AddAsync(NewCustomer(_orgA, "+94771234567"));

        var otherOrg = await sut.GetByPhoneAsync(_orgB, "+94771234567");
        Assert.Null(otherOrg);
    }

    [Fact]
    public async Task CustomerRepository_SoftDeletedCustomer_IsNotReturned()
    {
        var sut = new CustomerRepository(_context);
        var created = await sut.AddAsync(NewCustomer(_orgA, "+94771234567"));

        created.DeletedAt = DateTime.UtcNow;
        await sut.SaveAsync(created);

        var found = await sut.GetAsync(_orgA, created.Id);
        Assert.Null(found);
    }

    [Fact]
    public async Task CustomerRepository_Get_IncludesPreferencesAndTags()
    {
        var sut = new CustomerRepository(_context);
        var customer = await sut.AddAsync(NewCustomer(_orgA, "+94771234567"));

        _context.CustomerPreferences.Add(new CustomerPreference
        {
            OrganizationId = _orgA,
            CustomerId = customer.Id,
            PreferenceKey = "fabric",
            PreferenceValue = "silk",
            IsExplicit = true,
        });
        _context.CustomerTags.Add(new CustomerTag
        {
            OrganizationId = _orgA,
            CustomerId = customer.Id,
            Tag = "vip",
        });
        await _context.SaveChangesAsync();

        var loaded = await sut.GetAsync(_orgA, customer.Id);
        Assert.NotNull(loaded);
        Assert.Single(loaded.Preferences);
        Assert.Single(loaded.Tags);
    }

    [Fact]
    public async Task CustomerMemoryRepository_AddAndListByCustomer_IsScoped()
    {
        var sut = new CustomerMemoryRepository(_context);
        var customerId = Guid.NewGuid();
        await sut.AddAsync(new CustomerMemory
        {
            OrganizationId = _orgA,
            CustomerId = customerId,
            Content = "Prefers emerald silk",
            Category = "preference",
        });
        await sut.AddAsync(new CustomerMemory
        {
            OrganizationId = _orgB,
            CustomerId = customerId,
            Content = "Another org memory",
        });

        var memories = await sut.ListByCustomerAsync(_orgA, customerId);
        var memory = Assert.Single(memories);
        Assert.Equal("Prefers emerald silk", memory.Content);
    }

    [Fact]
    public async Task CustomerInteractionRepository_AddAndListByCustomer()
    {
        var sut = new CustomerInteractionRepository(_context);
        var customerId = Guid.NewGuid();
        await sut.AddAsync(new CustomerInteraction
        {
            OrganizationId = _orgA,
            CustomerId = customerId,
            Channel = "whatsapp",
            Direction = "inbound",
            MessageContent = "Hi! I need a blue saree.",
            ParsedIntentJson = "{\"occasion\":\"wedding\"}",
        });

        var interactions = await sut.ListByCustomerAsync(_orgA, customerId);
        var interaction = Assert.Single(interactions);
        Assert.Equal("whatsapp", interaction.Channel);
    }

    [Fact]
    public async Task CustomerConsentRepository_GetReturnsNullThenPersists()
    {
        var sut = new CustomerConsentRepository(_context);
        var customerId = Guid.NewGuid();

        Assert.Null(await sut.GetForCustomerAsync(_orgA, customerId));

        var consent = await sut.AddAsync(new CustomerConsent
        {
            OrganizationId = _orgA,
            CustomerId = customerId,
            ConsentStatus = "granted",
            ConsentGrantedAt = DateTime.UtcNow,
        });
        consent.ConsentStatus = "revoked";
        consent.ConsentRevokedAt = DateTime.UtcNow;
        await sut.SaveAsync(consent);

        var reloaded = await sut.GetForCustomerAsync(_orgA, customerId);
        Assert.NotNull(reloaded);
        Assert.Equal("revoked", reloaded.ConsentStatus);
    }

    [Fact]
    public async Task CustomerTagRepository_AddIsIdempotent_AndListsTags()
    {
        var sut = new CustomerTagRepository(_context);
        var customerId = Guid.NewGuid();
        await sut.AddAsync(new CustomerTag { OrganizationId = _orgA, CustomerId = customerId, Tag = "vip" });
        await sut.AddAsync(new CustomerTag { OrganizationId = _orgA, CustomerId = customerId, Tag = "vip" });
        await sut.AddAsync(new CustomerTag { OrganizationId = _orgA, CustomerId = customerId, Tag = "wedding" });

        var tags = await sut.ListByCustomerAsync(_orgA, customerId);
        Assert.Equal(new[] { "vip", "wedding" }, tags.OrderBy(t => t));
    }

    [Fact]
    public async Task CustomerTagRepository_IsScopedPerCustomer()
    {
        var sut = new CustomerTagRepository(_context);
        var customerA = Guid.NewGuid();
        var customerB = Guid.NewGuid();
        await sut.AddAsync(new CustomerTag { OrganizationId = _orgA, CustomerId = customerA, Tag = "vip" });

        var tagsForB = await sut.ListByCustomerAsync(_orgA, customerB);
        Assert.Empty(tagsForB);
    }
}
