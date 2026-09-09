using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.CustomerConcierge.Models;
using Aveline.Api.Modules.CustomerConcierge.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Tests;

/// <summary>
/// Repository tests for the read-only customer lookup path (Issue #161). The lookup is
/// org-scoped, soft-delete aware, matches names case-insensitively and matches phones by
/// exact or normalized (E.164) form.
/// </summary>
public class CustomerConciergeLookupRepositoryTests
{
    private readonly AppDbContext _context;
    private readonly Guid _orgA = Guid.NewGuid();
    private readonly Guid _orgB = Guid.NewGuid();

    public CustomerConciergeLookupRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"CustomerLookupRepo_{Guid.NewGuid()}")
            .Options;
        _context = new AppDbContext(options);
    }

    private async Task<Customer> SeedAsync(Guid org, string phone, string? name = "Samantha Arias")
    {
        var created = await _context.Customers.AddAsync(new Customer
        {
            OrganizationId = org,
            PhoneNumber = phone,
            FullName = name,
            Status = "new",
        });
        await _context.SaveChangesAsync();
        return created.Entity;
    }

    private CustomerRepository Sut() => new(_context);

    [Fact]
    public async Task ListMatches_ByName_CaseInsensitiveContains()
    {
        var sut = Sut();
        await SeedAsync(_orgA, "+94770000001", "Samantha Arias");
        await SeedAsync(_orgA, "+94770000002", "Samantha Ranaweera");

        var matches = await sut.ListMatchesAsync(_orgA, "sam", null, 5);

        Assert.Equal(2, matches.Count);
        Assert.All(matches, m => Assert.Contains("Sam", m.FullName, System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ListMatches_ByName_PartialLastWord()
    {
        var sut = Sut();
        await SeedAsync(_orgA, "+94770000001", "Samantha Arias");

        var matches = await sut.ListMatchesAsync(_orgA, "Arias", null, 5);

        Assert.Single(matches);
        Assert.Equal("Samantha Arias", matches[0].FullName);
    }

    [Fact]
    public async Task ListMatches_ByName_NoMatch_ReturnsEmpty()
    {
        var sut = Sut();
        await SeedAsync(_orgA, "+94770000001", "Samantha Arias");

        var matches = await sut.ListMatchesAsync(_orgA, "Zara Nobody", null, 5);

        Assert.Empty(matches);
    }

    [Fact]
    public async Task ListMatches_ByPhone_ExactMatch()
    {
        var sut = Sut();
        var seeded = await SeedAsync(_orgA, "+94771234567");

        var matches = await sut.ListMatchesAsync(_orgA, null, "+94771234567", 5);

        Assert.Single(matches);
        Assert.Equal(seeded.Id, matches[0].Id);
    }

    [Fact]
    public async Task ListMatches_ByPhone_LocalFormat_MatchesNormalizedStorage()
    {
        // Stored as E.164 (canonical) but the query uses a leading-0 local format.
        var sut = Sut();
        var seeded = await SeedAsync(_orgA, "+94771234567");

        var matches = await sut.ListMatchesAsync(_orgA, null, "0771234567", 5);

        Assert.Single(matches);
        Assert.Equal(seeded.Id, matches[0].Id);
    }

    [Fact]
    public async Task ListMatches_IsScopedPerOrganization()
    {
        var sut = Sut();
        await SeedAsync(_orgB, "+94771234567", "Samantha Arias");

        var matches = await sut.ListMatchesAsync(_orgA, "Samantha", null, 5);

        Assert.Empty(matches);
    }

    [Fact]
    public async Task ListMatches_ExcludesSoftDeleted()
    {
        var sut = Sut();
        var seeded = await SeedAsync(_orgA, "+94771234567", "Samantha Arias");
        seeded.DeletedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        var matches = await sut.ListMatchesAsync(_orgA, "Samantha", null, 5);

        Assert.Empty(matches);
    }

    [Fact]
    public async Task ListMatches_RespectsLimit()
    {
        var sut = Sut();
        for (var i = 0; i < 5; i++)
        {
            await SeedAsync(_orgA, $"+9477123456{i}", "Samantha Arias");
        }

        var matches = await sut.ListMatchesAsync(_orgA, "Samantha", null, 3);

        Assert.Equal(3, matches.Count);
    }

    [Fact]
    public async Task ListMatches_NoCriteria_ReturnsEmpty()
    {
        var sut = Sut();
        await SeedAsync(_orgA, "+94771234567", "Samantha Arias");

        var matches = await sut.ListMatchesAsync(_orgA, null, null, 5);

        Assert.Empty(matches);
    }
}
