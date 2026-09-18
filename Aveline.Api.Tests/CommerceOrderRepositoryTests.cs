using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Commerce.DTOs;
using Aveline.Api.Modules.Commerce.Models;
using Aveline.Api.Modules.Commerce.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Aveline.Api.Tests;

public class CommerceOrderRepositoryTests
{
    private readonly AppDbContext _context;
    private readonly OrderRepository _repository;
    private readonly Guid _orgId = Guid.NewGuid();
    private readonly Guid _otherOrgId = Guid.NewGuid();

    public CommerceOrderRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"OrderRepoTest_{Guid.NewGuid()}")
            .Options;

        _context = new AppDbContext(options);
        _repository = new OrderRepository(_context);
    }

    [Fact]
    public async Task CreateAsync_PersistsOrderAndItems()
    {
        var order = new Order
        {
            OrganizationId = _orgId,
            CustomerId = Guid.NewGuid(),
            CustomerName = "John Doe",
            OrderType = "whatsapp",
            Status = "pending_hold",
            Subtotal = 10000m,
            Discount = 500m,
            Total = 9500m,
            TotalCost = 6000m,
            Margin = 0.3684m,
            Items = new List<OrderItem>
            {
                new OrderItem
                {
                    ItemId = Guid.NewGuid(),
                    ItemName = "Silk Shirt",
                    Quantity = 2,
                    UnitPrice = 5000m,
                    WholesaleCost = 3000m,
                    TotalPrice = 10000m
                }
            }
        };

        var created = await _repository.CreateAsync(order);

        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Single(created.Items);
        Assert.Equal(created.Id, created.Items.First().OrderId);
        Assert.Equal(_orgId, created.Items.First().OrganizationId);

        var fetched = await _repository.GetByIdAsync(created.Id, _orgId);
        Assert.NotNull(fetched);
        Assert.Equal("John Doe", fetched.CustomerName);
        Assert.Single(fetched.Items);
    }

    [Fact]
    public async Task GetByIdAsync_WithDifferentOrg_ReturnsNull()
    {
        var order = new Order
        {
            OrganizationId = _orgId,
            CustomerId = Guid.NewGuid(),
            CustomerName = "Alice Smith",
            Status = "pending_hold"
        };
        await _repository.CreateAsync(order);

        var result = await _repository.GetByIdAsync(order.Id, _otherOrgId);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsOnlyOrgOrders()
    {
        await _repository.CreateAsync(new Order { OrganizationId = _orgId, CustomerName = "User 1", Status = "pending_hold" });
        await _repository.CreateAsync(new Order { OrganizationId = _orgId, CustomerName = "User 2", Status = "completed" });
        await _repository.CreateAsync(new Order { OrganizationId = _otherOrgId, CustomerName = "Other User", Status = "pending_hold" });

        var results = await _repository.GetAllAsync(_orgId);

        Assert.Equal(2, results.Count);
        Assert.All(results, o => Assert.Equal(_orgId, o.OrganizationId));
    }

    [Fact]
    public async Task GetPagedAsync_FiltersByStatusAndCustomer()
    {
        var cust1 = Guid.NewGuid();
        var cust2 = Guid.NewGuid();

        await _repository.CreateAsync(new Order { OrganizationId = _orgId, CustomerId = cust1, CustomerName = "Cust 1", Status = "pending_approval" });
        await _repository.CreateAsync(new Order { OrganizationId = _orgId, CustomerId = cust1, CustomerName = "Cust 1", Status = "payment_requested" });
        await _repository.CreateAsync(new Order { OrganizationId = _orgId, CustomerId = cust2, CustomerName = "Cust 2", Status = "payment_requested" });

        var (items, total) = await _repository.GetPagedAsync(_orgId, new OrderQueryParametersDto
        {
            CustomerId = cust1,
            Status = "payment_requested"
        });

        Assert.Equal(1, total);
        Assert.Single(items);
        Assert.Equal(cust1, items[0].CustomerId);
        Assert.Equal("payment_requested", items[0].Status);
    }

    [Fact]
    public async Task UpdateAsync_UpdatesPropertiesAndTimestamp()
    {
        var order = await _repository.CreateAsync(new Order { OrganizationId = _orgId, CustomerName = "Updatable", Status = "pending_hold" });

        order.Status = "payment_requested";
        var updated = await _repository.UpdateAsync(order);

        Assert.Equal("payment_requested", updated.Status);
        Assert.NotNull(updated.UpdatedAt);
    }

    [Fact]
    public async Task DeleteAsync_RemovesOrder()
    {
        var order = await _repository.CreateAsync(new Order { OrganizationId = _orgId, CustomerName = "To Delete", Status = "pending_hold" });

        var success = await _repository.DeleteAsync(order.Id, _orgId);
        Assert.True(success);

        var fetched = await _repository.GetByIdAsync(order.Id, _orgId);
        Assert.Null(fetched);
    }
}
