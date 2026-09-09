using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.CustomerConcierge.DTOs;
using Aveline.Api.Modules.CustomerConcierge.Models;
using Aveline.Api.Modules.CustomerConcierge.Repositories;
using Aveline.Api.Modules.CustomerConcierge.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aveline.Api.Tests;

/// <summary>
/// Service-level tests for the read-only customer lookup (Issue #161). Verifies the
/// exact / multiple / empty resolution contract and that repeat lookups are served from the
/// distributed cache without re-querying the repository.
/// </summary>
public class CustomerConciergeLookupServiceTests
{
    private readonly AppDbContext _context;
    private readonly Guid _orgA = Guid.NewGuid();

    public CustomerConciergeLookupServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"CustomerLookupSvc_{Guid.NewGuid()}")
            .Options;
        _context = new AppDbContext(options);
    }

    private async Task<Customer> SeedAsync(string phone, string? name)
    {
        var created = await _context.Customers.AddAsync(new Customer
        {
            OrganizationId = _orgA,
            PhoneNumber = phone,
            FullName = name,
            Status = "new",
        });
        await _context.SaveChangesAsync();
        return created.Entity;
    }

    private CustomerService BuildSut(CountingCustomerRepository? repo = null)
    {
        var customers = repo ?? new CountingCustomerRepository(new CustomerRepository(_context));
        return new CustomerService(
            customers,
            new CustomerConsentRepository(_context),
            new CustomerTagRepository(_context),
            new TestDistributedCache(),
            NullLogger<CustomerService>.Instance);
    }

    [Fact]
    public async Task Lookup_ExactPhone_ReturnsSingleExactMatch()
    {
        var sut = BuildSut();
        await SeedAsync("+94771234567", "Samantha Arias");

        var result = await sut.LookupAsync(new CustomerLookupRequest
        {
            OrganizationId = _orgA,
            PhoneNumber = "+94771234567",
        });

        Assert.True(result.IsExact);
        Assert.Equal(1, result.Total);
        Assert.Equal("Samantha Arias", result.Matches[0].FullName);
    }

    [Fact]
    public async Task Lookup_ByName_MultipleMatches_NotExact()
    {
        var sut = BuildSut();
        await SeedAsync("+94770000001", "Samantha Arias");
        await SeedAsync("+94770000002", "Samantha Ranaweera");

        var result = await sut.LookupAsync(new CustomerLookupRequest
        {
            OrganizationId = _orgA,
            Name = "Samantha",
        });

        Assert.False(result.IsExact);
        Assert.Equal(2, result.Total);
    }

    [Fact]
    public async Task Lookup_ByName_NoMatch_ReturnsEmpty()
    {
        var sut = BuildSut();
        await SeedAsync("+94770000001", "Samantha Arias");

        var result = await sut.LookupAsync(new CustomerLookupRequest
        {
            OrganizationId = _orgA,
            Name = "Zara Nobody",
        });

        Assert.Empty(result.Matches);
        Assert.False(result.IsExact);
        Assert.Equal(0, result.Total);
    }

    [Fact]
    public async Task Lookup_NeverCreatesAProfile()
    {
        var sut = BuildSut();

        var result = await sut.LookupAsync(new CustomerLookupRequest
        {
            OrganizationId = _orgA,
            Name = "Brand New Person",
        });

        Assert.Empty(result.Matches);
        Assert.Equal(0, await _context.Customers.CountAsync());
    }

    [Fact]
    public async Task Lookup_IsScopedPerOrganization()
    {
        var sut = BuildSut();
        await SeedAsync("+94770000001", "Samantha Arias");

        var result = await sut.LookupAsync(new CustomerLookupRequest
        {
            OrganizationId = Guid.NewGuid(),
            Name = "Samantha",
        });

        Assert.Empty(result.Matches);
    }

    [Fact]
    public async Task Lookup_RepeatCall_HitsCacheAndSkipsRepository()
    {
        var repo = new CountingCustomerRepository(new CustomerRepository(_context));
        var sut = BuildSut(repo);
        await SeedAsync("+94771234567", "Samantha Arias");
        var request = new CustomerLookupRequest { OrganizationId = _orgA, Name = "Samantha" };

        var first = await sut.LookupAsync(request);
        var second = await sut.LookupAsync(request);

        Assert.Equal(first.Matches.Single().CustomerId, second.Matches.Single().CustomerId);
        Assert.Equal(1, repo.LookupCalls);
    }

    /// <summary>Decorates the real repository, counting how often the lookup hits storage.</summary>
    private sealed class CountingCustomerRepository : ICustomerRepository
    {
        private readonly CustomerRepository _inner;

        public CountingCustomerRepository(CustomerRepository inner) => _inner = inner;

        public int LookupCalls { get; private set; }

        public Task<Customer?> GetAsync(Guid orgId, Guid id, CancellationToken cancellationToken = default)
            => _inner.GetAsync(orgId, id, cancellationToken);

        public Task<Customer?> GetByPhoneAsync(Guid orgId, string phoneNumber, CancellationToken cancellationToken = default)
            => _inner.GetByPhoneAsync(orgId, phoneNumber, cancellationToken);

        public Task<Customer> AddAsync(Customer customer, CancellationToken cancellationToken = default)
            => _inner.AddAsync(customer, cancellationToken);

        public Task SaveAsync(Customer customer, CancellationToken cancellationToken = default)
            => _inner.SaveAsync(customer, cancellationToken);

        public async Task<IReadOnlyList<Customer>> ListMatchesAsync(
            Guid orgId, string? name, string? phoneNumber, int limit, CancellationToken cancellationToken = default, string? email = null)
        {
            LookupCalls++;
            return await _inner.ListMatchesAsync(orgId, name, phoneNumber, limit, cancellationToken, email);
        }
    }
}
