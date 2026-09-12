using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.CustomerConcierge.Repositories;
using Aveline.Api.Modules.CustomerConcierge.Services;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Tests;

/// <summary>
/// Tests for loyalty/tier progression (Issue #169): the pure deterministic rule boundaries and
/// the recompute/override service path against the in-memory EF provider.
/// </summary>
public class CustomerLoyaltyServiceTests
{
    private readonly AppDbContext _context;
    private readonly Guid _orgA = Guid.NewGuid();

    public CustomerLoyaltyServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"Loyalty_{Guid.NewGuid()}")
            .Options;
        _context = new AppDbContext(options);
    }

    private static readonly DateTime Now = new(2026, 9, 9, 0, 0, 0, DateTimeKind.Utc);

    private CustomerLoyaltyService Sut() => new(new CustomerRepository(_context));

    // ------------------------------------------------------------ pure rule

    [Theory]
    [InlineData(0, 0, "new")]
    [InlineData(10000, 0, "new")]          // spend == threshold -> not yet returning
    [InlineData(0, 1, "new")]              // single visit -> not yet returning
    [InlineData(10001, 0, "returning")]    // spend above returning threshold
    [InlineData(0, 2, "returning")]        // second visit
    [InlineData(50000, 5, "returning")]    // vip spend threshold is exclusive
    [InlineData(50001, 5, "vip")]
    [InlineData(60000, 3, "returning")]    // high spend but < vip visits
    public void RecommendStatus_PromotesOnSpendAndVisits(decimal spend, int visits, string expected)
    {
        var result = CustomerLoyaltyService.RecommendStatus(spend, visits, lastVisitAt: Now.AddDays(-1), Now);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void RecommendStatus_NeverVisited_IsNotDormant()
    {
        var result = CustomerLoyaltyService.RecommendStatus(0, 0, lastVisitAt: null, Now);
        Assert.Equal("new", result);
    }

    [Fact]
    public void RecommendStatus_Dormant_AfterNinetyDaysInactive()
    {
        var result = CustomerLoyaltyService.RecommendStatus(50001, 6, lastVisitAt: Now.AddDays(-91), Now);
        Assert.Equal("dormant", result);
    }

    [Fact]
    public void RecommendStatus_RecentVisit_NotDormant()
    {
        var result = CustomerLoyaltyService.RecommendStatus(50001, 6, lastVisitAt: Now.AddDays(-89), Now);
        Assert.Equal("vip", result);
    }

    // ------------------------------------------------------- service path

    private async Task<Guid> SeedCustomerAsync(decimal spend, int visits, DateTime? lastVisit)
    {
        var created = await _context.Customers.AddAsync(new()
        {
            OrganizationId = _orgA,
            PhoneNumber = $"+94771234{Guid.NewGuid():N}"[..13],
            FullName = "Sarah Perera",
            Status = "new",
            TotalSpent = spend,
            VisitCount = visits,
            LastVisitAt = lastVisit,
        });
        await _context.SaveChangesAsync();
        return created.Entity.Id;
    }

    [Fact]
    public async Task RecomputeAsync_PromotesToReturning_AndPersists()
    {
        var id = await SeedCustomerAsync(0, 2, Now.AddDays(-1));
        var dto = await Sut().RecomputeAsync(_orgA, id);

        Assert.NotNull(dto);
        Assert.Equal("returning", dto!.Status);
        var row = await _context.Customers.SingleAsync(c => c.Id == id);
        Assert.Equal("returning", row.Status);
    }

    [Fact]
    public async Task RecomputeAsync_OwnerOverride_IsRespected()
    {
        var id = await SeedCustomerAsync(0, 0, Now.AddDays(-1));
        var dto = await Sut().RecomputeAsync(_orgA, id, overrideStatus: "vip");

        Assert.Equal("vip", dto!.Status);
    }

    [Fact]
    public async Task RecomputeAsync_InvalidOverride_Throws()
    {
        var id = await SeedCustomerAsync(0, 0, Now.AddDays(-1));
        await Assert.ThrowsAsync<ArgumentException>(
            () => Sut().RecomputeAsync(_orgA, id, overrideStatus: "deleted"));
    }

    [Fact]
    public async Task RecomputeAsync_UnknownCustomer_ReturnsNull()
    {
        var dto = await Sut().RecomputeAsync(_orgA, Guid.NewGuid());
        Assert.Null(dto);
    }
}
