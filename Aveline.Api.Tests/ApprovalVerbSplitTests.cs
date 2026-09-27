using System.ComponentModel.DataAnnotations;
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

    /// <summary>
    /// The exact bodies the dashboard posts to the verb routes must pass model validation.
    /// </summary>
    /// <remarks>
    /// This is the defect the dashboard hit: the client posts the verb in the URL
    /// (<c>/approvals/{id}/approve</c>) and a body of <c>{reason, revisedDiscount}</c>, the DTO
    /// declared <c>Decision</c> with <c>[Required]</c>, and <c>[ApiController]</c>'s automatic model
    /// validation therefore rejected every decision with
    /// <c>400 "The Decision field is required."</c> before the action could set the verb itself.
    /// Every other test here calls the action directly, which is exactly why none of them caught it:
    /// it is a binding failure, not a logic failure.
    ///
    /// <c>Validator.TryValidateObject</c> over the same attributes is what the MVC model binder's
    /// validation visitor runs, so this fails on the attribute that caused the 400 and passes once
    /// the requirement moves to the service. The payloads are the ones <c>approvals-api.ts</c> sends.
    /// </remarks>
    [Theory]
    // approve / reject with a reason and nothing else
    [InlineData(null, null)]
    [InlineData("Within policy", null)]
    // revise, whose discount is the one real field the body carries
    [InlineData(null, "250")]
    [InlineData("Adjusted to policy", "250")]
    public void AVebRoutePayload_WithNoDecisionInTheBody_PassesModelValidation(
        string? reason, string? revisedDiscount)
    {
        // The discount arrives as a string because xUnit cannot type an `InlineData` literal as a
        // decimal; parsing it here keeps the payload shape honest without a second theory.
        var dto = new ApprovalDecisionDto
        {
            Reason = reason,
            RevisedDiscount = revisedDiscount is null
                ? null
                : decimal.Parse(revisedDiscount, System.Globalization.CultureInfo.InvariantCulture),
        };

        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(dto, new ValidationContext(dto), results, validateAllProperties: true);

        Assert.True(isValid, string.Join("; ", results.Select(r => r.ErrorMessage)));
    }

    [Fact]
    public void TheDecision_IsNotARequiredBodyField()
    {
        // The narrow guard against re-adding the attribute that broke every verb route, stated as a
        // property of the type rather than of one payload, so it fails even if the `Validator` call
        // above is ever loosened.
        var decision = typeof(ApprovalDecisionDto).GetProperty(nameof(ApprovalDecisionDto.Decision))!;

        Assert.Empty(decision.GetCustomAttributes(typeof(RequiredAttribute), inherit: true));
    }

    [Fact]
    public async Task TheGenericDecisionBody_StillRequiresADecisionAtTheService()
    {
        // The other half of the contract: removing the attribute must not let `/decision` through
        // with no verb at all. The service is the one place the body is the source of the decision.
        using var context = CreateInMemoryDbContext();
        var service = new ApprovalService(new ApprovalRepository(context), new OrderRepository(context));

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => service.ProcessDecisionAsync(
            Guid.NewGuid(), Guid.NewGuid(), new ApprovalDecisionDto(), decidedBy: null));

        Assert.Contains("Decision is required", exception.Message);
    }

    [Fact]
    public async Task TheGenericDecisionBody_RejectsAnUnknownVerbAtTheService()
    {
        // With the shape rule gone from the DTO, an unrecognised verb has to be refused by the same
        // guard that used to be redundant with it - otherwise "the attribute is not needed" would
        // have quietly become "no decision is validated". A real pending entry is used so the verb
        // check is what refuses it, rather than a not-found lookup short-circuiting first.
        using var context = CreateInMemoryDbContext();
        var (_, approval) = await SeededControllerAsync(context, allowsOrderManage: true);
        var service = new ApprovalService(new ApprovalRepository(context), new OrderRepository(context));

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => service.ProcessDecisionAsync(
            approval.Id, approval.OrganizationId, new ApprovalDecisionDto { Decision = "accept" }, decidedBy: null));

        Assert.Contains("Invalid decision 'accept'", exception.Message);
    }

    [Fact]
    public async Task TheApproveRoute_SuppliesTheVerbTheClientOmits()
    {
        using var context = CreateInMemoryDbContext();
        var (controller, approval) = await SeededControllerAsync(context, allowsOrderManage: false);

        // No `Decision` in the body, exactly as `approvals-api.ts` posts it.
        var result = await controller.Approve(approval.OrganizationId, approval.Id);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ApprovalQueueResponseDto>(ok.Value);
        Assert.Equal("approved", response.Status);
    }

    [Fact]
    public async Task TheApproveRoute_IgnoresAVerbTheBodyTriesToSmuggleIn()
    {
        // The verb routes own the verb. A body that says `reject` must still approve: otherwise the
        // permission split Q14 exists for could be bypassed by posting to the approve route.
        using var context = CreateInMemoryDbContext();
        var (controller, approval) = await SeededControllerAsync(context, allowsOrderManage: false);

        var result = await controller.Approve(
            approval.OrganizationId, approval.Id, new ApprovalDecisionDto { Decision = "reject" });

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ApprovalQueueResponseDto>(ok.Value);
        Assert.Equal("approved", response.Status);
    }

    [Fact]
    public async Task TheRejectRoute_SuppliesTheVerbTheClientOmits()
    {
        using var context = CreateInMemoryDbContext();
        var (controller, approval) = await SeededControllerAsync(context, allowsOrderManage: true);

        var result = await controller.Reject(approval.OrganizationId, approval.Id);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ApprovalQueueResponseDto>(ok.Value);
        Assert.Equal("rejected", response.Status);
    }

    [Fact]
    public async Task TheReviseRoute_SuppliesTheVerbAndKeepsTheDiscountFromTheBody()
    {
        using var context = CreateInMemoryDbContext();
        var (controller, approval) = await SeededControllerAsync(context, allowsOrderManage: true);

        // Revise is the one verb route whose body carries a real field (the discount); the decision
        // still comes from the route, so a client cannot revise by posting a different verb.
        var result = await controller.Revise(
            approval.OrganizationId, approval.Id, new ApprovalDecisionDto { RevisedDiscount = 100m });

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ApprovalQueueResponseDto>(ok.Value);
        Assert.Equal("revised", response.Status);
    }

    /// <summary>A controller over an in-memory store with one pending approval on one order.</summary>
    private static async Task<(ApprovalsController Controller, ApprovalQueueEntry Approval)> SeededControllerAsync(
        AppDbContext context, bool allowsOrderManage)
    {
        var orderRepo = new OrderRepository(context);
        var approvalRepo = new ApprovalRepository(context);
        var service = new ApprovalService(approvalRepo, orderRepo);
        var controller = Controller(service, new UserRepository(context), allowsOrderManage);

        var orgId = Guid.NewGuid();
        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            CustomerId = Guid.NewGuid(),
            CustomerName = "Verb route",
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

        return (controller, approval);
    }

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
