using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Commerce.Models;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Payments.Domain;
using Aveline.Api.Modules.Payments.Models;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Aveline.Api.Tests;

/// <summary>
/// The PostgreSQL-backed invariant Phase 9 adds to the Commerce payment table (plan §9.7): the
/// index on <c>Payments.GatewayTransactionId</c> is unique, so a provider reference cannot name two
/// charges. The project review recorded the opposite as a defect —
/// <c>docs/reports/PR-290-slice3-review.md:269</c>: "<c>GatewayTransactionId</c> has a
/// **non-unique** index (<c>PaymentConfiguration.cs</c>), so the spec's *idempotent by transaction
/// id* is enforced only as 'already-confirmed returns early'".
/// </summary>
/// <remarks>
/// The in-memory provider cannot enforce a unique index, so this is asserted by exercising the
/// violation against a real migrated schema rather than by reading the EF model. The migration is
/// applied with <c>MigrateAsync</c>, which is also what proves the migration itself runs.
/// </remarks>
public class CommercePaymentSchemaPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("pgvector/pgvector:pg16")
        .WithDatabase("aveline_commerce_test")
        .WithUsername("aveline")
        .WithPassword("aveline_test_pw")
        .Build();

    private DbContextOptions<AppDbContext> _options = null!;
    private Guid _orgId;
    private Guid _orderId;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(
                _postgres.GetConnectionString(),
                npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .Options;

        await using var context = new AppDbContext(_options);
        await context.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS vector");
        await context.Database.MigrateAsync();

        var ownerId = Guid.CreateVersion7();
        context.Users.Add(new User
        {
            Id = ownerId, ClerkId = $"commerce_{ownerId:N}", Email = "commerce@aveline.lk",
            FirstName = "Commerce", LastName = "Owner", Username = $"commerce_{ownerId:N}",
            UserRole = "owner", OrganizationRole = "org:boutique_owner",
        });
        var org = new Organization
        {
            Name = "Commerce Pg Boutique", Slug = $"commerce-pg-{ownerId:N}", OwnerUserId = ownerId,
        };
        context.Organizations.Add(org);

        var order = new Order
        {
            OrganizationId = org.Id,
            CustomerId = Guid.CreateVersion7(),
            CustomerName = "Commerce Pg Customer",
            Status = "payment_requested",
            Subtotal = 1000m,
            Total = 1000m,
            CreatedAt = DateTime.UtcNow,
        };
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        _orgId = org.Id;
        _orderId = order.Id;
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private Payment NewPayment(string? gatewayTransactionId) => new()
    {
        OrganizationId = _orgId,
        OrderId = _orderId,
        Amount = 1000m,
        PaymentType = "full",
        PaymentMethod = "online",
        Status = "pending",
        GatewayTransactionId = gatewayTransactionId,
        CreatedAt = DateTime.UtcNow,
    };

    [Fact]
    public async Task AGatewayTransactionId_CannotNameTwoPayments()
    {
        await using var context = new AppDbContext(_options);
        context.Payments.Add(NewPayment("TXN-UNIQUE-1"));
        await context.SaveChangesAsync();

        await using var second = new AppDbContext(_options);
        second.Payments.Add(NewPayment("TXN-UNIQUE-1"));

        // The unique index, not the service, is the enforcement point: two concurrent confirmations
        // race in different processes, and only the database sees both.
        await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task ManyPayments_WithNoGatewayReference_AreStillAllowed()
    {
        await using var context = new AppDbContext(_options);
        context.Payments.Add(NewPayment(null));
        context.Payments.Add(NewPayment(null));
        context.Payments.Add(NewPayment(null));

        // A counter payment has no provider reference. A unique index that collapsed nulls into one
        // value would make the honest path the failing one, so this pins the other half of the rule.
        await context.SaveChangesAsync();

        Assert.Equal(3, await context.Payments.CountAsync(p => p.OrganizationId == _orgId));
    }

    [Fact]
    public async Task APaymentIntentLink_IsStoredAndScopedToItsTenant()
    {
        await using var context = new AppDbContext(_options);
        var intentId = Guid.CreateVersion7();
        context.PaymentIntents.Add(new PaymentIntent
        {
            Id = intentId,
            OrganizationId = _orgId,
            Provider = "manual",
            Purpose = PaymentPurpose.CommerceOrder,
            Status = PaymentProviderStatus.RequiresAction,
            AmountMinor = 100000,
            Currency = "LKR",
            PriceLkr = 1000m,
            Description = "Commerce order checkout.",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });

        var payment = NewPayment(null);
        payment.PaymentIntentId = intentId;
        context.Payments.Add(payment);
        await context.SaveChangesAsync();

        // The migration added the column and its foreign key; the link is readable back.
        var stored = await context.Payments.AsNoTracking().SingleAsync(p => p.Id == payment.Id);
        Assert.Equal(intentId, stored.PaymentIntentId);
        Assert.Equal(_orgId, stored.OrganizationId);
    }
}
