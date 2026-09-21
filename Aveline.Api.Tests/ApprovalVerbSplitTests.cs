using System.Security.Claims;
using Aveline.Api.Configurations;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Commerce.Controllers;
using Aveline.Api.Modules.Commerce.DTOs;
using Aveline.Api.Modules.Commerce.Models;
using Aveline.Api.Modules.Commerce.Repositories;
using Aveline.Api.Modules.Commerce.Services;
using Aveline.Api.Modules.Shared.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Tests;

/// <summary>
/// T6 / Q14. Staff hold <c>approvals:approve</c> after Q8, but `reject` **cancels** the order and
/// `revise` **rewrites** its discount, total and margin. The split is therefore by **verb**, not by
/// route: `/decision` carries the verb in its body, so a route-level policy alone would leave the
/// door open. These tests pin the controller's own branch, which is where the split lives.
/// </summary>
public class ApprovalVerbSplitTests
{
    private static AppDbContext CreateInMemoryDbContext()
        => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options);

    [Theory]
    [InlineData("reject", true)]
    [InlineData("REJECT", true)]
    [InlineData(" revise ", true)]
    [InlineData("revise", true)]
    [InlineData("approve", false)]
    [InlineData("APPROVE", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void RequiresOrderManage_ClassifiesTheVerb(string? decision, bool expected)
        => Assert.Equal(expected, ApprovalDecisions.RequiresOrderManage(decision));

    private static ApprovalsController Controller(
        ApprovalService service, UserRepository users, bool allowsOrderManage)
    {
        var controller = new ApprovalsController(
            service,
            users,
            new TestAuthorizationService(policy =>
                policy != AuthorizationConfiguration.BoutiqueOrderManagePolicy || allowsOrderManage));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) },
        };

        return controller;
    }

    [Theory]
    [InlineData("reject")]
    [InlineData("revise")]
    public async Task AStaffApprover_CannotCancelOrRewriteThroughTheDecisionBody(string decision)
    {
        using var context = CreateInMemoryDbContext();
        var service = new ApprovalService(new ApprovalRepository(context), new OrderRepository(context));
        var controller = Controller(service, new UserRepository(context), allowsOrderManage: false);

        var result = await controller.ProcessDecision(
            Guid.NewGuid(), Guid.NewGuid(), new ApprovalDecisionDto { Decision = decision });

        var forbidden = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task AStaffApprover_MayStillApproveThroughTheDecisionBody()
    {
        using var context = CreateInMemoryDbContext();
        var approvalRepo = new ApprovalRepository(context);
        var orderRepo = new OrderRepository(context);
        var service = new ApprovalService(approvalRepo, orderRepo);
        var controller = Controller(service, new UserRepository(context), allowsOrderManage: false);

        var orgId = Guid.NewGuid();
        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            CustomerId = Guid.NewGuid(),
            CustomerName = "Q14",
            Status = "pending_approval",
            Subtotal = 1000m,
            Total = 1000m,
            CreatedAt = DateTime.UtcNow,
        };
        await orderRepo.CreateAsync(order);

        var approval = new ApprovalQueueEntry
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            OrderId = order.Id,
            ApprovalType = "discount",
            Status = "pending",
            CreatedAt = DateTime.UtcNow,
        };
        await approvalRepo.AddAsync(approval);

        var result = await controller.ProcessDecision(
            orgId, approval.Id, new ApprovalDecisionDto { Decision = "approve", Reason = "Within policy" });

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ApprovalQueueResponseDto>(ok.Value);
        Assert.Equal("approved", response.Status);
    }

    [Fact]
    public async Task AManagerWithOrderManage_MayReject()
    {
        using var context = CreateInMemoryDbContext();
        var approvalRepo = new ApprovalRepository(context);
        var orderRepo = new OrderRepository(context);
        var service = new ApprovalService(approvalRepo, orderRepo);
        var controller = Controller(service, new UserRepository(context), allowsOrderManage: true);

        var orgId = Guid.NewGuid();
        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            CustomerId = Guid.NewGuid(),
            CustomerName = "Q14",
            Status = "pending_approval",
            Subtotal = 1000m,
            Total = 1000m,
            CreatedAt = DateTime.UtcNow,
        };
        await orderRepo.CreateAsync(order);

        var approval = new ApprovalQueueEntry
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            OrderId = order.Id,
            ApprovalType = "discount",
            Status = "pending",
            CreatedAt = DateTime.UtcNow,
        };
        await approvalRepo.AddAsync(approval);

        var result = await controller.ProcessDecision(
            orgId, approval.Id, new ApprovalDecisionDto { Decision = "reject", Reason = "Out of policy" });

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ApprovalQueueResponseDto>(ok.Value);
        Assert.Equal("rejected", response.Status);
    }
}
