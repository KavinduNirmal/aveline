using Aveline.Api.Modules.Admin.Models;
using Aveline.Api.Modules.Attendance.Models;
using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Commerce.Models;
using Aveline.Api.Modules.Conversations.Models;
using Aveline.Api.Modules.Integrations.Models;
using Aveline.Api.Modules.Notifications.Models;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();

    // Commerce Module (Slice 3)
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<ApprovalQueueEntry> ApprovalQueue => Set<ApprovalQueueEntry>();
    public DbSet<DeliveryPlan> DeliveryPlans => Set<DeliveryPlan>();
    public DbSet<BusinessRule> BusinessRules => Set<BusinessRule>();

    public DbSet<AdminApprovalRequest> AdminApprovalRequests => Set<AdminApprovalRequest>();

    public DbSet<Organization> Organizations => Set<Organization>();

    public DbSet<OrganizationMembership> OrganizationMemberships => Set<OrganizationMembership>();

    public DbSet<OrganizationInvitation> OrganizationInvitations => Set<OrganizationInvitation>();

    public DbSet<AiUsageRecord> AiUsageRecords => Set<AiUsageRecord>();

    public DbSet<UsageAccount> UsageAccounts => Set<UsageAccount>();

    public DbSet<IntegrationCredential> IntegrationCredentials => Set<IntegrationCredential>();

    public DbSet<InboundMessageLog> InboundMessageLogs => Set<InboundMessageLog>();

    public DbSet<TimeEntry> TimeEntries => Set<TimeEntry>();

    public DbSet<NotificationRecord> NotificationRecords => Set<NotificationRecord>();

    public DbSet<NotificationDelivery> NotificationDeliveries => Set<NotificationDelivery>();

    public DbSet<UserDeviceToken> UserDeviceTokens => Set<UserDeviceToken>();

    public DbSet<UserNotification> UserNotifications => Set<UserNotification>();

    public DbSet<Conversation> Conversations => Set<Conversation>();

    public DbSet<Message> Messages => Set<Message>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
