using Aveline.Api.Modules.Commerce.DTOs;
using Aveline.Api.Modules.Commerce.Models;
using Aveline.Api.Modules.Commerce.Repositories;
using Aveline.Api.Modules.Commerce.Services;
using Aveline.Api.Modules.CustomerConcierge.DTOs;
using Aveline.Api.Modules.CustomerConcierge.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Aveline.Api.Tests;

/// <summary>
/// ADR-024, Decision 2. A pause used to have nowhere to land: <c>ApprovalQueueEntry.OrderId</c> is
/// required and references an <c>Order</c>, and only the staff orders API ever created one. This type
/// creates it from the conversation, which makes it a write path on a turn that may be retried - so
/// the idempotency assertions below are the point of the class, not incidental.
/// </summary>
public class ConversationOrderBridgeTests
{
    private static readonly Guid OrgId = Guid.Parse("01a0cb20-96f0-7c72-9823-98f59781c679");

    private static readonly OrderContextItem Saree = new(
        Guid.Parse("b7f1c1a4-0000-4000-8000-000000000001"),
        "Emerald Green Georgette Saree",
        1,
        75000m,
        65000m);

    private sealed record Harness(
        ConversationOrderBridge Bridge,
        Mock<IOrderService> Orders,
        Mock<IApprovalRepository> Approvals,
        Mock<ICustomerService> Customers);

    private static Harness BuildHarness()
    {
        var orders = new Mock<IOrderService>();
        var approvals = new Mock<IApprovalRepository>();
        var customers = new Mock<ICustomerService>();

        approvals
            .Setup(repo => repo.GetPendingByThreadIdAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ApprovalQueueEntry?)null);

        orders
            .Setup(service => service.CreateOrderAsync(
                It.IsAny<Guid>(), It.IsAny<CreateOrderDto>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid org, CreateOrderDto dto, Guid? _, CancellationToken _) => new OrderResponseDto
            {
                Id = Guid.CreateVersion7(),
                OrganizationId = org,
                CustomerId = dto.CustomerId,
                CustomerName = dto.CustomerName,
                Status = "pending_approval",
                Items = dto.Items
            });

        customers
            .Setup(service => service.IdentifyOrCreateAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid org, string phone, string? name, CancellationToken _) => new CustomerProfileDto(
                Guid.CreateVersion7(), phone, null, name, "new", 0m, 0,
                Array.Empty<CustomerPreferenceDto>(), Array.Empty<string>(), "pending"));

        return new Harness(
            new ConversationOrderBridge(
                orders.Object, approvals.Object, customers.Object, NullLogger<ConversationOrderBridge>.Instance),
            orders,
            approvals,
            customers);
    }

    private static Task<PausedOrderOutcome?> CreateAsync(
        Harness harness,
        string? threadId = "thread-1",
        Guid? conversationId = null,
        Guid? customerId = null,
        string? phoneNumber = "+94763475058",
        string? customerName = "Kasha Vivian Perera",
        IReadOnlyList<OrderContextItem>? items = null)
        => harness.Bridge.CreateForPausedRunAsync(
            OrgId,
            threadId,
            conversationId,
            customerId,
            phoneNumber,
            customerName,
            items ?? new[] { Saree });

    [Fact]
    public async Task APausedRun_CreatesAnOrderWithTheItemsTheConversationDerived()
    {
        var harness = BuildHarness();
        var customerId = Guid.CreateVersion7();

        var outcome = await CreateAsync(harness, customerId: customerId, conversationId: Guid.CreateVersion7());

        Assert.NotNull(outcome);
        Assert.True(outcome!.Created);
        Assert.True(outcome.RequiresApproval);
        Assert.Equal(customerId, outcome.CustomerId);

        harness.Orders.Verify(service => service.CreateOrderAsync(
            OrgId,
            It.Is<CreateOrderDto>(dto =>
                dto.Items.Count == 1
                && dto.Items[0].ItemId == Saree.ItemId
                && dto.Items[0].UnitPrice == 75000m
                && dto.Items[0].WholesaleCost == 65000m
                && dto.ThreadId == "thread-1"
                && dto.CustomerId == customerId),
            null,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ARetriedRun_ReusesTheOrderItAlreadyDrafted()
    {
        // The failure this prevents: a redelivered webhook drafting a second order for one pause,
        // which would double every figure derived from it.
        var harness = BuildHarness();
        var existingOrderId = Guid.CreateVersion7();
        var existingCustomerId = Guid.CreateVersion7();

        harness.Approvals
            .Setup(repo => repo.GetPendingByThreadIdAsync(OrgId, "thread-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApprovalQueueEntry
            {
                Id = Guid.CreateVersion7(),
                OrganizationId = OrgId,
                OrderId = existingOrderId,
                ApprovalType = "high_value_order",
                Status = "pending",
                ThreadId = "thread-1",
                Order = new Order
                {
                    Id = existingOrderId,
                    OrganizationId = OrgId,
                    CustomerId = existingCustomerId,
                    CustomerName = "Existing",
                }
            });

        var outcome = await CreateAsync(harness, customerId: Guid.CreateVersion7());

        Assert.NotNull(outcome);
        Assert.False(outcome!.Created);
        Assert.Equal(existingOrderId, outcome.OrderId);
        Assert.Equal(existingCustomerId, outcome.CustomerId);

        harness.Orders.Verify(service => service.CreateOrderAsync(
            It.IsAny<Guid>(), It.IsAny<CreateOrderDto>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task AFirstContactOrder_ResolvesTheCustomerFromTheSendersNumber()
    {
        var harness = BuildHarness();

        var outcome = await CreateAsync(harness, customerId: null, phoneNumber: "+94763475058");

        Assert.NotNull(outcome);
        Assert.NotEqual(Guid.Empty, outcome!.CustomerId);
        harness.Customers.Verify(service => service.IdentifyOrCreateAsync(
            OrgId, "+94763475058", "Kasha Vivian Perera", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task APauseWithNoThread_CreatesNothing()
    {
        // Invariant A6: without a thread there is no checkpoint to resume and no way to link a
        // decision back to the run, so the pause stays telemetry.
        var harness = BuildHarness();

        var outcome = await CreateAsync(harness, threadId: null, customerId: Guid.CreateVersion7());

        Assert.Null(outcome);
        harness.Orders.Verify(service => service.CreateOrderAsync(
            It.IsAny<Guid>(), It.IsAny<CreateOrderDto>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task APauseWithNoLineItems_CreatesNothing()
    {
        var harness = BuildHarness();

        var outcome = await CreateAsync(
            harness, customerId: Guid.CreateVersion7(), items: Array.Empty<OrderContextItem>());

        Assert.Null(outcome);
        harness.Orders.Verify(service => service.CreateOrderAsync(
            It.IsAny<Guid>(), It.IsAny<CreateOrderDto>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task APauseWithNoCustomer_CreatesNothing()
    {
        // An order must reference a real customer. Inventing a nameless placeholder is exactly the
        // duplicate-customer defect that was already fixed once.
        var harness = BuildHarness();

        var outcome = await CreateAsync(harness, customerId: null, phoneNumber: null);

        Assert.Null(outcome);
        harness.Orders.Verify(service => service.CreateOrderAsync(
            It.IsAny<Guid>(), It.IsAny<CreateOrderDto>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task AnOrderTheRulesDoNotPause_IsReportedAsNotRequiringApproval()
    {
        // The rules are the API's, and they can disagree with the agent if the rules changed between
        // the pause and this write. That has to be visible rather than silently creating an approval
        // row for an order that is already through the gate.
        var harness = BuildHarness();
        harness.Orders
            .Setup(service => service.CreateOrderAsync(
                It.IsAny<Guid>(), It.IsAny<CreateOrderDto>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrderResponseDto
            {
                Id = Guid.CreateVersion7(),
                OrganizationId = OrgId,
                CustomerId = Guid.CreateVersion7(),
                Status = "payment_requested"
            });

        var outcome = await CreateAsync(harness, customerId: Guid.CreateVersion7());

        Assert.NotNull(outcome);
        Assert.False(outcome!.RequiresApproval);
    }
}
