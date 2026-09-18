using System.Security.Claims;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Commerce.Controllers;
using Aveline.Api.Modules.Commerce.DTOs;
using Aveline.Api.Modules.Commerce.Models;
using Aveline.Api.Modules.Commerce.Repositories;
using Aveline.Api.Modules.Commerce.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Aveline.Api.Tests;

public class CommerceApprovalsTests
{
    private static AppDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task ApprovalRepository_AddAndGetById_ReturnsCorrectEntry()
    {
        using var context = CreateInMemoryDbContext();
        var repo = new ApprovalRepository(context);
        var orderRepo = new OrderRepository(context);
        var orgId = Guid.NewGuid();

        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            CustomerId = Guid.NewGuid(),
            CustomerName = "Sarah Perera",
            Status = "pending_approval",
            Subtotal = 50000m,
            Total = 50000m,
            CreatedAt = DateTime.UtcNow
        };
        await orderRepo.CreateAsync(order);

        var entry = new ApprovalQueueEntry
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            OrderId = order.Id,
            ApprovalType = "discount",
            Status = "pending",
            Reason = "High discount requested",
            ThresholdExceeded = true,
            CreatedAt = DateTime.UtcNow
        };

        await repo.AddAsync(entry);

        var retrieved = await repo.GetByIdAsync(entry.Id, orgId);
        Assert.NotNull(retrieved);
        Assert.Equal(entry.Id, retrieved.Id);
        Assert.Equal("pending", retrieved.Status);
        Assert.Equal(orgId, retrieved.OrganizationId);
    }

    [Fact]
    public async Task ApprovalRepository_ListAsync_FiltersByStatusAndPaginates()
    {
        using var context = CreateInMemoryDbContext();
        var repo = new ApprovalRepository(context);
        var orderRepo = new OrderRepository(context);
        var orgId = Guid.NewGuid();

        for (int i = 0; i < 5; i++)
        {
            var order = new Order
            {
                Id = Guid.NewGuid(),
                OrganizationId = orgId,
                CustomerId = Guid.NewGuid(),
                CustomerName = $"Customer {i}",
                Status = "pending_approval",
                Subtotal = 10000m,
                Total = 10000m,
                CreatedAt = DateTime.UtcNow.AddMinutes(i)
            };
            await orderRepo.CreateAsync(order);

            await repo.AddAsync(new ApprovalQueueEntry
            {
                Id = Guid.NewGuid(),
                OrganizationId = orgId,
                OrderId = order.Id,
                ApprovalType = "discount",
                Status = i < 3 ? "pending" : "approved",
                Reason = $"Reason {i}",
                CreatedAt = DateTime.UtcNow.AddMinutes(i)
            });
        }

        var pendingList = await repo.ListAsync(orgId, "pending", 1, 10);
        Assert.Equal(3, pendingList.TotalCount);
        Assert.Equal(3, pendingList.Items.Count);

        var count = await repo.GetPendingCountAsync(orgId);
        Assert.Equal(3, count);
    }

    [Fact]
    public async Task ApprovalService_ProcessDecision_Approve_TransitionsOrderToConfirmed()
    {
        using var context = CreateInMemoryDbContext();
        var approvalRepo = new ApprovalRepository(context);
        var orderRepo = new OrderRepository(context);
        var service = new ApprovalService(approvalRepo, orderRepo);

        var orgId = Guid.NewGuid();
        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            CustomerId = Guid.NewGuid(),
            CustomerName = "Sarah Perera",
            Status = "pending_approval",
            Subtotal = 50000m,
            Discount = 5000m,
            Total = 45000m,
            TotalCost = 30000m,
            Margin = 0.3333m,
            CreatedAt = DateTime.UtcNow
        };
        await orderRepo.CreateAsync(order);

        var approval = new ApprovalQueueEntry
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            OrderId = order.Id,
            ApprovalType = "high_value_order",
            Status = "pending",
            Reason = "Exceeds LKR 40,000 threshold",
            CreatedAt = DateTime.UtcNow
        };
        await approvalRepo.AddAsync(approval);

        var decisionDto = new ApprovalDecisionDto
        {
            Decision = "approve",
            Reason = "Approved by boutique owner."
        };
        var managerId = Guid.NewGuid();

        var result = await service.ProcessDecisionAsync(approval.Id, orgId, decisionDto, managerId);

        Assert.Equal("approved", result.Status);
        Assert.Equal(managerId, result.DecidedBy);
        Assert.NotNull(result.DecidedAt);

        var updatedOrder = await orderRepo.GetByIdAsync(order.Id, orgId);
        Assert.NotNull(updatedOrder);
        Assert.Equal("confirmed", updatedOrder.Status);
    }

    [Fact]
    public async Task ApprovalService_ProcessDecision_Reject_TransitionsOrderToCancelled()
    {
        using var context = CreateInMemoryDbContext();
        var approvalRepo = new ApprovalRepository(context);
        var orderRepo = new OrderRepository(context);
        var service = new ApprovalService(approvalRepo, orderRepo);

        var orgId = Guid.NewGuid();
        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            CustomerId = Guid.NewGuid(),
            CustomerName = "Jane Doe",
            Status = "pending_approval",
            Subtotal = 20000m,
            Discount = 8000m,
            Total = 12000m,
            TotalCost = 15000m,
            Margin = -0.2500m,
            CreatedAt = DateTime.UtcNow
        };
        await orderRepo.CreateAsync(order);

        var approval = new ApprovalQueueEntry
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            OrderId = order.Id,
            ApprovalType = "low_margin",
            Status = "pending",
            Reason = "Negative profit margin",
            CreatedAt = DateTime.UtcNow
        };
        await approvalRepo.AddAsync(approval);

        var decisionDto = new ApprovalDecisionDto
        {
            Decision = "reject",
            Reason = "Margin too low to accept."
        };

        var result = await service.ProcessDecisionAsync(approval.Id, orgId, decisionDto, Guid.NewGuid());

        Assert.Equal("rejected", result.Status);

        var updatedOrder = await orderRepo.GetByIdAsync(order.Id, orgId);
        Assert.NotNull(updatedOrder);
        Assert.Equal("cancelled", updatedOrder.Status);
    }

    [Fact]
    public async Task ApprovalService_ProcessDecision_Revise_UpdatesDiscountAndRecalculatesMargin()
    {
        using var context = CreateInMemoryDbContext();
        var approvalRepo = new ApprovalRepository(context);
        var orderRepo = new OrderRepository(context);
        var service = new ApprovalService(approvalRepo, orderRepo);

        var orgId = Guid.NewGuid();
        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            CustomerId = Guid.NewGuid(),
            CustomerName = "Elena Silva",
            Status = "pending_approval",
            Subtotal = 40000m,
            Discount = 10000m,
            Total = 30000m,
            TotalCost = 24000m,
            Margin = 0.2000m,
            CreatedAt = DateTime.UtcNow
        };
        await orderRepo.CreateAsync(order);

        var approval = new ApprovalQueueEntry
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            OrderId = order.Id,
            ApprovalType = "discount",
            Status = "pending",
            Reason = "Discount 25% exceeds limit",
            CreatedAt = DateTime.UtcNow
        };
        await approvalRepo.AddAsync(approval);

        var decisionDto = new ApprovalDecisionDto
        {
            Decision = "revise",
            Reason = "Counter-offer with 10% discount",
            RevisedDiscount = 4000m // 10% of 40,000
        };

        var result = await service.ProcessDecisionAsync(approval.Id, orgId, decisionDto, Guid.NewGuid());

        Assert.Equal("revised", result.Status);

        var updatedOrder = await orderRepo.GetByIdAsync(order.Id, orgId);
        Assert.NotNull(updatedOrder);
        Assert.Equal("revised", updatedOrder.Status);
        Assert.Equal(4000m, updatedOrder.Discount);
        Assert.Equal(36000m, updatedOrder.Total);
        // Margin: (36000 - 24000) / 36000 = 12000 / 36000 = 0.3333
        Assert.Equal(0.3333m, Math.Round(updatedOrder.Margin, 4));
    }

    [Fact]
    public async Task ApprovalService_ProcessDecision_ThrowsIfAlreadyProcessed()
    {
        using var context = CreateInMemoryDbContext();
        var approvalRepo = new ApprovalRepository(context);
        var orderRepo = new OrderRepository(context);
        var service = new ApprovalService(approvalRepo, orderRepo);

        var orgId = Guid.NewGuid();
        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            CustomerId = Guid.NewGuid(),
            CustomerName = "Test Order",
            Status = "approved",
            Subtotal = 1000m,
            Total = 1000m,
            CreatedAt = DateTime.UtcNow
        };
        await orderRepo.CreateAsync(order);

        var approval = new ApprovalQueueEntry
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            OrderId = order.Id,
            ApprovalType = "discount",
            Status = "approved",
            CreatedAt = DateTime.UtcNow
        };
        await approvalRepo.AddAsync(approval);

        var decisionDto = new ApprovalDecisionDto { Decision = "approve" };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ProcessDecisionAsync(approval.Id, orgId, decisionDto, null));
    }

    [Fact]
    public async Task ApprovalsController_GetAll_ReturnsOkWithPagedResult()
    {
        using var context = CreateInMemoryDbContext();
        var approvalRepo = new ApprovalRepository(context);
        var orderRepo = new OrderRepository(context);
        var service = new ApprovalService(approvalRepo, orderRepo);
        var controller = new ApprovalsController(service);

        var orgId = Guid.NewGuid();
        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            CustomerId = Guid.NewGuid(),
            CustomerName = "Test Order",
            Status = "pending_approval",
            Subtotal = 1000m,
            Total = 1000m,
            CreatedAt = DateTime.UtcNow
        };
        await orderRepo.CreateAsync(order);

        await approvalRepo.AddAsync(new ApprovalQueueEntry
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            OrderId = order.Id,
            ApprovalType = "discount",
            Status = "pending",
            CreatedAt = DateTime.UtcNow
        });

        var actionResult = await controller.GetAll(orgId, new ApprovalQueryParametersDto());
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var pagedResult = Assert.IsType<PagedResult<ApprovalQueueResponseDto>>(okResult.Value);
        Assert.Single(pagedResult.Items);
    }

    [Fact]
    public async Task ApprovalsController_ProcessDecision_ReturnsOkWithUpdatedEntry()
    {
        using var context = CreateInMemoryDbContext();
        var approvalRepo = new ApprovalRepository(context);
        var orderRepo = new OrderRepository(context);
        var service = new ApprovalService(approvalRepo, orderRepo);
        var controller = new ApprovalsController(service);

        var userId = Guid.NewGuid();
        var claims = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString())
        }, "TestAuth"));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = claims }
        };

        var orgId = Guid.NewGuid();
        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            CustomerId = Guid.NewGuid(),
            CustomerName = "Test User",
            Status = "pending_approval",
            Subtotal = 1000m,
            Total = 1000m,
            CreatedAt = DateTime.UtcNow
        };
        await orderRepo.CreateAsync(order);

        var approval = new ApprovalQueueEntry
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            OrderId = order.Id,
            ApprovalType = "discount",
            Status = "pending",
            CreatedAt = DateTime.UtcNow
        };
        await approvalRepo.AddAsync(approval);

        var actionResult = await controller.ProcessDecision(
            orgId,
            approval.Id,
            new ApprovalDecisionDto { Decision = "approve", Reason = "All good" });

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var response = Assert.IsType<ApprovalQueueResponseDto>(okResult.Value);
        Assert.Equal("approved", response.Status);
        Assert.Equal(userId, response.DecidedBy);
    }
}
