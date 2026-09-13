using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Commerce.DTOs;
using Aveline.Api.Modules.Commerce.Models;
using Aveline.Api.Modules.Commerce.Repositories;
using Aveline.Api.Modules.Commerce.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aveline.Api.Tests;

public class CommerceOrderServiceTests
{
    private readonly AppDbContext _context;
    private readonly OrderRepository _orderRepo;
    private readonly BusinessRulesRepository _rulesRepo;
    private readonly BusinessRulesService _rulesService;
    private readonly OrderService _orderService;
    private readonly Guid _orgId = Guid.NewGuid();

    public CommerceOrderServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"OrderServiceTest_{Guid.NewGuid()}")
            .Options;

        _context = new AppDbContext(options);
        _orderRepo = new OrderRepository(_context);
        _rulesRepo = new BusinessRulesRepository(_context);
        _rulesService = new BusinessRulesService(_rulesRepo, NullLogger<BusinessRulesService>.Instance);
        _orderService = new OrderService(_orderRepo, NullLogger<OrderService>.Instance, _rulesService);
    }

    [Fact]
    public async Task CreateOrderAsync_CalculatesTotalsAndMarginAccurately()
    {
        var dto = new CreateOrderDto
        {
            CustomerId = Guid.NewGuid(),
            CustomerName = "Jane Doe",
            OrderType = "whatsapp",
            Items = new List<OrderItemDto>
            {
                new OrderItemDto { ItemId = Guid.NewGuid(), ItemName = "Item 1", Quantity = 2, UnitPrice = 1000m, WholesaleCost = 600m },
                new OrderItemDto { ItemId = Guid.NewGuid(), ItemName = "Item 2", Quantity = 1, UnitPrice = 3000m, WholesaleCost = 1800m }
            },
            Discount = 200m
        };

        // Subtotal = (2 * 1000) + (1 * 3000) = 5000
        // TotalCost = (2 * 600) + (1 * 1800) = 3000
        // Total = 5000 - 200 = 4800
        // Margin = (4800 - 3000) / 4800 = 1800 / 4800 = 0.3750

        var result = await _orderService.CreateOrderAsync(_orgId, dto);

        Assert.NotNull(result);
        Assert.Equal(5000m, result.Subtotal);
        Assert.Equal(200m, result.Discount);
        Assert.Equal(4800m, result.Total);
        Assert.Equal(3000m, result.TotalCost);
        Assert.Equal(0.3750m, result.Margin);
        Assert.Equal("payment_requested", result.Status);
        Assert.Equal(2, result.Items.Count);
    }

    [Theory]
    [InlineData("VIP", 5000, 250)]       // 5% of 5000 = 250
    [InlineData("Bronze", 5000, 150)]    // 3% of 5000 = 150
    [InlineData("Silver", 5000, 350)]    // 7% of 5000 = 350
    [InlineData("Gold", 5000, 500)]      // 10% of 5000 = 500
    [InlineData("Platinum", 5000, 750)]  // 15% of 5000 = 750
    [InlineData("Standard", 5000, 0)]    // 0%
    public void CalculateTierDiscount_ReturnsExpectedDiscount(string tier, decimal subtotal, decimal expectedDiscount)
    {
        var discount = OrderService.CalculateTierDiscount(subtotal, tier);
        Assert.Equal(expectedDiscount, discount);
    }

    [Fact]
    public async Task CreateOrderAsync_WithTierDiscount_AppliesCorrectDiscount()
    {
        var dto = new CreateOrderDto
        {
            CustomerId = Guid.NewGuid(),
            CustomerName = "VIP Customer",
            CustomerTier = "VIP",
            Items = new List<OrderItemDto>
            {
                new OrderItemDto { ItemId = Guid.NewGuid(), ItemName = "Dress", Quantity = 1, UnitPrice = 10000m, WholesaleCost = 5000m }
            }
        };

        // Subtotal = 10000, VIP = 5% => Discount = 500, Total = 9500
        var result = await _orderService.CreateOrderAsync(_orgId, dto);

        Assert.Equal(10000m, result.Subtotal);
        Assert.Equal(500m, result.Discount);
        Assert.Equal(9500m, result.Total);
    }

    [Fact]
    public async Task CreateOrderAsync_WhenBusinessRuleTriggered_SetsPendingApproval()
    {
        // Add a high-value order rule: threshold 20000
        await _rulesService.CreateRuleAsync(_orgId, new CreateBusinessRuleDto(
            "High Value Rule",
            "high_value",
            "{\"threshold\": 20000}",
            "Require approval above 20k"));

        var dto = new CreateOrderDto
        {
            CustomerId = Guid.NewGuid(),
            CustomerName = "Big Spender",
            Items = new List<OrderItemDto>
            {
                new OrderItemDto { ItemId = Guid.NewGuid(), ItemName = "Luxury Item", Quantity = 1, UnitPrice = 25000m, WholesaleCost = 15000m }
            }
        };

        var result = await _orderService.CreateOrderAsync(_orgId, dto);
        Assert.Equal("pending_approval", result.Status);
    }

    [Fact]
    public async Task TransitionStatusAsync_ValidTransitions_Succeeds()
    {
        var created = await _orderService.CreateOrderAsync(_orgId, new CreateOrderDto
        {
            CustomerId = Guid.NewGuid(),
            CustomerName = "Transition Test",
            Items = new List<OrderItemDto>
            {
                new OrderItemDto { ItemId = Guid.NewGuid(), ItemName = "Item", Quantity = 1, UnitPrice = 1000m, WholesaleCost = 500m }
            }
        });

        // payment_requested -> payment_confirmed
        var updated = await _orderService.TransitionStatusAsync(created.Id, _orgId, new UpdateOrderStatusDto { Status = "payment_confirmed" });
        Assert.Equal("payment_confirmed", updated.Status);

        // payment_confirmed -> delivery_scheduled
        updated = await _orderService.TransitionStatusAsync(created.Id, _orgId, new UpdateOrderStatusDto { Status = "delivery_scheduled" });
        Assert.Equal("delivery_scheduled", updated.Status);

        // delivery_scheduled -> delivered
        updated = await _orderService.TransitionStatusAsync(created.Id, _orgId, new UpdateOrderStatusDto { Status = "delivered" });
        Assert.Equal("delivered", updated.Status);

        // delivered -> completed
        updated = await _orderService.TransitionStatusAsync(created.Id, _orgId, new UpdateOrderStatusDto { Status = "completed" });
        Assert.Equal("completed", updated.Status);
    }

    [Fact]
    public async Task TransitionStatusAsync_InvalidTransition_ThrowsInvalidOperationException()
    {
        var created = await _orderService.CreateOrderAsync(_orgId, new CreateOrderDto
        {
            CustomerId = Guid.NewGuid(),
            CustomerName = "Invalid Transition Test",
            Items = new List<OrderItemDto>
            {
                new OrderItemDto { ItemId = Guid.NewGuid(), ItemName = "Item", Quantity = 1, UnitPrice = 1000m, WholesaleCost = 500m }
            }
        });

        // payment_requested cannot directly jump to completed
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _orderService.TransitionStatusAsync(created.Id, _orgId, new UpdateOrderStatusDto { Status = "completed" }));
    }

    [Fact]
    public async Task CancelOrderAsync_OpenOrder_Succeeds()
    {
        var created = await _orderService.CreateOrderAsync(_orgId, new CreateOrderDto
        {
            CustomerId = Guid.NewGuid(),
            CustomerName = "Cancel Test",
            Items = new List<OrderItemDto>
            {
                new OrderItemDto { ItemId = Guid.NewGuid(), ItemName = "Item", Quantity = 1, UnitPrice = 1000m, WholesaleCost = 500m }
            }
        });

        var success = await _orderService.CancelOrderAsync(created.Id, _orgId, "Customer requested");
        Assert.True(success);

        var fetched = await _orderService.GetOrderByIdAsync(created.Id, _orgId);
        Assert.Equal("cancelled", fetched?.Status);
    }

    [Fact]
    public async Task RecalculateOrderAsync_UpdatesTotalsAccurately()
    {
        var order = new Order
        {
            OrganizationId = _orgId,
            CustomerId = Guid.NewGuid(),
            CustomerName = "Recalc Test",
            Status = "pending_hold",
            Subtotal = 0m,
            Discount = 200m,
            Items = new List<OrderItem>
            {
                new OrderItem { ItemId = Guid.NewGuid(), ItemName = "A", Quantity = 3, UnitPrice = 1000m, WholesaleCost = 400m, TotalPrice = 0m }
            }
        };

        await _orderRepo.CreateAsync(order);

        var result = await _orderService.RecalculateOrderAsync(order.Id, _orgId);

        // Subtotal = 3000, Discount = 200, Total = 2800, TotalCost = 1200, Margin = (2800 - 1200) / 2800 = 1600 / 2800 = 0.5714
        Assert.Equal(3000m, result.Subtotal);
        Assert.Equal(2800m, result.Total);
        Assert.Equal(1200m, result.TotalCost);
        Assert.Equal(0.5714m, result.Margin);
    }
}
