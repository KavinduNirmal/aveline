using Aveline.Api.Modules.Commerce.Repositories;
using Aveline.Api.Modules.Commerce.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Aveline.Api.Modules.Commerce;

public static class CommerceModule
{
    public static IServiceCollection AddCommerceModule(this IServiceCollection services)
    {
        // Business Rules
        services.AddScoped<IBusinessRulesRepository, BusinessRulesRepository>();
        services.AddScoped<IBusinessRulesService, BusinessRulesService>();

        // Orders
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IOrderService, OrderService>();

        // Approvals (Feature 4)
        services.AddScoped<IApprovalRepository, ApprovalRepository>();
        services.AddScoped<IApprovalService, ApprovalService>();

        // Payments (Feature 5)
        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<IPaymentService, PaymentService>();

        // Deliveries (Feature 6)
        services.AddScoped<IDeliveryRepository, DeliveryRepository>();
        services.AddScoped<IDeliveryService, DeliveryService>();

        return services;
    }
}
