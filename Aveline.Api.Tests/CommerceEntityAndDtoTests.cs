using Aveline.Api.Modules.Commerce.DTOs;
using Aveline.Api.Modules.Commerce.Models;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Shared.Models;
using Xunit;

namespace Aveline.Api.Tests;

public class CommerceEntityAndDtoTests
{
    [Fact]
    public void Order_Entity_PropertiesAndDefaults_SetAndGetCorrectly()
    {
        var id = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var createdBy = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var order = new Order
        {
            Id = id,
            OrganizationId = orgId,
            CustomerId = customerId,
            CustomerName = "Jane Doe",
            OrderType = "in_store",
            Status = "pending_hold",
            Subtotal = 10000.50m,
            Discount = 500.00m,
            Total = 9500.50m,
            TotalCost = 6000.00m,
            Margin = 0.3684m,
            CreatedBy = createdBy,
            CreatedAt = now,
            UpdatedAt = now.AddMinutes(5),
            Organization = new Organization { Id = orgId, Name = "Boutique Colombo" },
            CreatedByUser = new User { Id = createdBy, Email = "staff@boutique.com" }
        };

        Assert.Equal(id, order.Id);
        Assert.Equal(orgId, order.OrganizationId);
        Assert.Equal(customerId, order.CustomerId);
        Assert.Equal("Jane Doe", order.CustomerName);
        Assert.Equal("in_store", order.OrderType);
        Assert.Equal("pending_hold", order.Status);
        Assert.Equal(10000.50m, order.Subtotal);
        Assert.Equal(500.00m, order.Discount);
        Assert.Equal(9500.50m, order.Total);
        Assert.Equal(6000.00m, order.TotalCost);
        Assert.Equal(0.3684m, order.Margin);
        Assert.Equal(createdBy, order.CreatedBy);
        Assert.Equal(now, order.CreatedAt);
        Assert.Equal(now.AddMinutes(5), order.UpdatedAt);
        Assert.NotNull(order.Organization);
        Assert.NotNull(order.CreatedByUser);
        Assert.NotNull(order.Items);
        Assert.NotNull(order.Payments);
        Assert.NotNull(order.Approvals);
        Assert.Null(order.DeliveryPlan);
    }

    [Fact]
    public void Order_NavigationCollections_CanAddAndRemoveItems()
    {
        var order = new Order();
        var item = new OrderItem { Id = Guid.NewGuid(), OrderId = order.Id, ItemName = "Silk Dress" };
        var payment = new Payment { Id = Guid.NewGuid(), OrderId = order.Id, Amount = 5000m };
        var approval = new ApprovalQueueEntry { Id = Guid.NewGuid(), OrderId = order.Id, ApprovalType = "high_value_order" };
        var delivery = new DeliveryPlan { Id = Guid.NewGuid(), OrderId = order.Id, DeliveryAddress = "Colombo 03" };

        order.Items.Add(item);
        order.Payments.Add(payment);
        order.Approvals.Add(approval);
        order.DeliveryPlan = delivery;

        Assert.Single(order.Items);
        Assert.Single(order.Payments);
        Assert.Single(order.Approvals);
        Assert.NotNull(order.DeliveryPlan);
        Assert.Equal("Silk Dress", order.Items.First().ItemName);
        Assert.Equal(5000m, order.Payments.First().Amount);
        Assert.Equal("high_value_order", order.Approvals.First().ApprovalType);
    }

    [Fact]
    public void OrderItem_Entity_PropertiesAndDefaults_SetAndGetCorrectly()
    {
        var id = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        var item = new OrderItem
        {
            Id = id,
            OrganizationId = orgId,
            OrderId = orderId,
            ItemId = itemId,
            ItemName = "Cashmere Cardigan",
            Quantity = 2,
            UnitPrice = 7500.00m,
            WholesaleCost = 4500.00m,
            TotalPrice = 15000.00m,
            Organization = new Organization { Id = orgId, Name = "Boutique" },
            Order = new Order { Id = orderId }
        };

        Assert.Equal(id, item.Id);
        Assert.Equal(orgId, item.OrganizationId);
        Assert.Equal(orderId, item.OrderId);
        Assert.Equal(itemId, item.ItemId);
        Assert.Equal("Cashmere Cardigan", item.ItemName);
        Assert.Equal(2, item.Quantity);
        Assert.Equal(7500.00m, item.UnitPrice);
        Assert.Equal(4500.00m, item.WholesaleCost);
        Assert.Equal(15000.00m, item.TotalPrice);
        Assert.NotNull(item.Organization);
        Assert.NotNull(item.Order);
    }

    [Fact]
    public void Payment_Entity_PropertiesAndDefaults_SetAndGetCorrectly()
    {
        var id = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var payment = new Payment
        {
            Id = id,
            OrganizationId = orgId,
            OrderId = orderId,
            Amount = 12000.00m,
            PaymentType = "full",
            PaymentMethod = "online",
            Status = "pending",
            PaymentLink = "https://pay.example.com/inv123",
            GatewayTransactionId = "TXN-9988",
            CreatedAt = now,
            ConfirmedAt = now.AddMinutes(2),
            ExpiresAt = now.AddHours(24),
            Organization = new Organization { Id = orgId, Name = "Boutique" },
            Order = new Order { Id = orderId }
        };

        Assert.Equal(id, payment.Id);
        Assert.Equal(orgId, payment.OrganizationId);
        Assert.Equal(orderId, payment.OrderId);
        Assert.Equal(12000.00m, payment.Amount);
        Assert.Equal("full", payment.PaymentType);
        Assert.Equal("online", payment.PaymentMethod);
        Assert.Equal("pending", payment.Status);
        Assert.Equal("https://pay.example.com/inv123", payment.PaymentLink);
        Assert.Equal("TXN-9988", payment.GatewayTransactionId);
        Assert.Equal(now, payment.CreatedAt);
        Assert.Equal(now.AddMinutes(2), payment.ConfirmedAt);
        Assert.Equal(now.AddHours(24), payment.ExpiresAt);
        Assert.NotNull(payment.Organization);
        Assert.NotNull(payment.Order);
    }

    [Fact]
    public void ApprovalQueueEntry_Entity_PropertiesAndDefaults_SetAndGetCorrectly()
    {
        var id = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var convId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var approval = new ApprovalQueueEntry
        {
            Id = id,
            OrganizationId = orgId,
            OrderId = orderId,
            ApprovalType = "high_value_order",
            Status = "pending",
            ThresholdExceeded = true,
            Reason = "Order exceeds LKR 40,000 threshold",
            DecisionComment = "Approved by manager",
            DecidedBy = userId,
            ThreadId = "thread-agent-1234",
            ConversationId = convId,
            CreatedAt = now,
            DecidedAt = now.AddHours(1),
            Organization = new Organization { Id = orgId, Name = "Boutique" },
            DecidedByUser = new User { Id = userId, Email = "manager@boutique.com" },
            Order = new Order { Id = orderId }
        };

        Assert.Equal(id, approval.Id);
        Assert.Equal(orgId, approval.OrganizationId);
        Assert.Equal(orderId, approval.OrderId);
        Assert.Equal("high_value_order", approval.ApprovalType);
        Assert.Equal("pending", approval.Status);
        Assert.True(approval.ThresholdExceeded);
        Assert.Equal("Order exceeds LKR 40,000 threshold", approval.Reason);
        Assert.Equal("Approved by manager", approval.DecisionComment);
        Assert.Equal(userId, approval.DecidedBy);
        Assert.Equal("thread-agent-1234", approval.ThreadId);
        Assert.Equal(convId, approval.ConversationId);
        Assert.Equal(now, approval.CreatedAt);
        Assert.Equal(now.AddHours(1), approval.DecidedAt);
        Assert.NotNull(approval.Organization);
        Assert.NotNull(approval.DecidedByUser);
        Assert.NotNull(approval.Order);
    }

    [Fact]
    public void DeliveryPlan_Entity_PropertiesAndDefaults_SetAndGetCorrectly()
    {
        var id = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var preferred = DateTime.UtcNow.AddDays(1);
        var eta = DateTime.UtcNow.AddDays(1).AddHours(2);
        var now = DateTime.UtcNow;

        var delivery = new DeliveryPlan
        {
            Id = id,
            OrganizationId = orgId,
            OrderId = orderId,
            CourierService = "PickMe",
            TrackingNumber = "PM-998822",
            DeliveryAddress = "45 Galle Road, Colombo 03",
            PreferredDeliveryTime = preferred,
            RouteOptimized = "{\"route\": [\"A\", \"B\"]}",
            EstimatedCost = 650.00m,
            EstimatedEta = eta,
            Status = "planned",
            CreatedAt = now,
            UpdatedAt = now.AddMinutes(10),
            Organization = new Organization { Id = orgId, Name = "Boutique" },
            Order = new Order { Id = orderId }
        };

        Assert.Equal(id, delivery.Id);
        Assert.Equal(orgId, delivery.OrganizationId);
        Assert.Equal(orderId, delivery.OrderId);
        Assert.Equal("PickMe", delivery.CourierService);
        Assert.Equal("PM-998822", delivery.TrackingNumber);
        Assert.Equal("45 Galle Road, Colombo 03", delivery.DeliveryAddress);
        Assert.Equal(preferred, delivery.PreferredDeliveryTime);
        Assert.Equal("{\"route\": [\"A\", \"B\"]}", delivery.RouteOptimized);
        Assert.Equal(650.00m, delivery.EstimatedCost);
        Assert.Equal(eta, delivery.EstimatedEta);
        Assert.Equal("planned", delivery.Status);
        Assert.Equal(now, delivery.CreatedAt);
        Assert.Equal(now.AddMinutes(10), delivery.UpdatedAt);
        Assert.NotNull(delivery.Organization);
        Assert.NotNull(delivery.Order);
    }

    [Fact]
    public void BusinessRule_Entity_PropertiesAndDefaults_SetAndGetCorrectly()
    {
        var id = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var rule = new BusinessRule
        {
            Id = id,
            OrganizationId = orgId,
            RuleName = "VIP_DISCOUNT_CAP",
            RuleType = "discount",
            RuleValue = "{\"vip_cap\": 0.15}",
            Description = "Cap VIP discount at 15%",
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now.AddDays(1),
            Organization = new Organization { Id = orgId, Name = "Boutique" }
        };

        Assert.Equal(id, rule.Id);
        Assert.Equal(orgId, rule.OrganizationId);
        Assert.Equal("VIP_DISCOUNT_CAP", rule.RuleName);
        Assert.Equal("discount", rule.RuleType);
        Assert.Equal("{\"vip_cap\": 0.15}", rule.RuleValue);
        Assert.Equal("Cap VIP discount at 15%", rule.Description);
        Assert.True(rule.IsActive);
        Assert.Equal(now, rule.CreatedAt);
        Assert.Equal(now.AddDays(1), rule.UpdatedAt);
        Assert.NotNull(rule.Organization);
    }

    [Fact]
    public void Commerce_DTOs_ConstructAndDeconstructCorrectly()
    {
        var createDto = new CreateBusinessRuleDto("HIGH_VAL", "approval_threshold", "{\"threshold\": 50000}", "Desc");
        Assert.Equal("HIGH_VAL", createDto.RuleName);
        Assert.Equal("approval_threshold", createDto.RuleType);
        Assert.Equal("{\"threshold\": 50000}", createDto.RuleValue);
        Assert.Equal("Desc", createDto.Description);

        var updateDto = new UpdateBusinessRuleDto("NEW_VAL", "{\"min\": 0.20}", "New Desc", false);
        Assert.Equal("NEW_VAL", updateDto.RuleName);
        Assert.Equal("{\"min\": 0.20}", updateDto.RuleValue);
        Assert.Equal("New Desc", updateDto.Description);
        Assert.False(updateDto.IsActive);

        var ruleId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var responseDto = new BusinessRuleResponseDto(ruleId, "RULE_A", "discount", "{}", true, "Desc", now, now);
        Assert.Equal(ruleId, responseDto.Id);
        Assert.Equal("RULE_A", responseDto.RuleName);
        Assert.Equal("discount", responseDto.RuleType);
        Assert.True(responseDto.IsActive);

        var reqDto = new EvaluateOrderRulesRequestDto(50000m, 0.30m, 0.05m, "VIP");
        Assert.Equal(50000m, reqDto.OrderTotal);
        Assert.Equal(0.30m, reqDto.Margin);
        Assert.Equal(0.05m, reqDto.RequestedDiscount);
        Assert.Equal("VIP", reqDto.CustomerTier);

        var resDto = new EvaluateOrderRulesResponseDto(
            RequiresApproval: true,
            IsAutoApproved: false,
            MaxAllowedDiscount: 0.10m,
            MinRequiredMargin: 0.25m,
            HighValueThreshold: 40000m,
            Flags: new List<string> { "Margin low" },
            TriggeredRules: new List<string> { "LOW_MARGIN" }
        );
        Assert.False(resDto.IsAutoApproved);
        Assert.True(resDto.RequiresApproval);
        Assert.Contains("LOW_MARGIN", resDto.TriggeredRules);
        Assert.Contains("Margin low", resDto.Flags);
        Assert.Equal(0.10m, resDto.MaxAllowedDiscount);
        Assert.Equal(40000m, resDto.HighValueThreshold);
        Assert.Equal(0.25m, resDto.MinRequiredMargin);
    }
}
