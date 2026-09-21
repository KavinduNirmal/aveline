using System.Security.Claims;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Commerce.Controllers;
using Aveline.Api.Modules.Commerce.DTOs;
using Aveline.Api.Modules.Commerce.Models;
using Aveline.Api.Modules.Commerce.Repositories;
using Aveline.Api.Modules.Commerce.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Shared.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aveline.Api.Tests;

public class CommercePaymentsTests
{
    private static AppDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task PaymentRepository_AddAndGetById_ReturnsCorrectPayment()
    {
        using var context = CreateInMemoryDbContext();
        var repo = new PaymentRepository(context);
        var orderRepo = new OrderRepository(context);
        var orgId = Guid.NewGuid();
        EnsureOrganization(context, orgId);

        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            CustomerId = Guid.NewGuid(),
            CustomerName = "Amara Dias",
            Status = "pending_hold",
            Subtotal = 15000m,
            Total = 15000m,
            CreatedAt = DateTime.UtcNow
        };
        await orderRepo.CreateAsync(order);

        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            OrderId = order.Id,
            Amount = 15000m,
            PaymentType = "full",
            PaymentMethod = "online",
            Status = "pending",
            PaymentLink = "https://pay.aveline.boutique/checkout/sample1",
            CreatedAt = DateTime.UtcNow
        };
        await repo.AddAsync(payment);

        var retrieved = await repo.GetByIdAsync(payment.Id, orgId);
        Assert.NotNull(retrieved);
        Assert.Equal(payment.Id, retrieved.Id);
        Assert.Equal("pending", retrieved.Status);
        Assert.Equal(15000m, retrieved.Amount);
    }

    [Fact]
    public async Task PaymentRepository_ListAsync_FiltersByOrderIdAndStatus()
    {
        using var context = CreateInMemoryDbContext();
        var repo = new PaymentRepository(context);
        var orderRepo = new OrderRepository(context);
        var orgId = Guid.NewGuid();
        EnsureOrganization(context, orgId);

        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            CustomerId = Guid.NewGuid(),
            CustomerName = "Test Order",
            Status = "pending_hold",
            Subtotal = 5000m,
            Total = 5000m,
            CreatedAt = DateTime.UtcNow
        };
        await orderRepo.CreateAsync(order);

        for (int i = 0; i < 4; i++)
        {
            await repo.AddAsync(new Payment
            {
                Id = Guid.NewGuid(),
                OrganizationId = orgId,
                OrderId = order.Id,
                Amount = 1000m * (i + 1),
                Status = i % 2 == 0 ? "pending" : "confirmed",
                CreatedAt = DateTime.UtcNow.AddMinutes(i)
            });
        }

        var paged = await repo.ListAsync(orgId, new PaymentQueryParametersDto
        {
            OrderId = order.Id,
            Status = "confirmed"
        });

        Assert.Equal(2, paged.TotalCount);
        Assert.All(paged.Items, p => Assert.Equal("confirmed", p.Status));
    }

    [Fact]
    public async Task PaymentService_GeneratePaymentRequest_CreatesPaymentAndUpdatesOrderStatus()
    {
        using var context = CreateInMemoryDbContext();
        var paymentRepo = new PaymentRepository(context);
        var orderRepo = new OrderRepository(context);
        var service = new PaymentService(paymentRepo, orderRepo,
            new BoutiqueSaleLedgerService(context, NullLogger<BoutiqueSaleLedgerService>.Instance));

        var orgId = Guid.NewGuid();
        EnsureOrganization(context, orgId);
        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            CustomerId = Guid.NewGuid(),
            CustomerName = "Nisha Fernando",
            Status = "pending_hold",
            Subtotal = 25000m,
            Total = 25000m,
            CreatedAt = DateTime.UtcNow
        };
        await orderRepo.CreateAsync(order);

        var requestDto = new GeneratePaymentRequestDto
        {
            OrderId = order.Id,
            Amount = 25000m,
            PaymentType = "full",
            PaymentMethod = "online"
        };

        var response = await service.GeneratePaymentRequestAsync(orgId, requestDto);

        Assert.NotNull(response);
        Assert.Equal("pending", response.Status);
        Assert.Equal(25000m, response.Amount);
        Assert.StartsWith("https://pay.aveline.boutique/checkout/", response.PaymentLink!);

        var updatedOrder = await orderRepo.GetByIdAsync(order.Id, orgId);
        Assert.NotNull(updatedOrder);
        Assert.Equal("payment_requested", updatedOrder.Status);
    }

    [Fact]
    public async Task PaymentService_ConfirmPayment_UpdatesPaymentAndOrderStatus()
    {
        using var context = CreateInMemoryDbContext();
        var paymentRepo = new PaymentRepository(context);
        var orderRepo = new OrderRepository(context);
        var service = new PaymentService(paymentRepo, orderRepo,
            new BoutiqueSaleLedgerService(context, NullLogger<BoutiqueSaleLedgerService>.Instance));

        var orgId = Guid.NewGuid();
        EnsureOrganization(context, orgId);
        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            CustomerId = Guid.NewGuid(),
            CustomerName = "Kavindi Peiris",
            Status = "payment_requested",
            Subtotal = 30000m,
            Total = 30000m,
            CreatedAt = DateTime.UtcNow
        };
        await orderRepo.CreateAsync(order);

        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            OrderId = order.Id,
            Amount = 30000m,
            Status = "pending",
            CreatedAt = DateTime.UtcNow
        };
        await paymentRepo.AddAsync(payment);

        var confirmDto = new ConfirmPaymentDto
        {
            GatewayTransactionId = "TXN-PAYHERE-987654",
            PaymentMethod = "card"
        };

        var response = await service.ConfirmPaymentAsync(orgId, payment.Id, confirmDto);

        Assert.Equal("confirmed", response.Status);
        Assert.Equal("TXN-PAYHERE-987654", response.GatewayTransactionId);
        Assert.NotNull(response.ConfirmedAt);

        var updatedOrder = await orderRepo.GetByIdAsync(order.Id, orgId);
        Assert.NotNull(updatedOrder);
        Assert.Equal("payment_confirmed", updatedOrder.Status);
    }

    [Fact]
    public async Task PaymentService_ConfirmPayment_IdempotentWhenAlreadyConfirmed()
    {
        using var context = CreateInMemoryDbContext();
        var paymentRepo = new PaymentRepository(context);
        var orderRepo = new OrderRepository(context);
        var service = new PaymentService(paymentRepo, orderRepo,
            new BoutiqueSaleLedgerService(context, NullLogger<BoutiqueSaleLedgerService>.Instance));

        var orgId = Guid.NewGuid();
        EnsureOrganization(context, orgId);
        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            CustomerId = Guid.NewGuid(),
            CustomerName = "Test Order",
            Status = "payment_confirmed",
            Subtotal = 1000m,
            Total = 1000m,
            CreatedAt = DateTime.UtcNow
        };
        await orderRepo.CreateAsync(order);

        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            OrderId = order.Id,
            Amount = 1000m,
            Status = "confirmed",
            GatewayTransactionId = "TXN-ORIGINAL",
            ConfirmedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        await paymentRepo.AddAsync(payment);

        var confirmDto = new ConfirmPaymentDto { GatewayTransactionId = "TXN-NEW" };
        var result = await service.ConfirmPaymentAsync(orgId, payment.Id, confirmDto);

        Assert.Equal("confirmed", result.Status);
        Assert.Equal("TXN-ORIGINAL", result.GatewayTransactionId);
    }

    [Fact]
    public async Task PaymentService_RefundPayment_SucceedsForConfirmedPayment()
    {
        using var context = CreateInMemoryDbContext();
        var paymentRepo = new PaymentRepository(context);
        var orderRepo = new OrderRepository(context);
        var service = new PaymentService(paymentRepo, orderRepo,
            new BoutiqueSaleLedgerService(context, NullLogger<BoutiqueSaleLedgerService>.Instance));

        var orgId = Guid.NewGuid();
        EnsureOrganization(context, orgId);
        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            CustomerId = Guid.NewGuid(),
            CustomerName = "Test Order",
            Status = "payment_confirmed",
            Subtotal = 2000m,
            Total = 2000m,
            CreatedAt = DateTime.UtcNow
        };
        await orderRepo.CreateAsync(order);

        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            OrderId = order.Id,
            Amount = 2000m,
            Status = "confirmed",
            GatewayTransactionId = "TXN-1234",
            CreatedAt = DateTime.UtcNow
        };
        await paymentRepo.AddAsync(payment);

        var result = await service.RefundPaymentAsync(
            orgId, payment.Id, "Customer requested cancellation",
            ct: default, refundedByUserId: Guid.CreateVersion7());
        Assert.Equal("refunded", result.Status);
    }

    [Fact]
    public async Task PaymentService_RefundPayment_ThrowsIfNotConfirmed()
    {
        using var context = CreateInMemoryDbContext();
        var paymentRepo = new PaymentRepository(context);
        var orderRepo = new OrderRepository(context);
        var service = new PaymentService(paymentRepo, orderRepo,
            new BoutiqueSaleLedgerService(context, NullLogger<BoutiqueSaleLedgerService>.Instance));

        var orgId = Guid.NewGuid();
        EnsureOrganization(context, orgId);
        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            CustomerId = Guid.NewGuid(),
            CustomerName = "Test Order",
            Status = "pending_hold",
            Subtotal = 2000m,
            Total = 2000m,
            CreatedAt = DateTime.UtcNow
        };
        await orderRepo.CreateAsync(order);

        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            OrderId = order.Id,
            Amount = 2000m,
            Status = "pending",
            CreatedAt = DateTime.UtcNow
        };
        await paymentRepo.AddAsync(payment);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RefundPaymentAsync(
                orgId, payment.Id, "Refund test",
                ct: default, refundedByUserId: Guid.CreateVersion7()));
    }

    [Fact]
    public async Task PaymentsController_GeneratePayment_ReturnsCreated()
    {
        using var context = CreateInMemoryDbContext();
        var paymentRepo = new PaymentRepository(context);
        var orderRepo = new OrderRepository(context);
        var service = new PaymentService(paymentRepo, orderRepo,
            new BoutiqueSaleLedgerService(context, NullLogger<BoutiqueSaleLedgerService>.Instance));
        var controller = new PaymentsController(service, new UserRepository(context));

        var orgId = Guid.NewGuid();
        EnsureOrganization(context, orgId);
        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            CustomerId = Guid.NewGuid(),
            CustomerName = "Test Customer",
            Status = "pending_hold",
            Subtotal = 1000m,
            Total = 1000m,
            CreatedAt = DateTime.UtcNow
        };
        await orderRepo.CreateAsync(order);

        var actionResult = await controller.GeneratePayment(orgId, new GeneratePaymentRequestDto
        {
            OrderId = order.Id,
            Amount = 1000m
        });

        var createdResult = Assert.IsType<CreatedAtActionResult>(actionResult.Result);
        var response = Assert.IsType<PaymentResponseDto>(createdResult.Value);
        Assert.Equal("pending", response.Status);
    }

    [Fact]
    public async Task PaymentsController_ConfirmPayment_ReturnsOk()
    {
        using var context = CreateInMemoryDbContext();
        var paymentRepo = new PaymentRepository(context);
        var orderRepo = new OrderRepository(context);
        var service = new PaymentService(paymentRepo, orderRepo,
            new BoutiqueSaleLedgerService(context, NullLogger<BoutiqueSaleLedgerService>.Instance));
        var controller = new PaymentsController(service, new UserRepository(context));

        var orgId = Guid.NewGuid();
        EnsureOrganization(context, orgId);
        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            CustomerId = Guid.NewGuid(),
            CustomerName = "Test Customer",
            Status = "payment_requested",
            Subtotal = 1000m,
            Total = 1000m,
            CreatedAt = DateTime.UtcNow
        };
        await orderRepo.CreateAsync(order);

        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            OrderId = order.Id,
            Amount = 1000m,
            Status = "pending",
            CreatedAt = DateTime.UtcNow
        };
        await paymentRepo.AddAsync(payment);

        var actionResult = await controller.Confirm(orgId, payment.Id, new ConfirmPaymentDto
        {
            GatewayTransactionId = "TXN-CONFIRM"
        });

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var response = Assert.IsType<PaymentResponseDto>(okResult.Value);
        Assert.Equal("confirmed", response.Status);
    }

    /// <summary>
    /// The ledger reads `Organization.Currency` from the organization row, so a fixture that
    /// references an organization must actually create one — a real database could not hold an order
    /// whose organization does not exist.
    /// </summary>
    private static void EnsureOrganization(AppDbContext context, Guid organizationId)
    {
        if (context.Organizations.Any(org => org.Id == organizationId))
        {
            return;
        }

        context.Organizations.Add(new Organization
        {
            Id = organizationId,
            Name = $"Payment Org {organizationId:N}",
            Slug = $"pay-{organizationId:N}",
            OwnerUserId = Guid.CreateVersion7(),
        });
        context.SaveChanges();
    }
}
