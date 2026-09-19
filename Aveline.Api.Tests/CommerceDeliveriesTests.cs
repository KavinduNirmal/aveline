using System.Security.Claims;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Commerce.Controllers;
using Aveline.Api.Modules.Commerce.DTOs;
using Aveline.Api.Modules.Commerce.Models;
using Aveline.Api.Modules.Commerce.Repositories;
using Aveline.Api.Modules.Commerce.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Aveline.Api.Tests;

public class CommerceDeliveriesTests
{
    private static AppDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task DeliveryRepository_AddAndGetById_ReturnsCorrectDeliveryPlan()
    {
        using var context = CreateInMemoryDbContext();
        var repo = new DeliveryRepository(context);
        var orderRepo = new OrderRepository(context);
        var orgId = Guid.NewGuid();

        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            CustomerId = Guid.NewGuid(),
            CustomerName = "Kasun Jayasuriya",
            Status = "payment_confirmed",
            Subtotal = 20000m,
            Total = 20000m,
            CreatedAt = DateTime.UtcNow
        };
        await orderRepo.CreateAsync(order);

        var plan = new DeliveryPlan
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            OrderId = order.Id,
            CourierService = "PickMe",
            TrackingNumber = "TRK-PI-123456",
            DeliveryAddress = "45 Galle Road, Colombo 03",
            EstimatedCost = 650.00m,
            Status = "planned",
            CreatedAt = DateTime.UtcNow
        };
        await repo.AddAsync(plan);

        var retrieved = await repo.GetByIdAsync(plan.Id, orgId);
        Assert.NotNull(retrieved);
        Assert.Equal(plan.Id, retrieved.Id);
        Assert.Equal("PickMe", retrieved.CourierService);
        Assert.Equal("planned", retrieved.Status);
    }

    [Fact]
    public async Task DeliveryRepository_ListAsync_FiltersByStatusAndCourier()
    {
        using var context = CreateInMemoryDbContext();
        var repo = new DeliveryRepository(context);
        var orderRepo = new OrderRepository(context);
        var orgId = Guid.NewGuid();

        for (int i = 0; i < 4; i++)
        {
            var order = new Order
            {
                Id = Guid.NewGuid(),
                OrganizationId = orgId,
                CustomerId = Guid.NewGuid(),
                CustomerName = $"Test Order {i}",
                Status = "payment_confirmed",
                Subtotal = 5000m,
                Total = 5000m,
                CreatedAt = DateTime.UtcNow.AddMinutes(i)
            };
            await orderRepo.CreateAsync(order);

            await repo.AddAsync(new DeliveryPlan
            {
                Id = Guid.NewGuid(),
                OrganizationId = orgId,
                OrderId = order.Id,
                CourierService = i % 2 == 0 ? "PickMe" : "Uber",
                DeliveryAddress = $"Address {i}",
                Status = i < 2 ? "planned" : "in_transit",
                CreatedAt = DateTime.UtcNow.AddMinutes(i)
            });
        }

        var paged = await repo.ListAsync(orgId, new DeliveryQueryParametersDto
        {
            CourierService = "PickMe"
        });

        Assert.Equal(2, paged.TotalCount);
        Assert.All(paged.Items, p => Assert.Equal("PickMe", p.CourierService));
    }

    [Fact]
    public async Task DeliveryService_CreateDeliveryPlan_CalculatesColomboRateAndSetsOrderScheduled()
    {
        using var context = CreateInMemoryDbContext();
        var deliveryRepo = new DeliveryRepository(context);
        var orderRepo = new OrderRepository(context);
        var service = new DeliveryService(deliveryRepo, orderRepo);

        var orgId = Guid.NewGuid();
        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            CustomerId = Guid.NewGuid(),
            CustomerName = "Dinithi Silva",
            Status = "payment_confirmed",
            Subtotal = 35000m,
            Total = 35000m,
            CreatedAt = DateTime.UtcNow
        };
        await orderRepo.CreateAsync(order);

        var dto = new CreateDeliveryDto
        {
            OrderId = order.Id,
            CourierService = "PickMe",
            DeliveryAddress = "12/A Flower Road, Colombo 07"
        };

        var response = await service.CreateDeliveryPlanAsync(orgId, dto);

        Assert.NotNull(response);
        Assert.Equal("planned", response.Status);
        Assert.Equal(650.00m, response.EstimatedCost);
        Assert.StartsWith("TRK-PI-", response.TrackingNumber!);

        var updatedOrder = await orderRepo.GetByIdAsync(order.Id, orgId);
        Assert.NotNull(updatedOrder);
        Assert.Equal("delivery_scheduled", updatedOrder.Status);
    }

    [Fact]
    public async Task DeliveryService_CreateDeliveryPlan_CalculatesOutstationRate()
    {
        using var context = CreateInMemoryDbContext();
        var deliveryRepo = new DeliveryRepository(context);
        var orderRepo = new OrderRepository(context);
        var service = new DeliveryService(deliveryRepo, orderRepo);

        var orgId = Guid.NewGuid();
        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            CustomerId = Guid.NewGuid(),
            CustomerName = "Pradeep Kumara",
            Status = "payment_confirmed",
            Subtotal = 18000m,
            Total = 18000m,
            CreatedAt = DateTime.UtcNow
        };
        await orderRepo.CreateAsync(order);

        var dto = new CreateDeliveryDto
        {
            OrderId = order.Id,
            CourierService = "In-house",
            DeliveryAddress = "88 Kandy Road, Peradeniya"
        };

        var response = await service.CreateDeliveryPlanAsync(orgId, dto);

        Assert.Equal(850.00m, response.EstimatedCost);
    }

    [Fact]
    public async Task DeliveryService_UpdateDeliveryStatus_TransitionsStatusAndUpdatesDeliveredOrder()
    {
        using var context = CreateInMemoryDbContext();
        var deliveryRepo = new DeliveryRepository(context);
        var orderRepo = new OrderRepository(context);
        var service = new DeliveryService(deliveryRepo, orderRepo);

        var orgId = Guid.NewGuid();
        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            CustomerId = Guid.NewGuid(),
            CustomerName = "Anoma Senanayake",
            Status = "delivery_scheduled",
            Subtotal = 22000m,
            Total = 22000m,
            CreatedAt = DateTime.UtcNow
        };
        await orderRepo.CreateAsync(order);

        var plan = new DeliveryPlan
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            OrderId = order.Id,
            CourierService = "Uber",
            DeliveryAddress = "10 Barnes Place, Colombo 07",
            Status = "planned",
            CreatedAt = DateTime.UtcNow
        };
        await deliveryRepo.AddAsync(plan);

        var updateDto = new UpdateDeliveryStatusDto
        {
            Status = "delivered",
            TrackingNumber = "TRK-UB-998877"
        };

        var response = await service.UpdateDeliveryStatusAsync(orgId, plan.Id, updateDto);

        Assert.Equal("delivered", response.Status);
        Assert.Equal("TRK-UB-998877", response.TrackingNumber);

        var updatedOrder = await orderRepo.GetByIdAsync(order.Id, orgId);
        Assert.NotNull(updatedOrder);
        Assert.Equal("delivered", updatedOrder.Status);
    }

    [Fact]
    public async Task DeliveriesController_CreateDelivery_ReturnsCreated()
    {
        using var context = CreateInMemoryDbContext();
        var deliveryRepo = new DeliveryRepository(context);
        var orderRepo = new OrderRepository(context);
        var service = new DeliveryService(deliveryRepo, orderRepo);
        var controller = new DeliveriesController(service);

        var orgId = Guid.NewGuid();
        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            CustomerId = Guid.NewGuid(),
            CustomerName = "Test User",
            Status = "payment_confirmed",
            Subtotal = 5000m,
            Total = 5000m,
            CreatedAt = DateTime.UtcNow
        };
        await orderRepo.CreateAsync(order);

        var actionResult = await controller.CreateDelivery(orgId, new CreateDeliveryDto
        {
            OrderId = order.Id,
            DeliveryAddress = "Colombo 05"
        });

        var createdResult = Assert.IsType<CreatedAtActionResult>(actionResult.Result);
        var response = Assert.IsType<DeliveryPlanResponseDto>(createdResult.Value);
        Assert.Equal("planned", response.Status);
    }

    [Fact]
    public async Task DeliveriesController_UpdateStatus_ReturnsOk()
    {
        using var context = CreateInMemoryDbContext();
        var deliveryRepo = new DeliveryRepository(context);
        var orderRepo = new OrderRepository(context);
        var service = new DeliveryService(deliveryRepo, orderRepo);
        var controller = new DeliveriesController(service);

        var orgId = Guid.NewGuid();
        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            CustomerId = Guid.NewGuid(),
            CustomerName = "Test User",
            Status = "delivery_scheduled",
            Subtotal = 5000m,
            Total = 5000m,
            CreatedAt = DateTime.UtcNow
        };
        await orderRepo.CreateAsync(order);

        var plan = new DeliveryPlan
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            OrderId = order.Id,
            DeliveryAddress = "Colombo 05",
            Status = "planned",
            CreatedAt = DateTime.UtcNow
        };
        await deliveryRepo.AddAsync(plan);

        var actionResult = await controller.UpdateStatus(orgId, plan.Id, new UpdateDeliveryStatusDto
        {
            Status = "in_transit"
        });

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var response = Assert.IsType<DeliveryPlanResponseDto>(okResult.Value);
        Assert.Equal("in_transit", response.Status);
    }
}
