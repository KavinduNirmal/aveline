using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Commerce.Controllers;
using Aveline.Api.Modules.Commerce.DTOs;
using Aveline.Api.Modules.Commerce.Repositories;
using Aveline.Api.Modules.Commerce.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aveline.Api.Tests;

public class CommerceOrdersControllerTests
{
    private readonly AppDbContext _context;
    private readonly OrderRepository _repository;
    private readonly OrderService _service;
    private readonly OrdersController _controller;
    private readonly Guid _orgId = Guid.NewGuid();

    public CommerceOrdersControllerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"OrdersControllerTest_{Guid.NewGuid()}")
            .Options;

        _context = new AppDbContext(options);
        _repository = new OrderRepository(_context);
        _service = new OrderService(_repository, NullLogger<OrderService>.Instance);
        _controller = new OrdersController(_service);
    }

    [Fact]
    public async Task GetOrders_ReturnsOkWithPagedResult()
    {
        await _service.CreateOrderAsync(_orgId, new CreateOrderDto
        {
            CustomerId = Guid.NewGuid(),
            CustomerName = "Controller List Test",
            Items = new List<OrderItemDto> { new() { ItemId = Guid.NewGuid(), ItemName = "Item", Quantity = 1, UnitPrice = 100 } }
        });

        var actionResult = await _controller.GetOrders(_orgId, new OrderQueryParametersDto());
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var paged = Assert.IsType<PagedResult<OrderResponseDto>>(okResult.Value);

        Assert.Equal(1, paged.TotalCount);
        Assert.Single(paged.Items);
    }

    [Fact]
    public async Task GetById_WhenFound_ReturnsOk_WhenNotFound_ReturnsNotFound()
    {
        var created = await _service.CreateOrderAsync(_orgId, new CreateOrderDto
        {
            CustomerId = Guid.NewGuid(),
            CustomerName = "GetById Test",
            Items = new List<OrderItemDto> { new() { ItemId = Guid.NewGuid(), ItemName = "Item", Quantity = 1, UnitPrice = 100 } }
        });

        var foundResult = await _controller.GetById(_orgId, created.Id);
        var okResult = Assert.IsType<OkObjectResult>(foundResult.Result);
        var dto = Assert.IsType<OrderResponseDto>(okResult.Value);
        Assert.Equal(created.Id, dto.Id);

        var notFoundResult = await _controller.GetById(_orgId, Guid.NewGuid());
        Assert.IsType<NotFoundResult>(notFoundResult.Result);
    }

    [Fact]
    public async Task Create_ValidOrder_ReturnsCreatedAtAction()
    {
        var dto = new CreateOrderDto
        {
            CustomerId = Guid.NewGuid(),
            CustomerName = "New Customer",
            Items = new List<OrderItemDto>
            {
                new() { ItemId = Guid.NewGuid(), ItemName = "Product", Quantity = 2, UnitPrice = 500, WholesaleCost = 300 }
            }
        };

        var result = await _controller.Create(_orgId, dto);
        var createdResult = Assert.IsType<CreatedAtActionResult>(result.Result);
        var response = Assert.IsType<OrderResponseDto>(createdResult.Value);

        Assert.Equal("New Customer", response.CustomerName);
        Assert.Equal(1000m, response.Total);
    }

    [Fact]
    public async Task UpdateStatus_ValidTransition_ReturnsOk()
    {
        var created = await _service.CreateOrderAsync(_orgId, new CreateOrderDto
        {
            CustomerId = Guid.NewGuid(),
            CustomerName = "Status Customer",
            Items = new List<OrderItemDto> { new() { ItemId = Guid.NewGuid(), ItemName = "P", Quantity = 1, UnitPrice = 50 } }
        });

        var result = await _controller.UpdateStatus(_orgId, created.Id, new UpdateOrderStatusDto { Status = "payment_confirmed" });
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var updated = Assert.IsType<OrderResponseDto>(okResult.Value);

        Assert.Equal("payment_confirmed", updated.Status);
    }

    [Fact]
    public async Task UpdateStatus_InvalidTransition_ReturnsBadRequest()
    {
        var created = await _service.CreateOrderAsync(_orgId, new CreateOrderDto
        {
            CustomerId = Guid.NewGuid(),
            CustomerName = "Bad Transition Customer",
            Items = new List<OrderItemDto> { new() { ItemId = Guid.NewGuid(), ItemName = "P", Quantity = 1, UnitPrice = 50 } }
        });

        var result = await _controller.UpdateStatus(_orgId, created.Id, new UpdateOrderStatusDto { Status = "completed" });
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Cancel_ExistingOrder_ReturnsOk()
    {
        var created = await _service.CreateOrderAsync(_orgId, new CreateOrderDto
        {
            CustomerId = Guid.NewGuid(),
            CustomerName = "Cancel Customer",
            Items = new List<OrderItemDto> { new() { ItemId = Guid.NewGuid(), ItemName = "P", Quantity = 1, UnitPrice = 50 } }
        });

        var result = await _controller.Cancel(_orgId, created.Id, "Out of stock");
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task Recalculate_ExistingOrder_ReturnsOk()
    {
        var created = await _service.CreateOrderAsync(_orgId, new CreateOrderDto
        {
            CustomerId = Guid.NewGuid(),
            CustomerName = "Recalc Customer",
            Items = new List<OrderItemDto> { new() { ItemId = Guid.NewGuid(), ItemName = "P", Quantity = 1, UnitPrice = 50 } }
        });

        var result = await _controller.Recalculate(_orgId, created.Id);
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.IsType<OrderResponseDto>(okResult.Value);
    }
}
