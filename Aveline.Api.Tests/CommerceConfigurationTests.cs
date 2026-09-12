using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Commerce.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Aveline.Api.Tests;

public class CommerceConfigurationTests
{
    private readonly AppDbContext _context;

    public CommerceConfigurationTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"CommerceConfigTest_{Guid.NewGuid()}")
            .Options;

        _context = new AppDbContext(options);
    }

    [Fact]
    public void Order_ModelConfiguration_ConfiguresCorrectTableAndProperties()
    {
        var entityType = _context.Model.FindEntityType(typeof(Order));
        Assert.NotNull(entityType);
        Assert.Equal("Orders", entityType.GetTableName());

        var orgIdProp = entityType.FindProperty(nameof(Order.OrganizationId));
        Assert.NotNull(orgIdProp);
        Assert.False(orgIdProp.IsNullable);

        var subtotalProp = entityType.FindProperty(nameof(Order.Subtotal));
        Assert.NotNull(subtotalProp);
        Assert.Equal(18, subtotalProp.GetPrecision());
        Assert.Equal(2, subtotalProp.GetScale());

        var marginProp = entityType.FindProperty(nameof(Order.Margin));
        Assert.NotNull(marginProp);
        Assert.Equal(5, marginProp.GetPrecision());
        Assert.Equal(4, marginProp.GetScale());
    }

    [Fact]
    public void OrderItem_ModelConfiguration_ConfiguresCorrectTableAndProperties()
    {
        var entityType = _context.Model.FindEntityType(typeof(OrderItem));
        Assert.NotNull(entityType);
        Assert.Equal("Order_Items", entityType.GetTableName());

        var unitPriceProp = entityType.FindProperty(nameof(OrderItem.UnitPrice));
        Assert.NotNull(unitPriceProp);
        Assert.Equal(18, unitPriceProp.GetPrecision());
        Assert.Equal(2, unitPriceProp.GetScale());

        var wholesaleCostProp = entityType.FindProperty(nameof(OrderItem.WholesaleCost));
        Assert.NotNull(wholesaleCostProp);
        Assert.Equal(18, wholesaleCostProp.GetPrecision());
        Assert.Equal(2, wholesaleCostProp.GetScale());
    }

    [Fact]
    public void Payment_ModelConfiguration_ConfiguresCorrectTableAndProperties()
    {
        var entityType = _context.Model.FindEntityType(typeof(Payment));
        Assert.NotNull(entityType);
        Assert.Equal("Payments", entityType.GetTableName());

        var amountProp = entityType.FindProperty(nameof(Payment.Amount));
        Assert.NotNull(amountProp);
        Assert.Equal(18, amountProp.GetPrecision());
        Assert.Equal(2, amountProp.GetScale());
    }

    [Fact]
    public void ApprovalQueueEntry_ModelConfiguration_ConfiguresCorrectTableAndProperties()
    {
        var entityType = _context.Model.FindEntityType(typeof(ApprovalQueueEntry));
        Assert.NotNull(entityType);
        Assert.Equal("Approval_Queue", entityType.GetTableName());

        var statusProp = entityType.FindProperty(nameof(ApprovalQueueEntry.Status));
        Assert.NotNull(statusProp);
        Assert.Equal(50, statusProp.GetMaxLength());

        var reasonProp = entityType.FindProperty(nameof(ApprovalQueueEntry.Reason));
        Assert.NotNull(reasonProp);
        Assert.Equal(500, reasonProp.GetMaxLength());
    }

    [Fact]
    public void DeliveryPlan_ModelConfiguration_ConfiguresCorrectTableAndProperties()
    {
        var entityType = _context.Model.FindEntityType(typeof(DeliveryPlan));
        Assert.NotNull(entityType);
        Assert.Equal("Delivery_Plans", entityType.GetTableName());

        var costProp = entityType.FindProperty(nameof(DeliveryPlan.EstimatedCost));
        Assert.NotNull(costProp);
        Assert.Equal(18, costProp.GetPrecision());
        Assert.Equal(2, costProp.GetScale());
    }

    [Fact]
    public void BusinessRule_ModelConfiguration_ConfiguresCorrectTableAndProperties()
    {
        var entityType = _context.Model.FindEntityType(typeof(BusinessRule));
        Assert.NotNull(entityType);
        Assert.Equal("Business_Rules", entityType.GetTableName());

        var nameProp = entityType.FindProperty(nameof(BusinessRule.RuleName));
        Assert.NotNull(nameProp);
        Assert.Equal(100, nameProp.GetMaxLength());
    }
}
