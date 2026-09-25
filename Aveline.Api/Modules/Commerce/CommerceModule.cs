using Aveline.Api.Modules.Commerce.Repositories;
using Aveline.Api.Modules.Commerce.Services;
using Aveline.Api.Modules.Payments;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Aveline.Api.Modules.Commerce;

public static class CommerceModule
{
    public static IServiceCollection AddCommerceModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        // Business Rules
        services.AddScoped<IBusinessRulesRepository, BusinessRulesRepository>();
        services.AddScoped<IBusinessRulesService, BusinessRulesService>();

        // Orders
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IOrderService, OrderService>();

        // Derives the line items a customer message asks to buy, resolved against the catalog
        // (ADR-024, Decision 1). Reads inventory through the shared service, never writes an order.
        services.AddScoped<IOrderContextBuilder, OrderContextBuilder>();

        // Turns an agent's `pending_approval` verdict into an order the owner can act on
        // (ADR-024, Decision 2). Delegates the write to `IOrderService`, so the agent still owns
        // no persistence.
        services.AddScoped<IConversationOrderBridge, ConversationOrderBridge>();

        // Approvals (Feature 4)
        services.AddScoped<IApprovalRepository, ApprovalRepository>();
        services.AddScoped<IApprovalService, ApprovalService>();

        // Payments (Feature 5). Phase 9's checkout reads `Payments:Commerce:UseProviderIntents`, its
        // one-release rollback switch (plan §8.4 S8). Binding here rather than relying on
        // `AddPaymentsModule` keeps the Commerce module self-sufficient for its own tests and makes the
        // switch's home the place a reader of `PaymentService` looks. `AddOptions` is idempotent, so the
        // sibling registration in `AddPaymentsModule` is unaffected.
        services.AddOptions<PaymentsOptions>()
            .BindConfiguration(PaymentsOptions.SectionName);
        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<IPaymentService, PaymentService>();

        // Deliveries (Feature 6)
        services.AddScoped<IDeliveryRepository, DeliveryRepository>();
        services.AddScoped<IDeliveryService, DeliveryService>();

        // Boutique income ledger (tenant dashboard T3). The shop's own takings, in their own table:
        // a different economy from the platform's `IncomeLedgerEntries`, never read together.
        services.AddScoped<IBoutiqueSaleLedgerService, BoutiqueSaleLedgerService>();
        services.AddScoped<IBoutiqueIncomeReadService, BoutiqueIncomeReadService>();
        services.AddScoped<ITenantDashboardService, TenantDashboardService>();

        // The counter sale of a catalog piece: one call that decrements stock and appends the money.
        // It lives in Commerce because it writes the takings journal; it reads the catalog row
        // through the shared `AppDbContext` rather than through a second visual-intelligence seam.
        services.AddScoped<ICatalogSaleService, CatalogSaleService>();

        // The ledger's repair pass. A payment confirmation that succeeded but whose ledger write did
        // not would otherwise leave the register silently understating the shop's takings.
        services.AddHostedService<Jobs.IncomeLedgerReconciliationJob>();

        return services;
    }
}
